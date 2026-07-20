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
using Shuffull.Api.Models.Files;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Api.Tools;
using Shuffull.Api.Tools.SongParsing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
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
    private readonly string[] _mimeImageExtensions = { "image/jpeg", "image/png", "image/gif", "image/bmp" };
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

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
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songImport = await context.SongImports
                .Where(x => x.State == SongImportState.ReadyForImporting)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            if (songImport != null)
            {
                var result = await ImportSongAsync(songImport, cancellationToken);
                if (result.IsError)
                {
                    Console.WriteLine($"Error importing song from SongImport {songImport.SongImportId}: {result.GetError()}");
                }
            }
            else
            {
                await Task.Delay(_interval, cancellationToken);
            }
        }
    }

    [GeneratedRegex("\\.(mp3|wav)$")]
    internal static partial Regex SupportedFileTypes(); // TODO: use

    private async Task<Result> ImportSongAsync(SongImport songImport, CancellationToken cancellationToken = default!)
    {
        var songImportState = SongImportState.Failed;

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
                        return Result.Ok(); // true duplicate — drop it, as before
                    }

                    // Same video, different audio content: the producer re-converted this song (e.g. a new
                    // loudness target after its pipeline state was wiped). Route it through the replacement
                    // path below so the fresh audio is swapped IN PLACE — same SongId, every user association
                    // and (for MetadataLocked songs) every hand-edit preserved — instead of being discarded.
                    songImport.ReplacesSongId = duplicate.SongId;
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
            if (generateTagsResult.IsError)
            {
                songImportState = SongImportState.ReadyForImporting;
                return generateTagsResult.PreserveErrorAs();
            }
            var (existingTags, newTags) = generateTagsResult.Get();

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

            // The producer's generated tags also carry an AI energy score (1-10); persist it on the Song.
            // Null for manual uploads (no producer tags), mirroring how Bpm is producer-only.
            var generatedTags = string.IsNullOrWhiteSpace(songImport.GeneratedTagsJson)
                ? null
                : JsonConvert.DeserializeObject<GeneratedSongTags>(songImport.GeneratedTagsJson);

            var fileExtension = Path.GetExtension(songImport.FileName).ToLowerInvariant();

            if (!string.IsNullOrEmpty(songImport.ReplacesSongId))
            {
                // Replace an existing song in place: overwrite its content + tags but keep its id and every user
                // association (likes, playlists, recently-played). Old fileHash-keyed files are cleaned up after.
                var replaceResult = await ReplaceInDbAsync(songImport, fileHash, fileExtension, lyrics, generatedTags, existingArtists, newArtists, existingTags, newTags, cancellationToken);
                if (replaceResult.IsError)
                {
                    return replaceResult.PreserveErrorAs();
                }
                var (oldFileHash, oldFileExtension) = replaceResult.Get();
                if (!string.Equals(oldFileHash, fileHash, StringComparison.Ordinal))
                {
                    await DeleteReplacedSongFilesAsync(oldFileHash, oldFileExtension, cancellationToken);
                }
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
                    Energy = generatedTags?.Energy,
                    // Tag provenance + raw AI inputs, persisted for a future model-upgrade re-tag (no re-download).
                    // Producer-supplied tags carry the producer's model; when WE generated them (manual uploads,
                    // incl. a file-cached response) stamp our own strong model so the song isn't forever "stale".
                    TagModel = !string.IsNullOrWhiteSpace(songImport.GeneratedTagsJson)
                        ? songImport.TagModel
                        : (existingTags.Count + newTags.Count > 0 ? _selfTagModelName : null),
                    MeasuredBpm = songImport.MeasuredBpm,
                    LoudnessRangeLu = songImport.LoudnessRangeLu,
                    CrestFactorDb = songImport.CrestFactorDb,
                    OnsetsPerSecond = songImport.OnsetsPerSecond,
                    OriginalReleaseYear = songImport.OriginalReleaseYear,
                    // Provisional until the user keeps it; a keep triggers a re-tag which clears this + adds tags.
                    Exploratory = songImport.Exploratory,
                    Version = DateTime.UtcNow
                };
                // Map the producer's "liked on the source" flag to the initial like sentiment.
                var likeStatus = songImport.MarkAsLiked ? LikeStatus.Like : LikeStatus.Neutral;
                var importToDbResult = await ImportToDbAsync(song, existingArtists, newArtists, existingTags, newTags, songImport.UserId, songImport.PlaylistId, songImport.TargetPlaylistName, likeStatus, cancellationToken);
                if (importToDbResult.IsError)
                {
                    return importToDbResult;
                }
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
                var setStateResult = songImportDb.SetState(songImportState);
                if (setStateResult.IsError)
                {
                    Console.WriteLine($"(Should never happen) Failed to set state for SongImport {songImport.SongImportId}: {setStateResult.GetError()}");
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
        Image newAlbumArt;
        var resolution = 512;

        if (musicFile.Tag.Pictures.Length > 0)
        {
            var picture = musicFile.Tag.Pictures[0];
            var mimeType = picture.MimeType;

            if (_mimeImageExtensions.Contains(mimeType))
            {
                using var ms = new MemoryStream(picture.Data.Data);
                using var rawAlbumArt = await Image.LoadAsync(ms, cancellationToken);

                if (rawAlbumArt.Width != resolution || rawAlbumArt.Height != resolution)
                {
                    newAlbumArt = ImageManipulator.ResizeWithPadding(rawAlbumArt, resolution, resolution);
                }
                else
                {
                    newAlbumArt = rawAlbumArt;
                }
            }
            else
            {
                newAlbumArt = ImageManipulator.GenerateDefaultImage(resolution, resolution);
            }
        }
        else
        {
            newAlbumArt = ImageManipulator.GenerateDefaultImage(resolution, resolution);
        }

        var outputFilePath = Path.Combine(_fileConfig.AlbumArtDirectory, $"{fileHash}.jpg");
        using var stream = new MemoryStream();
        newAlbumArt.Save(stream, new JpegEncoder());
        stream.Position = 0; // Reset position
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

        // Add newly generated items to the db
        dbContext.Songs.Add(song);
        dbContext.Artists.AddRange(newArtists);
        dbContext.Tags.AddRange(newTags);
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

        var tags = existingTags.Concat(newTags).ToList();
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
                    IsExploratory = song.Exploratory,
                    Version = DateTime.UtcNow
                };
                dbContext.Playlists.Add(playlist);
            }
        }

        if (playlist != null)
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

        var userSong = new UserSong()
        {
            UserId = userId,
            SongId = song.SongId,
            LastPlayed = DateTime.MinValue,
            Version = DateTime.UtcNow,
            LikeStatus = likeStatus
        };
        dbContext.UserSongs.Add(userSong);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Overwrites an existing song (<see cref="SongImport.ReplacesSongId"/>) in place with the freshly-imported
    /// content — its audio hash, name, lyrics, tempo, energy, external id and tag/artist joins are replaced — but
    /// its SongId, and therefore every UserSong / PlaylistSong association, is preserved. Marks any open
    /// SongReplacement request for the song Completed. Returns the OLD (file hash, extension) so the caller can
    /// delete the now-orphaned files.
    /// </summary>
    private async Task<Result<(string OldFileHash, string OldFileExtension)>> ReplaceInDbAsync(
        SongImport songImport, string fileHash, string fileExtension, SongLyrics? lyrics, GeneratedSongTags? generatedTags,
        List<Artist> existingArtists, List<Artist> newArtists, List<Tag> existingTags, List<Tag> newTags,
        CancellationToken cancellationToken = default!)
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
        // correction. While locked we skip the producer/AI Name/Bpm/Energy + artist/tag overwrite entirely.
        if (!song.MetadataLocked)
        {
            song.Name = songImport.Name;
            song.Bpm = songImport.Bpm;
            song.Energy = generatedTags?.Energy;
            // Refresh the tag provenance + raw AI inputs from this re-source so a later model-upgrade re-tag
            // sees the current model/inputs (and so they don't go stale against the new audio).
            song.TagModel = songImport.TagModel;
            song.MeasuredBpm = songImport.MeasuredBpm;
            song.LoudnessRangeLu = songImport.LoudnessRangeLu;
            song.CrestFactorDb = songImport.CrestFactorDb;
            song.OnsetsPerSecond = songImport.OnsetsPerSecond;
            song.OriginalReleaseYear = songImport.OriginalReleaseYear;

            // Swap the tag/artist joins; add any new master Artist/Tag rows. Remove first (+ save) so the new
            // master rows exist before their joins reference them and the deleted joins can't collide.
            dbContext.SongTags.RemoveRange(song.SongTags);
            dbContext.SongArtists.RemoveRange(song.SongArtists);
            dbContext.Artists.AddRange(newArtists);
            dbContext.Tags.AddRange(newTags);
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
            foreach (var tag in existingTags.Concat(newTags))
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
