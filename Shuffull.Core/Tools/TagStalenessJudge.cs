using Microsoft.Extensions.Configuration;
using Shuffull.Core.Models.Database;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tools;

/// <summary>
/// Answers "would the current strong model consider this song's tags stale?" for API projections — the same
/// decision <see cref="Features.Songs.RetagStaleSongs.RetagStaleSongsHandler"/> makes in SQL, packaged so sync
/// payloads can flag upgradable songs to the app (which promotes them on like, exactly like exploratory songs).
///
/// Inherits the <see cref="ModelStrengths"/> fail-safe: when the current strong model is unregistered
/// (strength 0), nothing is ever stale. Exploratory songs are never flagged (they have their own promote flow
/// and deliberately carry no tags), and curator-locked songs are never flagged (enrichment would skip them, so
/// flagging would only generate no-op retag requests on every like).
/// </summary>
public sealed class TagStalenessJudge
{
    private readonly HashSet<string>? _strongEnough;

    public TagStalenessJudge(ModelStrengths strengths, IConfiguration configuration)
    {
        var strongModel = configuration["AI:OpenAI:StrongModelName"];
        if (string.IsNullOrWhiteSpace(strongModel))
        {
            strongModel = configuration["AI:OpenAI:ModelName"];
        }

        _strongEnough = strengths.GetStrength(strongModel) > 0
            ? new HashSet<string>(strengths.ModelsAtLeastAsStrongAs(strongModel), StringComparer.OrdinalIgnoreCase)
            : null;
    }

    /// <summary>True when a like should trigger a strong-model re-tag of <paramref name="song"/>.</summary>
    public bool IsStale(Song song) =>
        _strongEnough is not null
        && !song.Exploratory
        && !song.MetadataLocked
        && (song.TagModel is null || !_strongEnough.Contains(song.TagModel));
}
