using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Metadata.Configuration;
using Shuffull.Metadata.Models;
using Shuffull.Metadata.Models.AI;
using Shuffull.Metadata.Services.AI;
using Shuffull.Api.Models.Files;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;
using Shuffull.Shared.Tools;
using System.Text.RegularExpressions;

namespace Shuffull.Api.Services;

/// <summary>
/// Re-runs the genre engine for a single song from its ALREADY-STORED inputs (name, artists, lyrics, measured
/// BPM, audio-shape features, authoritative release year) and updates its tags/BPM/energy/TagModel in place —
/// no re-download and no producer round-trip. This is the primitive behind both the "upgrade to a stronger
/// model" pass (re-tag songs whose <see cref="Song.TagModel"/> is weaker per <see cref="Core.Tools.ModelStrengths"/>)
/// and exploratory-mode promotion (tag a kept song on first like).
///
/// Legacy songs imported before provenance capture have null measured-BPM/features/year and enrich text-only —
/// still valid, just without the audio grounding the funnel-tagged songs get.
/// </summary>
public partial class SongEnrichmentService : ISongEnrichmentService
{
    private readonly IServiceProvider _services;

    public SongEnrichmentService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, EnrichmentModel model = EnrichmentModel.Strong, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var resolver = scope.ServiceProvider.GetService<IAIServiceResolver>();
        if (resolver == null)
        {
            return Result.Error<SongEnrichmentStatus>("AI is not enabled; cannot enrich songs.");
        }

        // Weak = the budget tier (audition Keep). The resolver decides WHICH VENDOR serves that tier as well
        // as which model, so moving bulk tagging onto a cheaper provider needs no change here. When no weak
        // model is configured the provider's ResolvedWeakModelName falls back to its strong one, so the
        // request never silently loses quality — it just isn't cheaper.
        //
        // Mapped rather than cast: EnrichmentModel is this app's vocabulary and lives in Core, AiTier is the
        // shared provider abstraction in the Metadata submodule (the funnel maps its own TagTier onto the same
        // thing). Their numeric values are deliberately NOT aligned, so a cast would be silently wrong.
        var tier = model == EnrichmentModel.Weak ? AiTier.Weak : AiTier.Strong;
        var (aiService, modelName) = resolver.Resolve(tier);

