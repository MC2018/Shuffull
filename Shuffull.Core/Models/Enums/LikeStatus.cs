namespace Shuffull.Core.Models.Enums;

/// <summary>
/// A user's sentiment toward a song, driving shuffle/playback weighting:
/// <list type="bullet">
/// <item><see cref="Neutral"/> — the default (no opinion); normal weight.</item>
/// <item><see cref="Like"/> — a mild positive (e.g. an imported "liked on the source" signal).</item>
/// <item><see cref="Love"/> — a favorite; surfaced + weighted higher.</item>
/// <item><see cref="Dislike"/> — "never play again"; excluded from shuffle/auto-play entirely.</item>
/// </list>
/// (Enforcing the weighting/exclusion lives in the playback client; this just records the sentiment.)
/// </summary>
public enum LikeStatus
{
    Neutral = 0,
    Like = 1,
    Love = 2,
    Dislike = 3,
}
