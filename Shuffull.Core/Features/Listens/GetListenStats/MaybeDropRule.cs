namespace Shuffull.Core.Features.Listens.GetListenStats;

/// <summary>
/// The "maybe drop this" rule (shuffull-workspace#35, item 6): a song is flagged when the user skipped it in under
/// <see cref="SkippedUnderSeconds"/> on at least <see cref="Skips"/> of its last <see cref="OfLastPlays"/> plays.
/// The flag is informational: nothing acts on it.
///
/// <para>This is the only place the numbers live. <see cref="Default"/> is the default proposed on Shuffull#61,
/// still waiting on the user's confirmation; change it here.</para>
/// </summary>
/// <param name="SkippedUnderSeconds">A skip counts only if less than this much played (strictly under).</param>
/// <param name="Skips">How many such skips flag the song.</param>
/// <param name="OfLastPlays">How many of the song's most recent plays are looked at.</param>
public record MaybeDropRule(double SkippedUnderSeconds, int Skips, int OfLastPlays)
{
    /// <summary>Skipped in under 30 s on 3 of the last 5 plays.</summary>
    public static readonly MaybeDropRule Default = new(SkippedUnderSeconds: 30, Skips: 3, OfLastPlays: 5);
}
