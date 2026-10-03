using Newtonsoft.Json;
using Nut.Results;
using Shuffull.Shared.Tools;
using Shuffull.Api.Configuration;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Api.Tools.SongParsing;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Api.Services;

/// <summary>
/// Single intake path for an externally-produced song: derives the display name from the audio's tags,
/// stages the audio under a fresh import folder, and queues a <see cref="SongImport"/> row
/// (ReadyForImporting) for <see cref="SongImportService"/> to process. Shared by the folder-drop importer
/// (<see cref="ExternalSongImporterService"/>) and the HTTP ingestion endpoint so both produce identical
/// SongImport rows from the same <see cref="SongImportDetails"/> contract.
/// </summary>
public class SongImportIntakeService
{
    private readonly ShuffullContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly ShuffullFilesConfiguration _fileConfig;

    public SongImportIntakeService(ShuffullContext context, IFileStorageService fileStorage, IConfiguration configuration)
    {
        _context = context;
        _fileStorage = fileStorage;
        _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>()
            ?? throw new InvalidOperationException("Files configuration is not set.");
    }

    /// <summary>
    /// Stages <paramref name="audioBytes"/> and queues a SongImport row built from <paramref name="details"/>.
    /// Returns the new SongImportId on success.
    /// </summary>
    public async Task<Result<string>> CreateImportAsync(SongImportDetails details, byte[] audioBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            var songImportId = IdGenerator.Generate();

            // Prefer the producer's vetted title (authoritative — it avoids inheriting a MusicBrainz tag-override
            // the producer may have written into the file's ID3). Fall back to the file's own ID3 title, then the
            // external id. Best-effort: a tag-parse failure shouldn't fail the import. The precedence itself lives
            // in SongImportMetadataResolver (pure + unit-tested); only the tag read needs the file.
            string songName;
            if (!string.IsNullOrWhiteSpace(details.Name))
            {
                songName = SongImportMetadataResolver.ResolveSongName(details.Name, null, details.ExternalSongId);
            }
            else
            {
                string? id3Title = null;
                try
                {
                    var abstraction = new ByteArrayAudioFileAbstraction($"{details.ExternalSongId}{details.FileExtension}", audioBytes);
                    var musicFile = TagLib.File.Create(abstraction);
                    id3Title = musicFile.Tag.Title;
                }
                catch
                {
                    id3Title = null;
                }

                songName = SongImportMetadataResolver.ResolveSongName(details.Name, id3Title, details.ExternalSongId);
            }

            var songImport = new SongImport
            {
                SongImportId = songImportId,
                Name = songName,
                ImportFolder = IdGenerator.Generate(),
                UserId = details.TargetUserId,
                PlaylistId = details.TargetPlaylistId,
                FileType = details.FileExtension,
                ExternalSongId = details.ExternalSongId,
                ExternalPlaylistId = details.ExternalPlaylistId,
                ExternalSource = details.ExternalSource,
                // Carry the producer's inferred tags/lyrics/tempo through so SongImportService can persist them
                // instead of re-running its own AI. Null when the producer didn't supply them.
                GeneratedTagsJson = details.GeneratedTags is null ? null : JsonConvert.SerializeObject(details.GeneratedTags),
                LyricsJson = details.Lyrics is null ? null : JsonConvert.SerializeObject(details.Lyrics),
                // Producer's vetted artist list (authoritative); null => SongImportService falls back to ID3 tags.
                ArtistsJson = details.Artists is { Count: > 0 } ? JsonConvert.SerializeObject(details.Artists) : null,
                Bpm = details.Bpm,
                // Provenance + raw AI inputs, carried through so the Song persists them and can be re-tagged
                // with a better model later without re-downloading/re-analysing the audio.
                MeasuredBpm = details.MeasuredBpm,
                TagModel = details.TagModel,
                LoudnessRangeLu = details.LoudnessRangeLu,
                CrestFactorDb = details.CrestFactorDb,
                OnsetsPerSecond = details.OnsetsPerSecond,
                OriginalReleaseYear = details.OriginalReleaseYear,
                Exploratory = details.Exploratory,
                MarkAsLiked = details.MarkAsLiked,
                TargetPlaylistName = details.TargetPlaylistName,
                ReplacesSongId = details.ReplacesSongId,
                LastUpdatedAt = DateTime.UtcNow,
            };

            var filePath = songImport.GetFilePath(_fileConfig.SongImportDirectory);
            using (var stream = new MemoryStream(audioBytes))
            {
                var uploadResult = await _fileStorage.UploadFileAsync(filePath, stream, true, cancellationToken);
                if (uploadResult.IsError)
                {
                    return Result.Error<string>($"Error staging audio for import: {uploadResult.GetError()}");
                }
            }

            _context.SongImports.Add(songImport);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Ok(songImportId);
        }
        catch (Exception ex)
        {
            return Result.Error<string>($"Error creating song import: {ex.Message}");
        }
    }
}
