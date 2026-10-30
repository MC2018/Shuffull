using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Shared.Enums;

namespace Shuffull.Core.Features.Listens.GetRecap;

/// <summary>
/// Derives a year's recap from the caller's listens: no counters, no stored recap, nothing to keep in step. It reads
/// the year's listens through the <c>(UserId, StartedAt)</c> index, plus the current name, artists, genre tags and
/// like status of the songs in them, then does the rest in memory. Months, days and hours depend on the user's time
/// zone, DST included, which SQLite can't compute and Postgres would need per-listen conversions for.
///
/// <para>Cost: measured on Postgres 16 with a year of heavy listening (21,900 listens, one every 24 minutes, over
/// 2,500 songs, in a table of 362,000 rows): about 150 ms warm, nearly all of it the five reads (the listens index scan
/// itself is under 4 ms). A recap is asked for rarely, so it is computed on every request, with no cache. The
/// first-listen read came later and wasn't measured: it takes the top row off the same index.</para>
/// </summary>
public class GetRecapHandler(ShuffullContext context) : IRequestHandler<GetRecapQuery, Result<GetRecapResponse>>
{
    /// <summary>How many songs and artists each top list holds, and how many genres.</summary>
    public const int TopCount = 10;

    /// <summary>A pause this long or longer between one listen ending and the next starting ends a session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    private static readonly (string Name, int[] Months)[] Seasons =
    [
        ("winter", [12, 1, 2]),
        ("spring", [3, 4, 5]),
        ("summer", [6, 7, 8]),
        ("autumn", [9, 10, 11]),
    ];

