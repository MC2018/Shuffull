using System.Linq.Expressions;
using Shuffull.Core.Models.Database;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tools;

/// <summary>
/// The one answer to "would tags from this model be an upgrade on this song?", by model strength or by prompt
/// revision. The producer's work queue
/// (GetPendingTagSongs) asks it with the tier's target model, and the write-back (ApplySongTags) asks it with the
/// model that actually ran. They used to differ: the write only checked locked songs, so a funnel running a
/// weaker model than the site's tier target had every write accepted, left the song still behind, and got it
/// handed straight back, bumping Version each time so every client re-synced it (Shuffull#40).
///
/// A song with no TagModel takes any model unless a curator locked it: UpdateSong never stamps TagModel, so a
/// locked song with none was tagged by a person and has nothing to upgrade FROM. Otherwise the model must be
/// strictly stronger than the one that produced the current tags.
///
/// Two forms of the same rule, kept side by side. ModelStrengths is a config map the database knows nothing
/// about, so the SQL form takes the strength comparison precomputed into a set of names.
/// </summary>
public static class TagUpgradeRule
{
    /// <summary>
    /// SQL form. <paramref name="atLeastAsStrongAsModel"/> is <see cref="ModelStrengths.ModelsAtLeastAsStrongAs"/>
    /// of the model being considered, so a TagModel outside it is weaker. As that method requires, callers must
    /// first short-circuit when the model is unregistered. Names compare case-insensitively, as ModelStrengths does.
    /// </summary>
    public static Expression<Func<Song, bool>> WouldUpgrade(IReadOnlyList<string> atLeastAsStrongAsModel)
    {
        var names = atLeastAsStrongAsModel.Select(n => n.ToLowerInvariant()).ToList();
        return s => s.TagModel == null ? !s.MetadataLocked : !names.Contains(s.TagModel.ToLower());
    }

    /// <summary>The same rule for one loaded song.</summary>
    public static bool WouldUpgrade(Song song, string? model, ModelStrengths strengths) =>
        song.TagModel == null
            ? !song.MetadataLocked
            : strengths.GetStrength(model) > strengths.GetStrength(song.TagModel);

    /// <summary>
    /// SQL form of "machine tags from an older prompt revision": the song is re-scored once with a model no weaker
    /// than the one that tagged it. A song with no TagModel is already covered by <see cref="WouldUpgrade(IReadOnlyList{string})"/>.
    /// A curator-locked song is left alone: UpdateSong sets its tags and Energy by hand while keeping the old
    /// TagModel, so a re-score would overwrite a human's decision with a prompt change.
    /// </summary>
    public static Expression<Func<Song, bool>> IsBehindRevision(int currentRevision) =>
        s => s.TagModel != null && !s.MetadataLocked && s.TagRevision < currentRevision;

    /// <summary>
    /// Write-time form: tags from <paramref name="model"/> under <paramref name="revision"/> re-score the song when
    /// they come from a newer revision and a model at least as strong as the current one. Equal strength is
    /// allowed here, unlike <see cref="WouldUpgrade(Song, string?, ModelStrengths)"/>, because the revision is the
    /// upgrade; a weaker model is not, or a re-score would quietly downgrade the song's provenance.
    /// </summary>
    public static bool WouldRescore(Song song, string? model, int revision, ModelStrengths strengths) =>
        song.TagModel != null
        && !song.MetadataLocked
        && revision > song.TagRevision
        && strengths.GetStrength(model) >= strengths.GetStrength(song.TagModel);
}
