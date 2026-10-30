namespace Shuffull.Core.Features.Listens.GetRecap;

/// <summary>
/// The caller's recap of one year (epic shuffull-workspace#35), from their listens that STARTED in that year in
/// <see cref="TimeZone"/>. Every month, day and hour below is local to that zone. A listen counts in full where it
/// started, even one that runs past midnight.
///
/// <para>Minutes are play time (<c>Listen.SecondsListened</c>), so pauses don't count. A play is any logged listen,
/// skips included, as in <c>GET /api/v1/listens/stats</c>.</para>
///
/// <para>Names, artists and genres are the song's CURRENT ones, not what it had when played: a re-tag or a rename
/// changes past recaps. A song the server no longer has keeps its minutes, with a null name and no artists.</para>
/// </summary>
/// <param name="TimeZone">The zone the recap was computed in: the IANA id, or the offset as "+02:00", or "UTC".</param>
/// <param name="From">The year's first instant, UTC (local midnight on 1 January).</param>
/// <param name="To">The next year's first instant, UTC. The recap covers <paramref name="From"/> up to but not including this.</param>
/// <param name="Plays">How many listens the year had.</param>
/// <param name="MinutesByMonth">12 entries, January first.</param>
/// <param name="Heatmap">Minutes by local day of week and hour: 7 rows, Sunday first, of 24 hours each, midnight
/// first. A listen counts in the hour it started.</param>
/// <param name="TopSongsByMinutes">Up to <see cref="GetRecapHandler.TopCount"/> songs, most minutes first.</param>
/// <param name="TopSongsByPlays">Up to <see cref="GetRecapHandler.TopCount"/> songs, most plays first.</param>
/// <param name="TopArtistsByMinutes">Each artist on a song gets the song's full minutes and plays.</param>
/// <param name="TopGenres">Genre-type tags, most minutes first. Each genre on a song gets the song's full minutes, so a
/// song tagged with two genres counts towards both and the genres add up to more than <paramref name="TotalMinutes"/>.</param>
/// <param name="GenresByMonth">12 months, January first, each with the minutes and share of the year's
/// <paramref name="TopGenres"/>, so a chart can follow the same genres through the year.</param>
/// <param name="AuditionMinutes">Minutes of listens played from an audition (exploratory) playlist.</param>
/// <param name="LongestStreak">The most consecutive local days with at least one listen; null for an empty year.</param>
/// <param name="LongestSession">The longest run of listens with less than <see cref="GetRecapHandler.SessionGap"/>
/// between one ending and the next starting; null for an empty year.</param>
/// <param name="BiggestGrower">The song with the largest rise in minutes from one month to the next.</param>
/// <param name="SongOfEachSeason">4 seasons, winter first; see <see cref="RecapSeason"/>.</param>
/// <param name="MostSkipped">The song skipped forward ("skipped") most often; null if nothing was skipped.</param>
public record GetRecapResponse(
    int Year,
    string TimeZone,
    DateTime From,
    DateTime To,
    double TotalMinutes,
    int Plays,
    IReadOnlyList<double> MinutesByMonth,
    IReadOnlyList<IReadOnlyList<double>> Heatmap,
    IReadOnlyList<RecapSong> TopSongsByMinutes,
    IReadOnlyList<RecapSong> TopSongsByPlays,
    IReadOnlyList<RecapArtist> TopArtistsByMinutes,
    IReadOnlyList<RecapArtist> TopArtistsByPlays,
    IReadOnlyList<RecapGenre> TopGenres,
    IReadOnlyList<RecapGenreMonth> GenresByMonth,
    double AuditionMinutes,
    RecapKeepRate KeepRate,
    RecapStreak? LongestStreak,
    RecapSession? LongestSession,
    RecapGrower? BiggestGrower,
    IReadOnlyList<RecapSeason> SongOfEachSeason,
    RecapMostSkipped? MostSkipped);

/// <summary>A song with its minutes and plays over whatever span the field holding it covers.</summary>
/// <param name="Name">Null if the server no longer has the song.</param>
/// <param name="Artists">Artist names, alphabetical.</param>
public record RecapSong(string SongId, string? Name, IReadOnlyList<string> Artists, double Minutes, int Plays);

public record RecapArtist(string ArtistId, string Name, double Minutes, int Plays);

public record RecapGenre(string TagId, string Name, double Minutes);

/// <param name="Month">1 to 12.</param>
/// <param name="TotalMinutes">All listening that month, tagged or not.</param>
public record RecapGenreMonth(int Month, double TotalMinutes, IReadOnlyList<RecapGenreShare> Genres);

/// <param name="Percent">This genre's minutes as a share of the month's <see cref="RecapGenreMonth.TotalMinutes"/>,
/// 0 to 100. A song carrying several genres counts towards each, so the shares can add up to more than 100.</param>
public record RecapGenreShare(string TagId, string Name, double Minutes, double Percent);

/// <summary>
/// Keep rate. The time of a Keep isn't stored anywhere (<c>Song.Exploratory</c> is a flag, cleared in place), so this
/// can't be "kept during the year". It is: of the songs the user played from an audition playlist this year
/// (<paramref name="AuditionedSongs"/>), how many are kept NOW (<paramref name="KeptSongs"/>): the song still exists
/// and is no longer exploratory, or the user likes or loves it. A purged song counts as not kept.
///
/// <para><c>Exploratory</c> is one flag per song, not per user: a song another user kept counts as kept here too. So a
/// past year's rate moves when a song is kept, liked or purged later. Shuffull#101 tracks recording Keeps, and asks the
/// user to confirm this definition.</para>
/// </summary>
/// <param name="Percent">0 to 100; null when nothing was auditioned.</param>
public record RecapKeepRate(int AuditionedSongs, int KeptSongs, double? Percent);

/// <param name="Start">The streak's first local day.</param>
/// <param name="End">Its last local day, inclusive.</param>
public record RecapStreak(int Days, DateOnly Start, DateOnly End);

/// <param name="Start">When its first listen started, UTC.</param>
/// <param name="End">When its last listen ended, UTC.</param>
/// <param name="Minutes">Play time within the session.</param>
public record RecapSession(DateTime Start, DateTime End, double Minutes, int Plays);

/// <param name="Song">Its minutes and plays are those of <paramref name="Month"/>.</param>
/// <param name="Month">The month it rose into, 2 to 12.</param>
/// <param name="PreviousMonthMinutes">Its minutes in the month before.</param>
public record RecapGrower(RecapSong Song, int Month, double PreviousMonthMinutes);

/// <summary>
/// The most-played song (by minutes) of one season. Seasons go by month: "winter" is December to February, "spring"
/// March to May, "summer" June to August, "autumn" September to November. Within one calendar year, winter is
/// January, February and that year's December.
/// </summary>
/// <param name="Song">Its minutes and plays are within the season; null if the season had no listens.</param>
public record RecapSeason(string Season, RecapSong? Song);

/// <param name="Song">Its minutes and plays are the year's.</param>
public record RecapMostSkipped(RecapSong Song, int Skips);
