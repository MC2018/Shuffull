namespace Shuffull.Core.Tools;

public static class Backoff
{
    /// <summary>
    /// The wait before retry number <paramref name="attempt"/> (1-based): <paramref name="first"/>, doubling each
    /// time, never more than <paramref name="max"/>.
    /// </summary>
    public static TimeSpan Doubling(TimeSpan first, int attempt, TimeSpan max)
    {
        // Clamp the shift so a large attempt count can't overflow the ticks before the cap applies.
        var shift = Math.Clamp(attempt - 1, 0, 16);
        return TimeSpan.FromTicks(Math.Min(first.Ticks << shift, max.Ticks));
    }
}
