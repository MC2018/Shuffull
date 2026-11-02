using System.Threading.RateLimiting;

namespace Shuffull.Api.Services.SourceSearch;

/// <summary>
/// A per-user cap on song searches. Each one becomes a source search on the producer's side, through an egress the
/// source already blocks often, so one client (a search box without a debounce, say) mustn't be able to flood it.
/// Each user gets a burst of <c>burst</c> searches, then one more per <c>perSearch</c>. Over that, a 429.
/// </summary>
public sealed class SourceSearchRateLimiter(int burst = SourceSearchRateLimiter.DefaultBurst, TimeSpan? perSearch = null) : IDisposable
{
    public const int DefaultBurst = 10;
    public const string LimitedMessage = "Too many searches. Wait a moment and try again.";

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
        RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = perSearch ?? TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    /// <summary>True when <paramref name="userId"/> may search now; that search is counted.</summary>
    public bool TryAcquire(string userId)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
