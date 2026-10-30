using FluentAssertions.Common;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Nut.Results;
using Shuffull.Shared.Enums;
using Shuffull.Shared.Tools;
using Shuffull.Metadata.Models;
using Shuffull.Metadata.Models.AI;
using Shuffull.Metadata.Services.AI;
using Shuffull.Api.Configuration;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tools;
using Shuffull.Core.Models.Files;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Api.Tools;
using Shuffull.Api.Tools.SongParsing;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Tag = Shuffull.Core.Models.Database.Tag;

namespace Shuffull.Api.Services;

/// <summary>
/// Handles logic regarding the downloading, file management, and database importing of songs
/// </summary>
public partial class SongImportService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ShuffullFilesConfiguration _fileConfig;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<SongImportService> _logger;
    private readonly string[] _mimeImageExtensions = { "image/jpeg", "image/png", "image/gif", "image/bmp" };
    /// <summary>How long the loop waits when nothing is due. Settable for tests.</summary>
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxLoopErrorDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="configuration">Configuration</param>
    /// <param name="services">Service provider</param>
    public SongImportService(IConfiguration configuration, IServiceProvider services, ILogger<SongImportService> logger, IFileStorageService fileStorageService)
    {
        _services = services;
        _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new ArgumentNullException(nameof(_fileConfig));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _logger = logger;
        // The strong model this service would SELF-generate tags with (manual uploads / producer omitted tags),
        // stamped as Song.TagModel so self-tagged songs aren't permanently "stale" to the upgrade pass.
        _selfTagModelName = configuration[$"{Shuffull.Metadata.Configuration.OpenAIConfiguration.OpenAIConfigurationSection}:StrongModelName"];
        if (string.IsNullOrWhiteSpace(_selfTagModelName))
        {
            _selfTagModelName = configuration[$"{Shuffull.Metadata.Configuration.OpenAIConfiguration.OpenAIConfigurationSection}:ModelName"];
        }
    }

    private readonly string? _selfTagModelName;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // An exception escaping ExecuteAsync stops the whole API (the host's default BackgroundServiceExceptionBehavior),
        // so a database blip here must only pause the loop, backing off while it lasts.
        var consecutiveErrors = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var imported = await ImportNextAsync(cancellationToken);
                consecutiveErrors = 0;
                if (!imported)
                {
                    await Task.Delay(PollInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var delay = LoopErrorDelay(consecutiveErrors, PollInterval);
                _logger.LogError(ex, "Song import loop failed ({Count} in a row); retrying in {Delay}", consecutiveErrors, delay);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Imports the oldest due <see cref="SongImport"/>, if any. Returns whether there was one.</summary>
    internal async Task<bool> ImportNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var songImport = await NextDueImport(context.SongImports, DateTime.UtcNow)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);
        if (songImport == null)
        {
            return false;
        }

        var result = await ImportSongAsync(songImport, cancellationToken);
        if (result.IsError)
        {
            Console.WriteLine($"Error importing song from SongImport {songImport.SongImportId}: {result.GetError()}");
        }
        return true;
    }

    /// <summary>
    /// Imports ready to run now, oldest first. A row waiting out a tag-generation backoff is skipped, and one that
    /// was just retried has a fresh <see cref="SongImport.LastUpdatedAt"/>, so it goes to the back of the queue.
    /// </summary>
    internal static IQueryable<SongImport> NextDueImport(IQueryable<SongImport> songImports, DateTime now) =>
        songImports
            .Where(x => x.State == SongImportState.ReadyForImporting && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.LastUpdatedAt)
            .ThenBy(x => x.SongImportId);

    /// <summary>The loop's pause after its Nth consecutive error: the poll interval, doubling, capped at 5 minutes.</summary>
    internal static TimeSpan LoopErrorDelay(int consecutiveErrors, TimeSpan interval) =>
        Backoff.Doubling(interval, consecutiveErrors, MaxLoopErrorDelay);

    internal enum TagFailureOutcome
    {
        /// <summary>Back off and try again later; the rest of the queue goes first.</summary>
        Retry,
        /// <summary>A replacement without tags would wipe the song it replaces, so fail it; that song stays as it is.</summary>
        FailImport,
        /// <summary>Tags are an enhancement: import the song untagged rather than lose it. Its TagModel stays null,
        /// so the re-tag sweep (RetagStaleSongs) still finds it.</summary>
        ImportUntagged,
    }

    /// <summary>What a failed tag generation does to <paramref name="songImport"/>.</summary>
    internal static TagFailureOutcome OnTagFailure(SongImport songImport) =>
        !songImport.IsLastTagAttempt ? TagFailureOutcome.Retry
        : !string.IsNullOrEmpty(songImport.ReplacesSongId) ? TagFailureOutcome.FailImport
        : TagFailureOutcome.ImportUntagged;

    [GeneratedRegex("\\.(mp3|wav)$")]
    internal static partial Regex SupportedFileTypes(); // TODO: use

    private async Task<Result> ImportSongAsync(SongImport songImport, CancellationToken cancellationToken = default!)
    {
        var songImportState = SongImportState.Failed;
        var shouldRetryTags = false;
        // The song this import ended up as, recorded on SongImport.SongId once it completes.
        string? resolvedSongId = null;
        // Whether a duplicate was found: the existing song still gets linked to this import's user and playlist.
        var linkToExistingSong = false;

        try
        {
            // Download the file bytes and file hash
            var fileBytesResult = await GetFileBytesAsync(songImport, cancellationToken);
            if (fileBytesResult.IsError)
            {
                return fileBytesResult.PreserveErrorAs();
            }
            var fileBytes = fileBytesResult.Get();
            var fileHash = Hasher.ShaHash(fileBytes).Substring(0, 32);

            // Check for uniqueness — only for brand-new imports. A replacement intentionally reuses an existing
            // song's id and brings new audio/tags, so the dedup-by-hash/external-id guard would reject it.
            if (string.IsNullOrEmpty(songImport.ReplacesSongId))
            {
                var duplicateResult = await FindDuplicateAsync(songImport, fileHash, cancellationToken);
                if (duplicateResult.IsError)
                {
                    return duplicateResult.PreserveErrorAs();
                }
                var duplicate = duplicateResult.Get().Match;
                if (duplicate != null)
                {
                    if (!ShouldRefreshInPlace(duplicate, songImport.ExternalSongId, fileHash, songImport.Exploratory))
                    {
                        // True duplicate: the audio is dropped, but the import was still FOR someone. Give that user
                        // and playlist the existing song, or a second user's import (or a song request) reports
                        // success and the song never appears for them (Shuffull#56). An audition import linked this
                        // way leaves a UserSong that the mix delete doesn't clear when the song is shared or already
                        // kept (Shuffull#87).
                        var linkResult = await LinkDuplicateAsync(duplicate.SongId, songImport, cancellationToken);
                        if (linkResult.IsError)
                        {
                            return linkResult;
                        }
                        resolvedSongId = duplicate.SongId;
                        songImportState = SongImportState.Completed;
                        return Result.Ok();
                    }

                    // Same video, different audio content: the producer re-converted this song (e.g. a new
                    // loudness target after its pipeline state was wiped). Route it through the replacement
                    // path below so the fresh audio is swapped IN PLACE — same SongId, every user association
                    // and (for MetadataLocked songs) every hand-edit preserved — instead of being discarded.
                    songImport.ReplacesSongId = duplicate.SongId;
                    linkToExistingSong = true;
                }
            }

            // Parse artists from the music file
            var abstraction = new ByteArrayAudioFileAbstraction(songImport.FileName, fileBytes);
            var musicFile = TagLib.File.Create(abstraction);
            var parseArtistsResult = await ParseArtistsAsync(songImport, musicFile, cancellationToken);
            if (parseArtistsResult.IsError)
            {
                return parseArtistsResult.PreserveErrorAs();
            }
            var (existingArtists, newArtists) = parseArtistsResult.Get();

            // Save album art
            var saveAlbumArtResult = await SaveAlbumArtAsync(musicFile, fileHash, cancellationToken);
            if (saveAlbumArtResult.IsError)
            {
                return saveAlbumArtResult;
            }

            //Generate tags for the song
            var artistNames = existingArtists.Select(x => x.Name).Concat(newArtists.Select(x => x.Name)).ToList();
            var generateTagsResult = await GetGeneratedSongTagsAsync(songImport, artistNames, fileHash, cancellationToken);
            List<Tag> existingTags, newTags;
            if (!generateTagsResult.IsError)
            {
                (existingTags, newTags) = generateTagsResult.Get();
            }
            else
            {
                switch (OnTagFailure(songImport))
                {
                    case TagFailureOutcome.Retry:
                        shouldRetryTags = true;
                        return generateTagsResult.PreserveErrorAs();
                    case TagFailureOutcome.FailImport:
                        return generateTagsResult.PreserveErrorAs();
                    default:
                        _logger.LogWarning("Importing SongImport {SongImportId} without tags after {Attempts} failed attempts: {Error}",
                            songImport.SongImportId, SongImport.MaxTagAttempts, generateTagsResult.GetError().Message);
                        (existingTags, newTags) = (new List<Tag>(), new List<Tag>());
                        break;
                }
            }

            // Delete the old file if it exists
            var newPath = Path.Combine(_fileConfig.MusicRootDirectory, $"{fileHash}{Path.GetExtension(songImport.FileName)}");
            var moveFileResult = await _fileStorageService.MoveFileAsync(songImport.GetFilePath(_fileConfig.SongImportDirectory), newPath, true, cancellationToken);
            if (moveFileResult.IsError)
            {
                return moveFileResult;
            }

            // Producer-supplied lyrics (carried on the SongImport), persisted onto the Song.
            var lyrics = string.IsNullOrWhiteSpace(songImport.LyricsJson)
                ? null
                : JsonConvert.DeserializeObject<SongLyrics>(songImport.LyricsJson);

            // The producer's generated tags also carry an AI heaviness score (1-10); persist it on the Song.
            // Null for manual uploads (no producer tags), mirroring how Bpm is producer-only.
            var generatedTags = string.IsNullOrWhiteSpace(songImport.GeneratedTagsJson)
                ? null
                : JsonConvert.DeserializeObject<GeneratedSongTags>(songImport.GeneratedTagsJson);

            var fileExtension = Path.GetExtension(songImport.FileName).ToLowerInvariant();

            if (!string.IsNullOrEmpty(songImport.ReplacesSongId))
            {
                // Replace an existing song in place: overwrite its content + tags but keep its id and every user
                // association (likes, playlists, recently-played). Old fileHash-keyed files are cleaned up after.
                var replaceResult = await ReplaceInDbAsync(songImport, fileHash, fileExtension, lyrics, generatedTags, existingArtists, newArtists, existingTags, newTags, linkToExistingSong, cancellationToken);
                if (replaceResult.IsError)
                {
                    return replaceResult.PreserveErrorAs();
                }
                var (oldFileHash, oldFileExtension) = replaceResult.Get();
                if (!string.Equals(oldFileHash, fileHash, StringComparison.Ordinal))
                {
                    await DeleteReplacedSongFilesAsync(oldFileHash, oldFileExtension, cancellationToken);
                }
                resolvedSongId = songImport.ReplacesSongId;
            }
            else
            {
                // Save everything to the db
                var song = new Song
                {
                    SongId = IdGenerator.Generate(),
                    Name = songImport.Name,
                    FileExtension = fileExtension,
                    FileHash = fileHash,
                    ExternalSongId = songImport.ExternalSongId,
                    SyncedLyrics = lyrics?.Synced,
                    PlainLyrics = lyrics?.Plain,
                    LyricsInstrumental = lyrics?.Instrumental ?? false,
                    LyricsSource = lyrics?.Source,
                    Bpm = songImport.Bpm,
                    Heaviness = generatedTags?.Heaviness,
                    // Tag provenance + raw AI inputs, persisted for a future model-upgrade re-tag (no re-download).
                    // Producer-supplied tags carry the producer's model; when WE generated them (manual uploads,
                    // incl. a file-cached response) stamp our own strong model so the song isn't forever "stale".
                    TagModel = !string.IsNullOrWhiteSpace(songImport.GeneratedTagsJson)
                        ? songImport.TagModel
                        : (existingTags.Count + newTags.Count > 0 ? _selfTagModelName : null),
                    TagRevision = ImportedTagRevision(songImport),
                    MeasuredBpm = songImport.MeasuredBpm,
                    LoudnessRangeLu = songImport.LoudnessRangeLu,
                    CrestFactorDb = songImport.CrestFactorDb,
                    OnsetsPerSecond = songImport.OnsetsPerSecond,
                    OriginalReleaseYear = songImport.OriginalReleaseYear,
                    // Provisional until the user keeps it (Keep or Like); ImportToDbCoreAsync clears it up front for
                    // an import that arrives already liked.
                    Exploratory = songImport.Exploratory,
                    Version = DateTime.UtcNow
                };
                var likeStatus = ImportedLikeStatus(songImport);
                var importToDbResult = await ImportToDbAsync(song, existingArtists, newArtists, existingTags, newTags, songImport.UserId, songImport.PlaylistId, songImport.TargetPlaylistName, likeStatus, cancellationToken);
                if (importToDbResult.IsError)
                {
                    return importToDbResult;
                }
                resolvedSongId = song.SongId;
            }

            songImportState = SongImportState.Completed;
            return Result.Ok();
        }
        catch (Exception ex)
        {
            songImportState = SongImportState.Failed;
            return Result.Error(ex);
        }
        finally
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songImportDb = context.SongImports.Find(songImport.SongImportId);
            if (songImportDb != null)
            {
                var setStateResult = shouldRetryTags
                    ? songImportDb.RecordTagFailure(DateTime.UtcNow)
                    : songImportDb.SetState(songImportState);
                if (setStateResult.IsError)
                {
                    Console.WriteLine($"(Should never happen) Failed to set state for SongImport {songImport.SongImportId}: {setStateResult.GetError()}");
                }
                if (resolvedSongId != null)
                {
                    songImportDb.SongId = resolvedSongId;
                }

                context.SongImports.Update(songImportDb);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task<Result<DuplicateProbe>> FindDuplicateAsync(SongImport songImport, string fileHash, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var existingSong = await context.Songs.Where(x => x.FileHash == fileHash || (x.ExternalSongId != null && x.ExternalSongId == songImport.ExternalSongId)).FirstOrDefaultAsync(cancellationToken);
        return Result.Ok(new DuplicateProbe(existingSong));
    }

    /// <summary>The (at most one) existing song a new import collides with; null when the import is unique.</summary>
    internal sealed record DuplicateProbe(Song? Match);

    /// <summary>
    /// Whether a colliding import should REFRESH the existing song's audio in place (via the replacement path)
    /// instead of being dropped as a duplicate. True only for "same source video, different audio content" —
    /// i.e. the producer re-converted the same song (new loudness target, better stream) after losing its
    /// pipeline state. Same-hash re-sends stay idempotent skips, a hash-only collision (same audio under a
    /// different/absent video id) is a true duplicate, and a tag-less exploratory re-rip must never refresh a
    /// song the user has promoted — it would wipe the tags enrichment gave it.
    /// </summary>
    /// <summary>
    /// The prompt revision an import's tags count as: the producer's, when it sent tags. Tags generated here at
    /// import carry no heaviness (it comes only from producer tags), so they count as 0 and the producer
    /// queue fills them in once.
    /// </summary>
    internal static int ImportedTagRevision(SongImport songImport) =>
        string.IsNullOrWhiteSpace(songImport.GeneratedTagsJson) ? 0 : songImport.TagRevision;

    /// <summary>The producer's "liked on the source" flag as the user's initial like sentiment.</summary>
    internal static LikeStatus ImportedLikeStatus(SongImport songImport) =>
        songImport.MarkAsLiked ? LikeStatus.Like : LikeStatus.Neutral;

    internal static bool ShouldRefreshInPlace(Song duplicate, string? importExternalSongId, string importFileHash, bool importExploratory)
    {
        var sameVideoNewAudio = !string.IsNullOrEmpty(importExternalSongId)
            && duplicate.ExternalSongId == importExternalSongId
            && !string.Equals(duplicate.FileHash, importFileHash, StringComparison.Ordinal);
        var wouldWipePromotedTags = importExploratory && !duplicate.Exploratory;
        return sameVideoNewAudio && !wouldWipePromotedTags;
    }

    private async Task<Result<byte[]>> GetFileBytesAsync(SongImport songImport, CancellationToken cancellationToken = default!)
    {
        var fileBytesResult = await _fileStorageService.DownloadFileBytesAsync(songImport.GetFilePath(_fileConfig.SongImportDirectory), cancellationToken);
        if (fileBytesResult.IsError)
        {
            return fileBytesResult.PreserveErrorAs<byte[]>();
        }
        var fileBytes = fileBytesResult.Get();
        return Result.Ok(fileBytes);
    }

    /// <summary>
    /// Parses artists from the music file
    /// </summary>
    /// <param name="songImport"></param>
    /// <param name="fileBytes"></param>
    /// <returns>Existing artists and new artists, in that order</returns>
    private async Task<Result<Tuple<List<Artist>, List<Artist>>>> ParseArtistsAsync(SongImport songImport, TagLib.File musicFile, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        // Prefer the producer's vetted artist list (authoritative — avoids a MusicBrainz tag-override that can
        // collapse a multi-artist collab into one credit string in the file's ID3). Fall back to the ID3
        // performers for manual uploads / older payloads that didn't carry artists. The precedence itself lives
        // in SongImportMetadataResolver (pure + unit-tested).
        var performerNames = SongImportMetadataResolver.ResolveArtistNames(songImport.ArtistsJson, musicFile.Tag.Performers);
        var existingArtists = new List<Artist>();
        var newArtists = new List<Artist>();

        for (int i = 0; i < performerNames.Length; i++)
        {
            var performerName = performerNames[i];
            var nextPerformerName = performerNames.Length > i + 1 ? performerNames[i + 1] : string.Empty;

            if (performerName.EndsWith("AC") && nextPerformerName.StartsWith("DC"))
            {
                performerName += $"/{nextPerformerName}";
                i++;
            }

            // TODO: try figuring out a better way of pulling this data from SQL
            var artist = await context.Artists.Where(a => a.Name == performerName).FirstOrDefaultAsync(cancellationToken);

            if (artist != null)
            {
                existingArtists.Add(artist);
            }
            else
            {
                artist = new Artist()
                {
                    ArtistId = IdGenerator.Generate(),
                    Name = performerName
                };
                newArtists.Add(artist);
            }
        }

        return Result.Ok(Tuple.Create(existingArtists, newArtists));
    }

    private async Task<Result> SaveAlbumArtAsync(TagLib.File musicFile, string fileHash, CancellationToken cancellationToken = default!)
    {
        var resolution = 512;
        var picture = musicFile.Tag.Pictures.FirstOrDefault();
        var imageBytes = picture != null && _mimeImageExtensions.Contains(picture.MimeType) ? picture.Data.Data : null;

        var jpegResult = ImageManipulator.ToAlbumArtJpeg(imageBytes, resolution);
        if (jpegResult.IsError)
        {
            return Result.Error($"{jpegResult.GetError().Message} ({picture?.MimeType})");
        }

        var outputFilePath = Path.Combine(_fileConfig.AlbumArtDirectory, $"{fileHash}.jpg");
        using var stream = new MemoryStream(jpegResult.Get());
        await _fileStorageService.UploadFileAsync(outputFilePath, stream, true, cancellationToken);
        return Result.Ok();
    }

    private async Task<Result<Tuple<List<Tag>, List<Tag>>>> GetGeneratedSongTagsAsync(SongImport songImport, List<string> artistNames, string fileHash, CancellationToken cancellationToken = default!)
    {
        // Exploratory imports arrive untagged on purpose - the user auditions them first, and a keep triggers a
        // re-tag (SongEnrichmentService) that adds real tags + clears the flag. Spend no AI (producer or self).
        if (songImport.Exploratory)
        {
            return Result.Ok(Tuple.Create(new List<Tag>(), new List<Tag>()));
        }

        using var scope = _services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var aiService = scope.ServiceProvider.GetService<IAIService>();
        // Genre-context strings for the AI requests below; left null now that Shuffull no longer re-fetches
        // YouTube video features (the producer supplies tags on the fast path; manual uploads self-generate).
        string? mainGenresContext = null, subGenresContext = null, otherDetailsContext = null;

        // Fast path: the external producer already inferred genre/era/language tags and carried them on the
        // SongImport. Use them directly and skip our own AI generation below. Only manual uploads (no producer
        // tags) fall through to self-generation.
        if (!string.IsNullOrWhiteSpace(songImport.GeneratedTagsJson))
        {
            var producerTags = JsonConvert.DeserializeObject<GeneratedSongTags>(songImport.GeneratedTagsJson);
            if (producerTags != null)
            {
                var knownTags = await dbContext.Tags.AsNoTracking().ToListAsync(cancellationToken);
                var produced = producerTags.ToTagList();
                var existing = knownTags.Where(x => produced.Any(y => y.Name == x.Name)).ToList();
                var fresh = produced.Where(x => knownTags.All(y => y.Name != x.Name)).ToList();
                return Result.Ok(Tuple.Create(existing, fresh));
            }
        }

        var allTags = await dbContext.Tags.AsNoTracking().ToListAsync(cancellationToken);
        var allGenres = await dbContext.Genres
            .AsNoTracking()
            .Include(x => x.GenreRelationsAsMain)
            .ThenInclude(x => x.SubGenre)
            .ToListAsync(cancellationToken); // TODO: specification, you need genre relations as main here
        GeneratedSongTags? generatedSongTags = null;
        List<Tag> existingTags, newTags;

        // Load in existing tags if saved before
        var aiResponseFilePath = Path.Combine(_fileConfig.SavedAiResponsesDirectory, $"{fileHash}.json");
        var generatedSongTagsResult = await _fileStorageService.DownloadSerializableObjectAsync<GeneratedSongTags>(aiResponseFilePath, cancellationToken);
        if (!generatedSongTagsResult.IsError)
        {
            generatedSongTags = generatedSongTagsResult.Get();
        }

        // Run AI service if no tags saved before
        else if (generatedSongTags == null && aiService != null)
        {
            // Main genres
            var allMainGenreNames = allGenres.Where(x => x.IsMain).Select(x => x.Name).ToList();
            var mainGenresRequest = new GenerateMainGenresRequest(songImport.Name, artistNames, allMainGenreNames, mainGenresContext);
            var mainGenresResult = await aiService.GenerateMainGenresAsync(mainGenresRequest, cancellationToken);
            if (mainGenresResult.IsError)
            {
                return mainGenresResult.PreserveErrorAs<Tuple<List<Tag>, List<Tag>>>();
            }
            var mainGenresResponse = mainGenresResult.Get();

            // Sub genres
            var selectedMainGenres = allGenres.Where(x => mainGenresResponse.MainGenres.Contains(x.Name)).ToList();
            var allSubGenreNames = selectedMainGenres.SelectMany(x => x.GenreRelationsAsMain).Select(x => x.SubGenre.Name).ToList();
            var subGenresRequest = new GenerateSubGenresRequest(songImport.Name, artistNames, allSubGenreNames, subGenresContext);
            var subGenresResult = await aiService.GenerateSubGenresAsync(subGenresRequest, cancellationToken);
            if (subGenresResult.IsError)
            {
                return subGenresResult.PreserveErrorAs<Tuple<List<Tag>, List<Tag>>>();
            }
            var subGenresResponse = subGenresResult.Get();

            // Other song details
            var otherSongDetailsRequest = new GenerateOtherSongDetailsRequest(songImport.Name, artistNames, otherDetailsContext);
            var otherSongDetailsResult = await aiService.GenerateOtherSongDetailsAsync(otherSongDetailsRequest, cancellationToken);
            if (otherSongDetailsResult.IsError)
            {
                return otherSongDetailsResult.PreserveErrorAs<Tuple<List<Tag>, List<Tag>>>();
            }
            var otherSongDetailsResponse = otherSongDetailsResult.Get();

            // Save generated tags
            generatedSongTags = new GeneratedSongTags(mainGenresResponse.MainGenres, subGenresResponse.SubGenres, otherSongDetailsResponse.Languages, otherSongDetailsResponse.TimePeriod);
            await _fileStorageService.UploadSerializableObjectAsync(aiResponseFilePath, generatedSongTags, true, cancellationToken);
        }

        // If no tags generated, return empty lists
        if (generatedSongTags == null)
        {
            return Result.Ok(Tuple.Create(new List<Tag>(), new List<Tag>()));
        }

        var generatedTags = generatedSongTags.ToTagList();
        existingTags = allTags.Where(x => generatedTags.Select(y => y.Name).Contains(x.Name)).ToList();
        newTags = generatedTags.Where((x) => !allTags.Select(y => y.Name).Contains(x.Name)).ToList();

        return Result.Ok(Tuple.Create(existingTags, newTags));
    }

    private async Task<Result> ImportToDbAsync(Song song, List<Artist> existingArtists, List<Artist> newArtists, List<Tag> existingTags, List<Tag> newTags, string userId, string? playlistId, string? playlistName, LikeStatus likeStatus, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        return await ImportToDbCoreAsync(dbContext, song, existingArtists, newArtists, existingTags, newTags, userId, playlistId, playlistName, likeStatus, cancellationToken);
    }

    /// <summary>
    /// The transactional db write for a brand-new song import (song + artist/tag joins + target-playlist
    /// resolution + UserSong). Split out from <see cref="ImportToDbAsync"/>, which owns the DI scope, so this
    /// half can be unit-tested against an in-memory <see cref="ShuffullContext"/> without the background host.
    /// Behavior is identical to the original inline body.
    /// </summary>
    internal static async Task<Result> ImportToDbCoreAsync(ShuffullContext dbContext, Song song, List<Artist> existingArtists, List<Artist> newArtists, List<Tag> existingTags, List<Tag> newTags, string userId, string? playlistId, string? playlistName, LikeStatus likeStatus, CancellationToken cancellationToken = default!)
    {
        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A liked song is a promoted song: never create one that is both Exploratory and liked (Shuffull#38). The
        // like is the user's decision to keep it, and an Exploratory flag would leave it purge-eligible until some
        // later, optional job cleared it. The import's audition INTENT is still what decides a new target
        // playlist's status below, so one liked song can't turn a whole audition playlist into an ordinary one.
        // LinkImportTargetAsync applies the same rule to an existing song a duplicate import links.
        var importedForAudition = song.Exploratory;
        if (song.Exploratory && likeStatus.RetainsSong())
        {
            song.Exploratory = false;
        }

        // Add newly generated items to the db. Tags were split into existing and new before the file moved, so a
        // name another writer has created since is reused here rather than failing the import (Shuffull#82).
        dbContext.Songs.Add(song);
        dbContext.Artists.AddRange(newArtists);
        var tags = await TagStore.GetOrCreateAsync(dbContext, existingTags.Concat(newTags), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Add relationships to the db
        var artists = existingArtists.Concat(newArtists).ToList();
        foreach (var artist in artists)
        {
            var songArtist = new SongArtist
            {
                SongArtistId = IdGenerator.Generate(),
                SongId = song.SongId,
                ArtistId = artist.ArtistId
            };
            dbContext.SongArtists.Add(songArtist);
        }

        foreach (var tag in tags)
        {
            var songTag = new SongTag
            {
                SongTagId = IdGenerator.Generate(),
                SongId = song.SongId,
                TagId = tag.TagId
            };
            dbContext.SongTags.Add(songTag);
        }

        await LinkImportTargetAsync(dbContext, song, userId, playlistId, playlistName, importedForAudition, likeStatus, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    private async Task<Result> LinkDuplicateAsync(string songId, SongImport songImport, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        return await LinkDuplicateCoreAsync(dbContext, songId, songImport, cancellationToken);
    }

    /// <summary>
    /// Applies a dropped duplicate import's target to the song it matched: the importing user's UserSong and the
    /// target playlist's PlaylistSong, each only if missing. The existing song's audio, tags and metadata are left
    /// alone; a liked import only promotes it out of Exploratory. Split out from <see cref="LinkDuplicateAsync"/>,
    /// which owns the DI scope, so it can be tested against an in-memory <see cref="ShuffullContext"/>.
    /// </summary>
    internal static async Task<Result> LinkDuplicateCoreAsync(ShuffullContext dbContext, string songId, SongImport songImport, CancellationToken cancellationToken = default!)
    {
        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var song = await dbContext.Songs.FindAsync([songId], cancellationToken: cancellationToken);
        if (song == null)
        {
            return Result.Error($"Cannot link duplicate import {songImport.SongImportId}: song '{songId}' was not found.");
        }

        var likeStatus = ImportedLikeStatus(songImport);
        await LinkImportTargetAsync(dbContext, song, songImport.UserId, songImport.PlaylistId, songImport.TargetPlaylistName, songImport.Exploratory, likeStatus, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Stages an import's target for <paramref name="song"/>, without saving: resolves (or creates) the target
    /// playlist and adds the song to it, and gives the user a UserSong. Each link is added only if it is missing,
    /// so a song that already exists (a duplicate import) is linked without doubling anything up. A playlist or
    /// UserSong that gains the song gets a fresh Version, so clients sync it.
    /// </summary>
    /// <param name="importedForAudition">The import's audition intent; decides a newly created playlist's status.</param>
    internal static async Task LinkImportTargetAsync(ShuffullContext dbContext, Song song, string userId, string? playlistId, string? playlistName, bool importedForAudition, LikeStatus likeStatus, CancellationToken cancellationToken = default!)
    {
        // Resolve the target playlist: prefer an existing one by id; otherwise, if a name was provided,
        // reuse a same-named playlist for this user (idempotent across imports) or create one attached to them.
        Playlist? playlist = null;
        if (!string.IsNullOrEmpty(playlistId))
        {
            playlist = await dbContext.Playlists.FindAsync([playlistId], cancellationToken: cancellationToken);
        }

        if (playlist == null && !string.IsNullOrWhiteSpace(playlistName))
        {
            playlist = await dbContext.Playlists
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Name == playlistName, cancellationToken);

            if (playlist == null)
            {
                playlist = new Playlist
                {
                    PlaylistId = string.IsNullOrWhiteSpace(playlistId) ? IdGenerator.Generate() : playlistId,
                    UserId = userId,
                    Name = playlistName,
                    CurrentSongId = null,
                    PercentUntilReplayable = 0.9m,
                    // The first song imported into a freshly-created playlist decides its audition status: an
                    // exploratory source imports its songs untagged into a dedicated target playlist, so that
                    // playlist becomes an audition playlist (and a later delete can purge the un-kept songs).
                    IsExploratory = importedForAudition,
                    Version = DateTime.UtcNow
                };
                dbContext.Playlists.Add(playlist);
            }
        }

        if (playlist != null)
        {
            var alreadyInPlaylist = await dbContext.PlaylistSongs
                .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId, cancellationToken);
            if (!alreadyInPlaylist)
            {
                var playlistSong = new PlaylistSong
                {
                    PlaylistSongId = IdGenerator.Generate(),
                    PlaylistId = playlist.PlaylistId,
                    SongId = song.SongId
                };
                dbContext.PlaylistSongs.Add(playlistSong);
                // Bump the playlist's version so clients re-fetch its (now-changed) song list. Without this, a
                // playlist that gains songs after its first sync stays frozen on the client — it only re-pulls a
                // playlist whose server Version increased — so newly-added songs never appear in it.
                playlist.Version = DateTime.UtcNow;
            }
        }

        // A user who already has the song keeps their UserSong as it is: a re-import must not overwrite a
        // like/dislike or play history they set here.
        var hasUserSong = await dbContext.UserSongs
            .AnyAsync(us => us.UserId == userId && us.SongId == song.SongId, cancellationToken);
        if (!hasUserSong)
        {
            var userSong = new UserSong()
            {
                UserId = userId,
                SongId = song.SongId,
                LastPlayed = DateTime.MinValue,
                Version = DateTime.UtcNow,
                LikeStatus = likeStatus
            };
            dbContext.UserSongs.Add(userSong);

            // A like is a decision to keep the song (Shuffull#38): never leave a liked song Exploratory. This only
            // ever PROMOTES an existing song; an audition import never demotes a kept one.
            if (song.Exploratory && likeStatus.RetainsSong())
            {
                song.Exploratory = false;
                song.Version = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Overwrites an existing song (<see cref="SongImport.ReplacesSongId"/>) in place with the freshly-imported
    /// content — its audio hash, name, lyrics, tempo, heaviness, external id and tag/artist joins are replaced — but
    /// its SongId, and therefore every UserSong / PlaylistSong association, is preserved. Marks any open
    /// SongReplacement request for the song Completed. Returns the OLD (file hash, extension) so the caller can
    /// delete the now-orphaned files.
    /// </summary>
    private async Task<Result<(string OldFileHash, string OldFileExtension)>> ReplaceInDbAsync(
        SongImport songImport, string fileHash, string fileExtension, SongLyrics? lyrics, GeneratedSongTags? generatedTags,
        List<Artist> existingArtists, List<Artist> newArtists, List<Tag> existingTags, List<Tag> newTags,
        bool linkImportTarget, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var song = await dbContext.Songs
            .Include(s => s.SongTags)
            .Include(s => s.SongArtists)
            .FirstOrDefaultAsync(s => s.SongId == songImport.ReplacesSongId, cancellationToken);
        if (song == null)
        {
            return Result.Error<(string, string)>($"Cannot replace: song '{songImport.ReplacesSongId}' was not found.");
        }

        var oldFileHash = song.FileHash;
        var oldFileExtension = song.FileExtension;

        // Always re-source the AUDIO in place (this is the whole point of a replacement) and the lyrics, which
        // are tied to the specific audio (LRC is trim-shifted for it). SongId + user associations are untouched.
        song.FileExtension = fileExtension;
        song.FileHash = fileHash;
        song.ExternalSongId = songImport.ExternalSongId;
        song.SyncedLyrics = lyrics?.Synced;
        song.PlainLyrics = lyrics?.Plain;
        song.LyricsInstrumental = lyrics?.Instrumental ?? false;
        song.LyricsSource = lyrics?.Source;
        song.Version = DateTime.UtcNow;

        // Curator-protected metadata: once a human has hand-edited this song, a re-source must NOT clobber their
        // correction. While locked we skip the producer/AI Name/Bpm/Heaviness + artist/tag overwrite entirely.
        if (!song.MetadataLocked)
        {
            song.Name = songImport.Name;
            song.Bpm = songImport.Bpm;
            song.Heaviness = generatedTags?.Heaviness;
            // Refresh the tag provenance + raw AI inputs from this re-source so a later model-upgrade re-tag
            // sees the current model/inputs (and so they don't go stale against the new audio).
            song.TagModel = songImport.TagModel;
            song.TagRevision = ImportedTagRevision(songImport);
            song.ResetTagAttempts();
            song.MeasuredBpm = songImport.MeasuredBpm;
            song.LoudnessRangeLu = songImport.LoudnessRangeLu;
            song.CrestFactorDb = songImport.CrestFactorDb;
            song.OnsetsPerSecond = songImport.OnsetsPerSecond;
            song.OriginalReleaseYear = songImport.OriginalReleaseYear;

            // Swap the tag/artist joins. New Artist rows are added and saved, with the old joins removed, before the
            // new joins reference them. Tags come from TagStore, which reuses a name created since the split.
            var tags = await TagStore.GetOrCreateAsync(dbContext, existingTags.Concat(newTags), cancellationToken);
            dbContext.SongTags.RemoveRange(song.SongTags);
            dbContext.SongArtists.RemoveRange(song.SongArtists);
            dbContext.Artists.AddRange(newArtists);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var artist in existingArtists.Concat(newArtists))
            {
                dbContext.SongArtists.Add(new SongArtist
                {
                    SongArtistId = IdGenerator.Generate(),
                    SongId = song.SongId,
                    ArtistId = artist.ArtistId
                });
            }
            foreach (var tag in tags)
            {
                dbContext.SongTags.Add(new SongTag
                {
                    SongTagId = IdGenerator.Generate(),
                    SongId = song.SongId,
                    TagId = tag.TagId
                });
            }
        }

        // Resolve any open replacement request(s) for this song.
        var openRequests = await dbContext.SongReplacements
            .Where(r => r.SongId == song.SongId && r.Status != SongReplacementStatus.Completed)
            .ToListAsync(cancellationToken);
        foreach (var request in openRequests)
        {
            request.Status = SongReplacementStatus.Completed;
            request.ResolvedAt = DateTime.UtcNow;
        }

        // A refresh in place found by duplicate detection (not a curator's replacement request) was still an import
        // for this user and playlist, so link them in the same commit, as a dropped duplicate would be.
        if (linkImportTarget)
        {
            var likeStatus = ImportedLikeStatus(songImport);
            await LinkImportTargetAsync(dbContext, song, songImport.UserId, songImport.PlaylistId, songImport.TargetPlaylistName, songImport.Exploratory, likeStatus, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok((oldFileHash, oldFileExtension));
    }

    // Best-effort cleanup of a replaced song's old fileHash-keyed files (audio + album art) once the new version
    // (new hash) is in place. DeleteFileAsync no-ops on a missing file; failures are logged, not fatal.
    private async Task DeleteReplacedSongFilesAsync(string oldFileHash, string oldFileExtension, CancellationToken cancellationToken)
    {
        var paths = new[]
        {
            Path.Combine(_fileConfig.MusicRootDirectory, $"{oldFileHash}{oldFileExtension}"),
            Path.Combine(_fileConfig.AlbumArtDirectory, $"{oldFileHash}.jpg"),
        };
        foreach (var path in paths)
        {
            var deleteResult = await _fileStorageService.DeleteFileAsync(path, cancellationToken);
            if (deleteResult.IsError)
            {
                Console.WriteLine($"Warning: failed to delete replaced song file '{path}': {deleteResult.GetError()}");
            }
        }
    }
}
