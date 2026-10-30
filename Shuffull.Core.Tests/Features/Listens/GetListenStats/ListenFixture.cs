using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Tests.Features.Listens.GetListenStats;

/// <summary>
/// Writes listens for the stats tests, each one minute after the last, so the order they are added in is the order
/// they were played in.
/// </summary>
internal sealed class ListenFixture(string userId)
{
    public static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private int _count;

    public DateTime LastStartedAt { get; private set; }

    public static User NewUser() => new()
    {
        UserId = Guid.NewGuid().ToString(),
        Username = $"user-{Guid.NewGuid():N}",
        Version = T0,
        ServerHash = "server-hash",
    };

    public void Add(ShuffullContext context, string songId, ListenEndReason endReason, double seconds, double? duration = 200)
    {
        var startedAt = T0.AddMinutes(_count);
        context.Listens.Add(new Listen
        {
            ListenId = $"{userId}-{_count++:D5}",
            UserId = userId,
            SongId = songId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(seconds),
            SecondsListened = seconds,
            SongDurationSeconds = duration,
            EndReason = endReason,
            Context = ListenContext.Playlist,
            RecordedAt = startedAt,
        });
        LastStartedAt = startedAt;
    }

    public void AddQuickSkip(ShuffullContext context, string songId, double seconds = 5)
        => Add(context, songId, ListenEndReason.Skipped, seconds);

    public void AddFinished(ShuffullContext context, string songId)
        => Add(context, songId, ListenEndReason.Finished, 200);
}
