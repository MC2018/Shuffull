using Nut.Results;
using Shuffull.Core.Features.Listens.GetRecap;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using static Shuffull.Core.Tests.Features.Listens.GetRecap.RecapFixture;

namespace Shuffull.Core.Tests.Features.Listens.GetRecap;

/// <summary>
/// The recap over fixtures of listens: every section's numbers, and the year, month, day and hour edges in the user's
/// time zone (local midnight, New Year, and both DST changes in New York).
/// </summary>
public class GetRecapHandlerTest : IDisposable
{
    private const string NewYork = "America/New_York";

    private readonly DatabaseFixture _database = new();
    private readonly User _me;
    private readonly User _other;
    private readonly RecapFixture _fx;

    public GetRecapHandlerTest()
    {
        _me = NewUser();
        _other = NewUser();
        _database.Context.Users.AddRange(_me, _other);
        _database.Context.SaveChanges();
        _fx = new RecapFixture(_database.Context, _me.UserId);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<GetRecapResponse> RecapAsync(int year = 2026, string? timeZone = null, string? userId = null)
    {
        await _database.Context.SaveChangesAsync();
        await using var context = _database.CreateContext();
        var result = await new GetRecapHandler(context)
            .Handle(new GetRecapQuery(userId ?? _me.UserId, year, timeZone), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    private static double[] Months(params (int Month, double Minutes)[] values)
    {
        var months = new double[12];
        foreach (var (month, minutes) in values)
        {
            months[month - 1] = minutes;
        }
        return months;
    }

    [Fact]
    public async Task EmptyYear_HasZeroesAndNoHighlights()
    {
        var recap = await RecapAsync();

        Assert.Equal((2026, "UTC", 0.0, 0), (recap.Year, recap.TimeZone, recap.TotalMinutes, recap.Plays));
        Assert.Equal(new double[12], recap.MinutesByMonth);
        Assert.Equal(7, recap.Heatmap.Count);
        Assert.All(recap.Heatmap, day => Assert.Equal(new double[24], day));
        Assert.Empty(recap.TopSongsByMinutes);
        Assert.Empty(recap.TopArtistsByPlays);
        Assert.Empty(recap.TopGenres);
        Assert.Equal(12, recap.GenresByMonth.Count);
        Assert.Equal(new RecapKeepRate(0, 0, null), recap.KeepRate);
        Assert.Null(recap.LongestStreak);
        Assert.Null(recap.LongestSession);
        Assert.Null(recap.BiggestGrower);
        Assert.Equal(["winter", "spring", "summer", "autumn"], recap.SongOfEachSeason.Select(s => s.Season));
        Assert.All(recap.SongOfEachSeason, s => Assert.Null(s.Song));
        Assert.Null(recap.MostSkipped);
    }

    [Fact]
    public async Task Utc_TotalsMonthsAndHeatmap_AndTheYearsEdges()
    {
        _fx.Listen("a", Utc(2025, 12, 31, 23, 59), minutes: 7); // last minute of 2025: not this year
        _fx.Listen("a", Utc(2026, 1, 1, 0, 0), minutes: 2);     // Thursday, midnight
        _fx.Listen("a", Utc(2026, 3, 15, 13, 30), minutes: 1);  // Sunday, 13:00
        _fx.Listen("a", Utc(2026, 12, 31, 23, 59), minutes: 3); // Thursday, 23:00, runs into 2027 but started in 2026
        _fx.Listen("a", Utc(2027, 1, 1, 0, 0), minutes: 5);     // next year

        var recap = await RecapAsync();

        Assert.Equal((Utc(2026, 1, 1, 0), Utc(2027, 1, 1, 0)), (recap.From, recap.To));
        Assert.Equal((6.0, 3), (recap.TotalMinutes, recap.Plays));
        Assert.Equal(Months((1, 2), (3, 1), (12, 3)), recap.MinutesByMonth);
        Assert.Equal(2, recap.Heatmap[(int)DayOfWeek.Thursday][0]);
        Assert.Equal(1, recap.Heatmap[(int)DayOfWeek.Sunday][13]);
        Assert.Equal(3, recap.Heatmap[(int)DayOfWeek.Thursday][23]);
        Assert.Equal(6.0, recap.Heatmap.Sum(day => day.Sum()));
    }

    [Fact]
    public async Task NewYork_YearMonthDayAndHourAreLocal_AroundMidnightAndNewYear()
    {
        // New York is UTC-5 in winter: local midnight on 1 January is 05:00 UTC.
        _fx.Listen("a", Utc(2026, 1, 1, 4, 59), minutes: 7);  // 23:59 on 31 Dec 2025 local: last year
        _fx.Listen("a", Utc(2026, 1, 1, 5, 0), minutes: 2);   // 00:00 on Thursday 1 Jan local
        _fx.Listen("a", Utc(2026, 2, 1, 4, 30), minutes: 1);  // 23:30 on Saturday 31 Jan local: January, not February
        _fx.Listen("a", Utc(2027, 1, 1, 4, 59), minutes: 3);  // 23:59 on Thursday 31 Dec local: this year's December
        _fx.Listen("a", Utc(2027, 1, 1, 5, 0), minutes: 5);   // 00:00 on 1 Jan 2027 local: next year

        var recap = await RecapAsync(timeZone: NewYork);

        Assert.Equal(NewYork, recap.TimeZone);
        Assert.Equal((Utc(2026, 1, 1, 5), Utc(2027, 1, 1, 5)), (recap.From, recap.To));
        Assert.Equal((6.0, 3), (recap.TotalMinutes, recap.Plays));
        Assert.Equal(Months((1, 3), (12, 3)), recap.MinutesByMonth);
        Assert.Equal(2, recap.Heatmap[(int)DayOfWeek.Thursday][0]);
        Assert.Equal(1, recap.Heatmap[(int)DayOfWeek.Saturday][23]);
        Assert.Equal(3, recap.Heatmap[(int)DayOfWeek.Thursday][23]);

        // The same instants seen from the year before and after.
        Assert.Equal(7.0, (await RecapAsync(year: 2025, timeZone: NewYork)).TotalMinutes);
        Assert.Equal(5.0, (await RecapAsync(year: 2027, timeZone: NewYork)).TotalMinutes);
        // And in UTC, the 04:59 listen is 2026 and the one in the last minute of local 2026 is 2027.
        Assert.Equal(2 + 1 + 7.0, (await RecapAsync()).TotalMinutes);
    }

    [Fact]
    public async Task NewYork_HoursFollowBothDstChanges()
    {
        // Sunday 8 March 2026: 02:00 EST jumps to 03:00 EDT, so local hour 2 doesn't exist that night.
        _fx.Listen("a", Utc(2026, 3, 8, 6, 30), minutes: 1); // 01:30 EST
        _fx.Listen("a", Utc(2026, 3, 8, 7, 30), minutes: 2); // 03:30 EDT, an hour of UTC later
        // Sunday 1 November 2026: 02:00 EDT falls back to 01:00 EST, so local 01:30 happens twice.
        _fx.Listen("a", Utc(2026, 11, 1, 5, 30), minutes: 4); // 01:30 EDT
        _fx.Listen("a", Utc(2026, 11, 1, 6, 30), minutes: 8); // 01:30 EST

        var heat = (await RecapAsync(timeZone: NewYork)).Heatmap[(int)DayOfWeek.Sunday];

        Assert.Equal(1 + 4 + 8, heat[1]);
        Assert.Equal(0, heat[2]);
        Assert.Equal(2, heat[3]);
    }

    [Fact]
    public async Task FixedOffset_MovesTheYearsEdges()
    {
        _fx.Listen("a", Utc(2025, 12, 31, 15, 0), minutes: 1); // 00:00 on 1 Jan 2026 at +09:00
        _fx.Listen("a", Utc(2026, 12, 31, 14, 59), minutes: 2); // 23:59 on 31 Dec 2026
        _fx.Listen("a", Utc(2026, 12, 31, 15, 0), minutes: 4); // 2027 already

        var recap = await RecapAsync(timeZone: "+09:00");

        Assert.Equal("+09:00", recap.TimeZone);
        Assert.Equal((Utc(2025, 12, 31, 15), Utc(2026, 12, 31, 15)), (recap.From, recap.To));
        Assert.Equal(Months((1, 1), (12, 2)), recap.MinutesByMonth);
        Assert.Equal(2, recap.Heatmap[(int)DayOfWeek.Thursday][23]);
    }

    [Fact]
    public async Task TopSongsAndArtists_ByMinutesAndByPlays_WithCurrentNamesAndArtists()
    {
        _fx.Song("a", "Alpha", artists: ["Ann", "Bob"]);
        _fx.Song("b", "Beta", artists: ["Bob"]);
        for (var i = 0; i < 3; i++)
        {
            _fx.Listen("a", Utc(2026, 4, 1 + i), minutes: 1);
        }
        _fx.Listen("b", Utc(2026, 5, 1), minutes: 10);
        _fx.Listen("gone", Utc(2026, 5, 2), minutes: 0.5); // a song the server no longer has

        var recap = await RecapAsync();

        Assert.Equal(
            new RecapSong[] { new("b", "Beta", ["Bob"], 10, 1), new("a", "Alpha", ["Ann", "Bob"], 3, 3), new("gone", null, [], 0.5, 1) },
            recap.TopSongsByMinutes, RecapSongComparer.Instance);
        Assert.Equal(["a", "b", "gone"], recap.TopSongsByPlays.Select(s => s.SongId));
        Assert.Equal(
            new RecapArtist[] { new("artist-Bob", "Bob", 13, 4), new("artist-Ann", "Ann", 3, 3) },
            recap.TopArtistsByMinutes);
        Assert.Equal(["artist-Bob", "artist-Ann"], recap.TopArtistsByPlays.Select(a => a.ArtistId));
    }

    [Fact]
    public async Task TopLists_StopAtTopCount()
    {
        for (var i = 0; i < GetRecapHandler.TopCount + 3; i++)
        {
            _fx.Song($"s{i:D2}", $"Song {i}", artists: [$"Artist {i}"], tags: [_fx.Genre($"g{i:D2}")]);
            _fx.Listen($"s{i:D2}", Utc(2026, 6, 1, 0, i), minutes: i + 1);
        }

        var recap = await RecapAsync();

        Assert.Equal(GetRecapHandler.TopCount, recap.TopSongsByMinutes.Count);
        Assert.Equal("s12", recap.TopSongsByMinutes[0].SongId);
        Assert.Equal(GetRecapHandler.TopCount, recap.TopArtistsByMinutes.Count);
        Assert.Equal(GetRecapHandler.TopCount, recap.TopGenres.Count);
        Assert.All(recap.GenresByMonth, m => Assert.Equal(GetRecapHandler.TopCount, m.Genres.Count));
    }

    [Fact]
    public async Task Genres_AreTheSongsCurrentGenreTags_AndShareMonthByMonth()
    {
        var rock = _fx.Genre("Rock");
        var jazz = _fx.Genre("Jazz");
        var calm = _fx.Mood("Calm"); // not a genre
        _fx.Song("a", "Alpha", tags: [rock, calm]);
        _fx.Song("b", "Beta", tags: [rock, jazz]);
        _fx.Song("c", "Gamma");
        _fx.Listen("a", Utc(2026, 1, 10), minutes: 3);
        _fx.Listen("b", Utc(2026, 2, 10), minutes: 10);
        _fx.Listen("c", Utc(2026, 2, 11), minutes: 5);

        var recap = await RecapAsync();

        Assert.Equal(new RecapGenre[] { new("genre-Rock", "Rock", 13), new("genre-Jazz", "Jazz", 10) }, recap.TopGenres);

        var january = recap.GenresByMonth[0];
        Assert.Equal((1, 3.0), (january.Month, january.TotalMinutes));
        Assert.Equal(new RecapGenreShare[] { new("genre-Rock", "Rock", 3, 100), new("genre-Jazz", "Jazz", 0, 0) }, january.Genres);

        var february = recap.GenresByMonth[1];
        Assert.Equal(15.0, february.TotalMinutes);
        Assert.Equal(10, february.Genres[0].Minutes);
        Assert.Equal(100 * 10 / 15.0, february.Genres[0].Percent, 9);
        Assert.Equal(100 * 10 / 15.0, february.Genres[1].Percent, 9);

        var march = recap.GenresByMonth[2];
        Assert.Equal(0.0, march.TotalMinutes);
        Assert.All(march.Genres, g => Assert.Equal(0.0, g.Percent));
    }

    [Fact]
    public async Task AuditionMinutes_AndKeepRate_CountSongsKeptOrLikedNow()
    {
        _fx.Song("promoted", "Kept", exploratory: false);
        _fx.Song("liked", "Liked", exploratory: true);
        _fx.Owns("liked", LikeStatus.Like);
        _fx.Song("loved-by-other", "Theirs", exploratory: true);
        _fx.Owns("loved-by-other", LikeStatus.Love, owner: _other.UserId);
        _fx.Song("still-auditioning", "Maybe", exploratory: true);
        _fx.Owns("still-auditioning", LikeStatus.Neutral);
        _fx.Song("disliked", "No", exploratory: true);
        _fx.Owns("disliked", LikeStatus.Dislike);
        _fx.Song("library", "Library");

        foreach (var song in new[] { "promoted", "liked", "loved-by-other", "still-auditioning", "disliked", "purged" })
        {
            _fx.Listen(song, Utc(2026, 7, 1), minutes: 2, listenContext: ListenContext.Audition);
        }
        _fx.Listen("promoted", Utc(2026, 7, 2), minutes: 1, listenContext: ListenContext.Audition); // same song again
        _fx.Listen("library", Utc(2026, 7, 3), minutes: 5);                                        // not an audition
        _fx.Listen("still-auditioning", Utc(2025, 7, 1), listenContext: ListenContext.Audition);    // last year

        var recap = await RecapAsync();

        Assert.Equal(6 * 2 + 1.0, recap.AuditionMinutes);
        Assert.Equal(6, recap.KeepRate.AuditionedSongs);
        Assert.Equal(2, recap.KeepRate.KeptSongs); // promoted, and liked by me
        Assert.Equal(100 * 2 / 6.0, recap.KeepRate.Percent!.Value, 9);
    }

    [Fact]
    public async Task LongestStreak_CountsConsecutiveLocalDays()
    {
        foreach (var day in new[] { 1, 2, 3, 5, 6, 7, 8, 10 })
        {
            _fx.Listen("a", Utc(2026, 3, day));
        }
        _fx.Listen("a", Utc(2026, 3, 8, 18)); // a second listen on one day is still one day

        Assert.Equal(new RecapStreak(4, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 8)), (await RecapAsync()).LongestStreak);
    }

    [Fact]
    public async Task LongestStreak_UsesLocalDays()
    {
        // New York is UTC-4 in June. Local days 9, 10 and 11 June; in UTC the first two are 10 and 11 June.
        _fx.Listen("a", Utc(2026, 6, 10, 0, 0));  // 20:00 on 9 June local
        _fx.Listen("a", Utc(2026, 6, 11, 3, 30)); // 23:30 on 10 June local
        _fx.Listen("a", Utc(2026, 6, 11, 5, 0));  // 01:00 on 11 June local

        Assert.Equal(new RecapStreak(3, new DateOnly(2026, 6, 9), new DateOnly(2026, 6, 11)),
            (await RecapAsync(timeZone: NewYork)).LongestStreak);
        Assert.Equal(new RecapStreak(2, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 11)), (await RecapAsync()).LongestStreak);
    }

    [Fact]
    public async Task LongestSession_JoinsListensWithGapsUnderTheThreshold()
    {
        var start = Utc(2026, 8, 1, 10);
        _fx.Listen("a", start, minutes: 3);                     // 10:00-10:03
        _fx.Listen("b", start.AddMinutes(32), minutes: 3);      // 29 min after the last ended: same session
        _fx.Listen("a", start.AddMinutes(65), minutes: 2);      // exactly 30 min after: a new session
        _fx.Listen("c", Utc(2026, 8, 2, 10), minutes: 30);      // one long listen, shorter than the first session

        var session = (await RecapAsync()).LongestSession;

        Assert.Equal(new RecapSession(start, start.AddMinutes(35), 6, 2), session);
    }

    [Fact]
    public async Task LongestSession_OverlappingListensFromTwoDevices_EndAtTheLatestEnd()
    {
        var start = Utc(2026, 8, 1, 10);
        _fx.Listen("a", start, minutes: 20);
        _fx.Listen("b", start.AddMinutes(5), minutes: 3); // ends before the first does

        Assert.Equal(new RecapSession(start, start.AddMinutes(20), 23, 2), (await RecapAsync()).LongestSession);
    }

    [Fact]
    public async Task BiggestGrower_IsTheLargestRiseFromOneMonthToTheNext()
    {
        _fx.Song("steady", "Steady");
        _fx.Song("riser", "Riser");
        _fx.Listen("steady", Utc(2026, 1, 5), minutes: 10);
        _fx.Listen("steady", Utc(2026, 2, 5), minutes: 15);      // +5
        _fx.Listen("faller", Utc(2026, 1, 6), minutes: 30);      // falls to 0: no rise
        _fx.Listen("riser", Utc(2026, 3, 5), minutes: 1);
        _fx.Listen("riser", Utc(2026, 4, 5), minutes: 4);
        _fx.Listen("riser", Utc(2026, 4, 6), minutes: 4);        // +7 into April

        var grower = (await RecapAsync()).BiggestGrower;

        Assert.NotNull(grower);
        Assert.Equal(("riser", "Riser", 8.0, 2), (grower.Song.SongId, grower.Song.Name, grower.Song.Minutes, grower.Song.Plays));
        Assert.Equal((4, 1.0), (grower.Month, grower.PreviousMonthMinutes));
    }

    [Fact]
    public async Task SongOfEachSeason_ByMinutes_WinterIsJanuaryFebruaryAndDecember()
    {
        _fx.Listen("jan", Utc(2026, 1, 5), minutes: 5);
        _fx.Listen("dec", Utc(2026, 12, 5), minutes: 3);
        _fx.Listen("dec", Utc(2026, 12, 6), minutes: 3);
        _fx.Listen("spring", Utc(2026, 4, 5), minutes: 1);
        _fx.Listen("autumn", Utc(2026, 9, 1), minutes: 2);
        _fx.Listen("jan", Utc(2026, 11, 30), minutes: 1);

        var seasons = (await RecapAsync()).SongOfEachSeason;

        Assert.Equal(["winter", "spring", "summer", "autumn"], seasons.Select(s => s.Season));
        Assert.Equal(("dec", 6.0, 2), (seasons[0].Song!.SongId, seasons[0].Song!.Minutes, seasons[0].Song!.Plays));
        Assert.Equal("spring", seasons[1].Song!.SongId);
        Assert.Null(seasons[2].Song);
        Assert.Equal(("autumn", 2.0), (seasons[3].Song!.SongId, seasons[3].Song!.Minutes));
    }

    [Fact]
    public async Task MostSkipped_CountsSkipsForward_TieGoesToFewerPlays()
    {
        _fx.Listen("often", Utc(2026, 5, 1), endReason: ListenEndReason.Skipped);
        _fx.Listen("often", Utc(2026, 5, 2), endReason: ListenEndReason.Skipped);
        for (var i = 0; i < 3; i++)
        {
            _fx.Listen("popular", Utc(2026, 5, 3 + i));
        }
        _fx.Listen("popular", Utc(2026, 5, 7), endReason: ListenEndReason.Skipped);
        _fx.Listen("popular", Utc(2026, 5, 8), endReason: ListenEndReason.Skipped);
        for (var i = 0; i < 5; i++)
        {
            _fx.Listen("back", Utc(2026, 5, 10 + i), endReason: ListenEndReason.Previous); // not a skip
            _fx.Listen("stopped", Utc(2026, 5, 10 + i, 18), endReason: ListenEndReason.Stopped); // nor this
        }

        var skipped = (await RecapAsync()).MostSkipped;

        Assert.NotNull(skipped);
        Assert.Equal(("often", 2, 2), (skipped.Song.SongId, skipped.Skips, skipped.Song.Plays));
    }

    [Fact]
    public async Task OnlyTheCallersListensCount()
    {
        _fx.Listen("mine", Utc(2026, 5, 1), minutes: 2);
        new RecapFixture(_database.Context, _other.UserId).Listen("theirs", Utc(2026, 5, 1), minutes: 50,
            listenContext: ListenContext.Audition);

        var recap = await RecapAsync();

        Assert.Equal((2.0, 1, 0.0), (recap.TotalMinutes, recap.Plays, recap.AuditionMinutes));
        Assert.Equal(["mine"], recap.TopSongsByMinutes.Select(s => s.SongId));
        Assert.Equal(0, recap.KeepRate.AuditionedSongs);
    }

    [Fact]
    public async Task UnknownTimeZone_IsAnError()
    {
        await using var context = _database.CreateContext();
        var result = await new GetRecapHandler(context)
            .Handle(new GetRecapQuery(_me.UserId, 2026, "Mars/Olympus_Mons"), CancellationToken.None);

        Assert.True(result.IsError);
    }

    /// <summary>Records holding lists compare by reference, so compare RecapSong field by field.</summary>
    private sealed class RecapSongComparer : IEqualityComparer<RecapSong>
    {
        public static readonly RecapSongComparer Instance = new();

        public bool Equals(RecapSong? x, RecapSong? y) => x is not null && y is not null
            && (x.SongId, x.Name, x.Minutes, x.Plays) == (y.SongId, y.Name, y.Minutes, y.Plays)
            && x.Artists.SequenceEqual(y.Artists);

        public int GetHashCode(RecapSong obj) => obj.SongId.GetHashCode();
    }
}