        return await EnrichSongCoreAsync(
            context, aiService, modelName,
            MoodsFile.LoadCanonical().Moods, ThemesFile.LoadCanonical().Themes,
            songId, cancellationToken);
    }

    /// <summary>
    /// The full enrichment write, split out (like SongImportService.ImportToDbCoreAsync) so it can be
    /// unit-tested against an in-memory context with a fake engine.
    /// </summary>
    internal static async Task<Result<SongEnrichmentStatus>> EnrichSongCoreAsync(
        ShuffullContext dbContext, IAIService aiService, string modelName,
        List<string> candidateMoods, List<string> candidateThemes,
        string songId, CancellationToken cancellationToken = default!)
    {
        var song = await dbContext.Songs
            .Include(s => s.SongTags)
            .Include(s => s.SongArtists)
            .ThenInclude(sa => sa.Artist)
            .FirstOrDefaultAsync(s => s.SongId == songId, cancellationToken);
        if (song == null)
        {
            return Result.Error<SongEnrichmentStatus>($"Song '{songId}' was not found.");
        }

        // Curator edits always win: a locked song is skipped outright (same contract as re-source replacement).
        if (song.MetadataLocked)
        {
            return Result.Ok(SongEnrichmentStatus.SkippedMetadataLocked);
        }

        var artistNames = song.SongArtists.Select(x => x.Artist.Name).ToList();

        // Shared context: lyrics (definitive for language, strong for mood) + the measured audio-shape block.
        // Legacy songs may have neither; the engine then works from name/artists alone.
        var lyricsContext = BuildLyricsContext(song);
        var audioBlock = AudioFeaturesPromptBlock.Format(song.LoudnessRangeLu, song.CrestFactorDb, song.OnsetsPerSecond);

        // Genre vocabulary comes from the db (seeded from the canonical embedded list), mirroring the import path.
        var allGenres = await dbContext.Genres
            .Include(x => x.GenreRelationsAsMain)
            .ThenInclude(x => x.SubGenre)
            .ToListAsync(cancellationToken);

        var mainGenreNames = allGenres.Where(x => x.IsMain).Select(x => x.Name).ToList();
        // ModelOverride pins each engine call to THIS enrichment's model (weak for an audition Keep, strong
        // for likes/upgrades), so the stamped TagModel below is always what actually ran.
        var mainResult = await aiService.GenerateMainGenresAsync(
            new GenerateMainGenresRequest(song.Name, artistNames, mainGenreNames, lyricsContext, ModelOverride: modelName), cancellationToken);
        if (mainResult.IsError)
        {
            return mainResult.PreserveErrorAs<SongEnrichmentStatus>();
        }
        var mainGenres = mainResult.Get().MainGenres;

        // Sub-genre candidates narrow to the chosen mains' own sub lists, exactly like the import path.
        var subCandidates = allGenres
            .Where(x => mainGenres.Contains(x.Name))
            .SelectMany(x => x.GenreRelationsAsMain)
            .Select(x => x.SubGenre.Name)
            .Distinct()
            .ToList();
        var subResult = await aiService.GenerateSubGenresAsync(
            new GenerateSubGenresRequest(song.Name, artistNames, subCandidates, lyricsContext, ModelOverride: modelName), cancellationToken);
        if (subResult.IsError)
        {
            return subResult.PreserveErrorAs<SongEnrichmentStatus>();
        }
        var subGenres = subResult.Get().SubGenres;

        // Other details get the richest context: chosen genres + audio block + lyrics (mirrors the producer).
        var otherContext = JoinContext(
            mainGenres.Count > 0 ? $"Chosen genres: {string.Join(", ", mainGenres.Concat(subGenres).Distinct())}" : null,
            audioBlock,
            lyricsContext);
        var otherResult = await aiService.GenerateOtherSongDetailsAsync(
            new GenerateOtherSongDetailsRequest(song.Name, artistNames, otherContext, candidateMoods, candidateThemes, song.MeasuredBpm, ModelOverride: modelName),
            cancellationToken);
        if (otherResult.IsError)
        {
            return otherResult.PreserveErrorAs<SongEnrichmentStatus>();
        }
        var other = otherResult.Get();

        // The authoritative MusicBrainz year (persisted at import) beats the model's cultural-era guess —
        // without this, every re-tag would regress a verified era back to an AI estimate.
        var timePeriod = song.OriginalReleaseYear is int year
            ? TimePeriodFormat.FromYear(year)
            : other.TimePeriod;

        var generatedTags = new GeneratedSongTags(mainGenres, subGenres, other.Languages, timePeriod, other.Moods, other.Energy, other.Themes);

        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Rewrite the tag joins wholesale (unlocked songs carry only machine tags, same as the replacement path).
        var produced = generatedTags.ToTagList();
        var knownTags = await dbContext.Tags.ToListAsync(cancellationToken);
        var existingTags = knownTags.Where(x => produced.Any(y => y.Name == x.Name)).ToList();
        var newTags = produced.Where(x => knownTags.All(y => y.Name != x.Name)).ToList();

        dbContext.SongTags.RemoveRange(song.SongTags);
        dbContext.Tags.AddRange(newTags);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var tag in existingTags.Concat(newTags))
        {
            dbContext.SongTags.Add(new SongTag
            {
                SongTagId = IdGenerator.Generate(),
                SongId = song.SongId,
                TagId = tag.TagId
            });
        }

        // Prefer the model's plausible known tempo over the stored measurement (mirrors the funnel's
        // BpmResolver: beat trackers are unreliable on dense electronic music); otherwise keep what we had.
        song.Bpm = other.TrueBpm is >= 40 and <= 300 ? other.TrueBpm : (song.MeasuredBpm ?? song.Bpm);
        song.Energy = other.Energy;
        song.TagModel = modelName;
        // Enriching an exploratory song promotes it: it now has real tags, so it's no longer provisional (and
        // won't be purged if its audition playlist is later deleted).
        song.Exploratory = false;
        song.Version = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok(SongEnrichmentStatus.Enriched);
    }

    /// <summary>
    /// Lyrics block for the prompt, from the lyrics persisted on the Song: instrumental sentinel, else plain
    /// text (falling back to LRC with timestamps stripped), capped at 40 lines / 1500 chars — mirroring the
    /// producer's export-time context so re-tags see equivalent input.
    /// </summary>
    internal static string? BuildLyricsContext(Song song)
    {
        if (song.LyricsInstrumental)
        {
            return "The track is instrumental (no lyrics).";
        }

        var text = !string.IsNullOrWhiteSpace(song.PlainLyrics)
            ? song.PlainLyrics!
            : LrcBrackets().Replace(song.SyncedLyrics ?? string.Empty, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(40)
            .ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        var snippet = string.Join("\n", lines);
        snippet = snippet.Length > 1500 ? snippet[..1500] : snippet;
        return $"Lyrics:\n{snippet}";
    }

    private static string? JoinContext(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join("\n\n", present);
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex LrcBrackets();
}