    public async Task<Result<GetRecapResponse>> Handle(GetRecapQuery request, CancellationToken cancellationToken)
    {
        if (!RecapTimeZone.TryParse(request.TimeZone, out var zone))
        {
            return Result.Error<GetRecapResponse>("Unknown time zone.");
        }

        var from = RecapTimeZone.StartOfYearUtc(request.Year, zone);
        var to = RecapTimeZone.StartOfYearUtc(request.Year + 1, zone);

        var rows = await context.Listens.AsNoTracking()
            .Where(l => l.UserId == request.UserId && l.StartedAt >= from && l.StartedAt < to)
            .Select(l => new { l.SongId, l.StartedAt, l.EndedAt, l.SecondsListened, l.EndReason, l.Context })
            .ToListAsync(cancellationToken);

        var listens = rows
            .Select(r =>
            {
                // SQLite hands timestamps back unspecified; they were stored as UTC.
                var startedAt = DateTime.SpecifyKind(r.StartedAt, DateTimeKind.Utc);
                return new RecapListen(r.SongId, startedAt, DateTime.SpecifyKind(r.EndedAt, DateTimeKind.Utc),
                    TimeZoneInfo.ConvertTimeFromUtc(startedAt, zone), r.SecondsListened / 60, r.EndReason, r.Context);
            })
            .OrderBy(l => l.StartedAt)
            .ToList();

        // The caller's first listen in any year, read off the same index: it tells the app where stats start.
        var firstListen = await context.Listens.AsNoTracking()
            .Where(l => l.UserId == request.UserId)
            .OrderBy(l => l.StartedAt)
            .Select(l => (DateTime?)l.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var catalog = await SongCatalog.LoadAsync(context, request.UserId,
            listens.Select(l => l.SongId).Distinct().ToList(), cancellationToken);

        // One pass over the listens: everything below works from per-song, per-month totals.
        var totalsById = new Dictionary<string, SongTotals>();
        var minutesByMonth = new double[13];
        var heatmap = new double[7, 24];
        var auditioned = new HashSet<string>();
        var auditionMinutes = 0.0;
        foreach (var l in listens)
        {
            if (!totalsById.TryGetValue(l.SongId, out var totals))
            {
                totalsById[l.SongId] = totals = new SongTotals(l.SongId);
            }
            totals.Add(l);
            minutesByMonth[l.Local.Month] += l.Minutes;
            heatmap[(int)l.Local.DayOfWeek, l.Local.Hour] += l.Minutes;
            if (l.Context == ListenContext.Audition)
            {
                auditioned.Add(l.SongId);
                auditionMinutes += l.Minutes;
            }
        }
        var songs = totalsById.Values.OrderBy(s => s.SongId, StringComparer.Ordinal).ToList();

        var genres = SumGenres(songs, catalog);
        var topGenres = genres
            .OrderByDescending(g => g.Minutes).ThenBy(g => g.TagId, StringComparer.Ordinal)
            .Take(TopCount)
            .ToList();
        var artists = Artists(songs, catalog);
        var kept = auditioned.Count(catalog.IsKept);

        return Result.Ok(new GetRecapResponse(
            Year: request.Year,
            TimeZone: zone.Id,
            From: from,
            To: to,
            FirstListen: firstListen is { } first ? DateTime.SpecifyKind(first, DateTimeKind.Utc) : null,
            TotalMinutes: minutesByMonth.Sum(),
            Plays: listens.Count,
            MinutesByMonth: minutesByMonth[1..],
            Heatmap: Enumerable.Range(0, 7)
                .Select(day => (IReadOnlyList<double>)Enumerable.Range(0, 24).Select(hour => heatmap[day, hour]).ToList())
                .ToList(),
            TopSongsByMinutes: RankByMinutes(songs.Select(s => (s.SongId, s.Minutes, s.Plays)))
                .Take(TopCount).Select(s => catalog.Song(s.SongId, s.Minutes, s.Plays)).ToList(),
            TopSongsByPlays: songs
                .OrderByDescending(s => s.Plays).ThenByDescending(s => s.Minutes).ThenBy(s => s.SongId, StringComparer.Ordinal)
                .Take(TopCount).Select(s => catalog.Song(s.SongId, s.Minutes, s.Plays)).ToList(),
            TopArtistsByMinutes: artists
                .OrderByDescending(a => a.Minutes).ThenByDescending(a => a.Plays).ThenBy(a => a.ArtistId, StringComparer.Ordinal)
                .Take(TopCount).ToList(),
            TopArtistsByPlays: artists
                .OrderByDescending(a => a.Plays).ThenByDescending(a => a.Minutes).ThenBy(a => a.ArtistId, StringComparer.Ordinal)
                .Take(TopCount).ToList(),
            TopGenres: topGenres.Select(g => new RecapGenre(g.TagId, g.Name, g.Minutes)).ToList(),
            GenresByMonth: Enumerable.Range(1, 12)
                .Select(month => new RecapGenreMonth(month, minutesByMonth[month], topGenres
                    .Select(g => new RecapGenreShare(g.TagId, g.Name, g.ByMonth[month],
                        minutesByMonth[month] > 0 ? 100 * g.ByMonth[month] / minutesByMonth[month] : 0))
                    .ToList()))
                .ToList(),
            AuditionMinutes: auditionMinutes,
            KeepRate: new RecapKeepRate(auditioned.Count, kept, auditioned.Count == 0 ? null : 100.0 * kept / auditioned.Count),
            LongestStreak: LongestStreak(listens),
            LongestSession: LongestSession(listens),
            BiggestGrower: BiggestGrower(songs, catalog),
            SongOfEachSeason: Seasons
                .Select(season => new RecapSeason(season.Name, TopSongOf(songs, season.Months, catalog)))
                .ToList(),
            MostSkipped: songs
                .Where(s => s.Skips > 0)
                // On equal skips, fewer plays wins: more of its plays were skips.
                .OrderByDescending(s => s.Skips).ThenBy(s => s.Plays).ThenBy(s => s.SongId, StringComparer.Ordinal)
                .Select(s => new RecapMostSkipped(catalog.Song(s.SongId, s.Minutes, s.Plays), s.Skips))
                .FirstOrDefault()));
    }

    private static List<RecapArtist> Artists(List<SongTotals> songs, SongCatalog catalog) => songs
        .SelectMany(s => catalog.ArtistsOf(s.SongId).Select(a => (Artist: a, Song: s)))
        .GroupBy(x => x.Artist.Id)
        .Select(g => new RecapArtist(g.Key, g.First().Artist.Name, g.Sum(x => x.Song.Minutes), g.Sum(x => x.Song.Plays)))
        .ToList();

    /// <summary>Minutes per genre, by month, from each song's monthly totals: a song with two genres counts for both.</summary>
    private static List<GenreTotals> SumGenres(List<SongTotals> songs, SongCatalog catalog)
    {
        var genres = new Dictionary<string, GenreTotals>();
        foreach (var song in songs)
        {
            foreach (var genre in catalog.GenresOf(song.SongId))
            {
                if (!genres.TryGetValue(genre.Id, out var totals))
                {
                    genres[genre.Id] = totals = new GenreTotals(genre.Id, genre.Name);
                }
                for (var month = 1; month <= 12; month++)
                {
                    totals.ByMonth[month] += song.MonthMinutes[month];
                }
            }
        }
        return genres.Values.ToList();
    }

    private static RecapStreak? LongestStreak(List<RecapListen> listens)
    {
        var days = listens.Select(l => DateOnly.FromDateTime(l.Local)).Distinct().Order().ToList();
        if (days.Count == 0)
        {
            return null;
        }

        RecapStreak best = new(1, days[0], days[0]);
        var start = days[0];
        for (var i = 1; i < days.Count; i++)
        {
            if (days[i] != days[i - 1].AddDays(1))
            {
                start = days[i];
            }
            var length = days[i].DayNumber - start.DayNumber + 1;
            // Strictly longer, so a tie keeps the earlier streak.
            if (length > best.Days)
            {
                best = new RecapStreak(length, start, days[i]);
            }
        }
        return best;
    }

    private static RecapSession? LongestSession(List<RecapListen> listens)
    {
        RecapSession? best = null;
        RecapSession? current = null;
        foreach (var l in listens)
        {
            if (current is not null && l.StartedAt - current.End < SessionGap)
            {
                // Two devices can overlap, so a session ends at the latest end seen, not at the last listen's.
                current = current with
                {
                    End = l.EndedAt > current.End ? l.EndedAt : current.End,
                    Minutes = current.Minutes + l.Minutes,
                    Plays = current.Plays + 1,
                };
            }
            else
            {
                best = Longer(best, current);
                current = new RecapSession(l.StartedAt, l.EndedAt, l.Minutes, 1);
            }
        }
        return Longer(best, current);

        // Strictly longer, so a tie goes to the earlier session.
        static RecapSession? Longer(RecapSession? best, RecapSession? candidate)
        {
            if (candidate is null)
            {
                return best;
            }
            if (best is null)
            {
                return candidate;
            }
            return candidate.End - candidate.Start > best.End - best.Start ? candidate : best;
        }
    }

    private static RecapGrower? BiggestGrower(List<SongTotals> songs, SongCatalog catalog)
    {
        RecapGrower? best = null;
        var bestRise = 0.0;
        // Songs in id order and months in order, and only a strictly larger rise wins: a tie goes to the lower song id,
        // then the earlier month.
        foreach (var song in songs)
        {
            for (var month = 2; month <= 12; month++)
            {
                var rise = song.MonthMinutes[month] - song.MonthMinutes[month - 1];
                if (rise > bestRise)
                {
                    bestRise = rise;
                    best = new RecapGrower(catalog.Song(song.SongId, song.MonthMinutes[month], song.MonthPlays[month]),
                        month, song.MonthMinutes[month - 1]);
                }
            }
        }
        return best;
    }

    private static RecapSong? TopSongOf(List<SongTotals> songs, int[] months, SongCatalog catalog)
        => RankByMinutes(songs
                .Select(s => (s.SongId, Minutes: months.Sum(m => s.MonthMinutes[m]), Plays: months.Sum(m => s.MonthPlays[m])))
                .Where(s => s.Plays > 0))
            .Select(s => catalog.Song(s.SongId, s.Minutes, s.Plays))
            .FirstOrDefault();

    /// <summary>Most minutes first, then most plays, then song id: the order of the top list and of each season's song.</summary>
    private static IOrderedEnumerable<(string SongId, double Minutes, int Plays)> RankByMinutes(
        IEnumerable<(string SongId, double Minutes, int Plays)> songs) => songs
        .OrderByDescending(s => s.Minutes).ThenByDescending(s => s.Plays).ThenBy(s => s.SongId, StringComparer.Ordinal);

    private sealed record RecapListen(
        string SongId, DateTime StartedAt, DateTime EndedAt, DateTime Local, double Minutes, ListenEndReason EndReason,
        ListenContext Context);

    /// <summary>One song's year, with minutes and plays per local month (index 1 is January; 0 is unused).</summary>
    private sealed class SongTotals(string songId)
    {
        public string SongId { get; } = songId;
        public double Minutes { get; private set; }
        public int Plays { get; private set; }
        public int Skips { get; private set; }
        public double[] MonthMinutes { get; } = new double[13];
        public int[] MonthPlays { get; } = new int[13];

        public void Add(RecapListen listen)
        {
            Minutes += listen.Minutes;
            Plays++;
            MonthMinutes[listen.Local.Month] += listen.Minutes;
            MonthPlays[listen.Local.Month]++;
            if (listen.EndReason == ListenEndReason.Skipped)
            {
                Skips++;
            }
        }
    }

    /// <summary>One genre's minutes per local month (index 1 is January; 0 is unused).</summary>
    private sealed class GenreTotals(string tagId, string name)
    {
        public string TagId { get; } = tagId;
        public string Name { get; } = name;
        public double[] ByMonth { get; } = new double[13];
        public double Minutes => ByMonth.Sum();
    }

    private sealed record NamedId(string Id, string Name);

    /// <summary>What the recap shows about each song it names: its current name, artists, genres, and whether it's kept.</summary>
    private sealed class SongCatalog
    {
        private readonly Dictionary<string, (string Name, bool Exploratory)> _songs;
        private readonly ILookup<string, NamedId> _artists;
        private readonly ILookup<string, NamedId> _genres;
        private readonly HashSet<string> _liked;

        private SongCatalog(Dictionary<string, (string, bool)> songs, ILookup<string, NamedId> artists,
            ILookup<string, NamedId> genres, HashSet<string> liked)
        {
            _songs = songs;
            _artists = artists;
            _genres = genres;
            _liked = liked;
        }

        public static async Task<SongCatalog> LoadAsync(ShuffullContext context, string userId, List<string> songIds,
            CancellationToken cancellationToken)
        {
            var songs = await context.Songs.AsNoTracking()
                .Where(s => songIds.Contains(s.SongId))
                .Select(s => new { s.SongId, s.Name, s.Exploratory })
                .ToListAsync(cancellationToken);
            var artists = await context.SongArtists.AsNoTracking()
                .Where(sa => songIds.Contains(sa.SongId))
                .Select(sa => new { sa.SongId, sa.ArtistId, sa.Artist.Name })
                .ToListAsync(cancellationToken);
            var genres = await context.SongTags.AsNoTracking()
                .Where(st => songIds.Contains(st.SongId) && st.Tag.Type == TagType.Genre)
                .Select(st => new { st.SongId, st.TagId, st.Tag.Name })
                .ToListAsync(cancellationToken);
            var liked = await context.UserSongs.AsNoTracking()
                .Where(us => us.UserId == userId && songIds.Contains(us.SongId)
                    && (us.LikeStatus == LikeStatus.Like || us.LikeStatus == LikeStatus.Love))
                .Select(us => us.SongId)
                .ToListAsync(cancellationToken);

            return new SongCatalog(
                songs.ToDictionary(s => s.SongId, s => (s.Name, s.Exploratory)),
                artists.OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.ArtistId, StringComparer.Ordinal)
                    .ToLookup(a => a.SongId, a => new NamedId(a.ArtistId, a.Name)),
                genres.ToLookup(g => g.SongId, g => new NamedId(g.TagId, g.Name)),
                liked.ToHashSet());
        }

        public IEnumerable<NamedId> ArtistsOf(string songId) => _artists[songId];

        public IEnumerable<NamedId> GenresOf(string songId) => _genres[songId];

        /// <summary>See <see cref="RecapKeepRate"/>: the song still exists and is out of audition, or the user likes it.</summary>
        public bool IsKept(string songId)
            => (_songs.TryGetValue(songId, out var song) && !song.Exploratory) || _liked.Contains(songId);

        public RecapSong Song(string songId, double minutes, int plays) => new(
            songId,
            _songs.TryGetValue(songId, out var song) ? song.Name : null,
            _artists[songId].Select(a => a.Name).ToList(),
            minutes,
            plays);
    }
}
