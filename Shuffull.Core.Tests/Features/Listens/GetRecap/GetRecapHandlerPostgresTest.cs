using Nut.Results;
using Shuffull.Core.Features.Listens.GetRecap;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using static Shuffull.Core.Tests.Features.Listens.GetRecap.RecapFixture;

namespace Shuffull.Core.Tests.Features.Listens.GetRecap;

/// <summary>
/// The recap on the production provider: the year's window compared against <c>timestamp with time zone</c>, the
/// song-id lists sent as arrays, and the genre filter on the tag discriminator. Skipped unless SHUFFULL_TEST_POSTGRES
/// is set.
/// </summary>
public class GetRecapHandlerPostgresTest : IAsyncLifetime
{
    private PostgresDatabase? _database;

    private PostgresDatabase Database => _database ?? throw new InvalidOperationException("Postgres is not configured.");

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.BaseConnectionString is { Length: > 0 } baseConnectionString)
        {
            _database = await PostgresDatabase.CreateAsync(baseConnectionString);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task Recap_InNewYork_OnPostgres()
    {
        var me = NewUser();
        var other = NewUser();
        await using (var seed = Database.CreateContext())
        {
            seed.Users.AddRange(me, other);
            var mine = new RecapFixture(seed, me.UserId);
            var rock = mine.Genre("Rock");
            mine.Song("a", "Alpha", exploratory: false, artists: ["Ann"], tags: [rock, mine.Mood("Calm")]);
            mine.Song("b", "Beta", exploratory: true, artists: ["Ann", "Bob"]);
            mine.Owns("b", LikeStatus.Neutral);
            mine.Listen("a", Utc(2026, 1, 1, 4, 59), minutes: 7); // 2025 in New York
            mine.Listen("a", Utc(2026, 1, 1, 5, 0), minutes: 2, listenContext: ListenContext.Audition);
            mine.Listen("b", Utc(2026, 1, 2, 5, 0), minutes: 3, endReason: ListenEndReason.Skipped,
                listenContext: ListenContext.Audition);
            mine.Listen("a", Utc(2027, 1, 1, 4, 59), minutes: 1); // 31 Dec 2026 in New York
            new RecapFixture(seed, other.UserId).Listen("a", Utc(2026, 6, 1), minutes: 100);
            await seed.SaveChangesAsync();
        }

        await using var context = Database.CreateContext();
        var result = await new GetRecapHandler(context)
            .Handle(new GetRecapQuery(me.UserId, 2026, "America/New_York"), CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        var recap = result.Get();
        Assert.Equal((6.0, 3, 5.0), (recap.TotalMinutes, recap.Plays, recap.AuditionMinutes));
        Assert.Equal((2 + 3.0, 1.0), (recap.MinutesByMonth[0], recap.MinutesByMonth[11]));
        Assert.Equal(["a", "b"], recap.TopSongsByMinutes.Select(s => s.SongId));
        Assert.Equal(["Ann", "Bob"], recap.TopSongsByMinutes[1].Artists);
        Assert.Equal(new RecapGenre("genre-Rock", "Rock", 3), Assert.Single(recap.TopGenres));
        Assert.Equal(new RecapKeepRate(2, 1, 50), recap.KeepRate);
        Assert.Equal(("b", 1), (recap.MostSkipped!.Song.SongId, recap.MostSkipped.Skips));
        Assert.Equal(new RecapStreak(2, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)), recap.LongestStreak);
        Assert.Equal(DateTimeKind.Utc, recap.LongestSession!.Start.Kind);
        // The earliest of the caller's listens, even one that falls in 2025 locally, read back as UTC.
        Assert.Equal(Utc(2026, 1, 1, 4, 59), recap.FirstListen);
        Assert.Equal(DateTimeKind.Utc, recap.FirstListen!.Value.Kind);
    }
}
