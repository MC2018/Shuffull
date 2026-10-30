using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shuffull.Api.Commands.Listens.RecordListens;
using Shuffull.Api.Commands.Songs.HoldTagSongs;
using Shuffull.Api.Commands.Songs.KeepSongs;
using Shuffull.Api.Commands.Songs.RetagSongs;
using Shuffull.Api.Commands.Songs.UpdateSong;
using Shuffull.Api.Commands.Songs.UpdateSongLyrics;
using Shuffull.Api.Commands.Users.Credentials;
using Shuffull.Api.Controllers;
using Shuffull.Api.Extensions;
using Shuffull.Core.Features.Listens.GetListenStats;
using Shuffull.Core.Features.Listens.GetRecap;
using Shuffull.Core.Features.Listens.RecordListens;
using Shuffull.Core.Features.Playlists.CreatePlaylist;
using Shuffull.Core.Features.Playlists.GetPlaylists;
using Shuffull.Core.Features.Playlists.GetUserPlaylists;
using Shuffull.Core.Features.Songs.GetLibrarySongs;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.Songs.KeepSongs;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Features.Songs.RetagStaleSongs;
using Shuffull.Core.Features.Songs.UpdateSong;
using Shuffull.Core.Features.Tags.GetAllTags;
using Shuffull.Core.Features.Users;
using Shuffull.Core.Features.Users.GetCurrentUser;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using Shuffull.Metadata.Models;
using Shuffull.Shared.Enums;
using JsonConvert = Newtonsoft.Json.JsonConvert;
using RatingAckOutcomes = Shuffull.Api.Controllers.YoutubeRatingOutcomes;
using RetagOutcomes = Shuffull.Core.Features.Songs.RetagSongs.Outcomes;

namespace Shuffull.Core.Tests.Contracts;

/// <summary>
/// The wire shapes the app and the funnel depend on (listed in <c>Contracts/Golden/README.md</c>), written with the
/// API's real JSON options into checked-in files under <c>Contracts/Golden</c>. For the request bodies the funnel and
/// the app send, the API must also bind the file, in camelCase and in PascalCase, since the funnel writes some keys each way.
/// The app (vitest, through its zod schemas) and the funnel (xunit, through its real HTTP clients) parse copies of the
/// same files, so a field renamed or retyped here fails a test on the consuming side instead of surfacing in prod as
/// a silently-null column. Same idea as ShuffullApp's hashParity.test.ts.
///
/// <para>When this test fails, the API's output changed. If that is intended, regenerate with
/// <c>SHUFFULL_UPDATE_GOLDEN=1 dotnet test --filter ApiContractGoldenFilesTest</c>, copy the files to the
/// consumers with the hub's <c>scripts/sync-contract-fixtures.sh</c>, and make sure their tests still pass:
/// a consumer that breaks is an installed app or a running funnel that breaks.</para>
/// </summary>
public class ApiContractGoldenFilesTest
{
    private const string UpdateVariable = "SHUFFULL_UPDATE_GOLDEN";

    // Indented for review only. The comparison is on parsed JSON, so layout never fails the test.
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // A Postgres timestamp carries microseconds, which is why the app reads `version` as a string for its cursor.
    private static readonly DateTime V1 = new DateTime(2026, 10, 1, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);
    private static readonly DateTime V2 = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc).AddTicks(10);

    // What the app sends: a JS Date goes out with milliseconds only, so a request time can't carry V1's microseconds.
    private static readonly DateTime SentAt = new(2026, 10, 1, 12, 34, 56, 123, DateTimeKind.Utc);

    private static SongDto FullSong(DateTime version) => new(
        SongId: "song-full",
        Name: "Everybody Wants You",
        FileExtension: ".mp3",
        FileHash: "0f343b0931126a20f133d67c2b018a3b",
        ExternalSongId: "dQw4w9WgXcQ",
        Artists: ["Billy Squier", "Beyoncé"],
        Tags: ["Rock", "1980s", "English"],
        Version: version,
        SyncedLyrics: "[00:01.00] First line",
        PlainLyrics: "First line",
        LyricsInstrumental: false,
        LyricsSource: "lrclib",
        Bpm: 128,
        Heaviness: 6,
        Exploratory: false,
        TagsStale: true,
        HookStartPercent: 37.5);

    // No enrichment and every nullable field null: an audition import as it first lands (Exploratory set, so the
    // flag's true value is pinned too).
    private static SongDto SparseSong(DateTime version) => new(
        SongId: "song-sparse",
        Name: "Untitled",
        FileExtension: ".m4a",
        FileHash: "9e107d9d372bb6826bd81d3542a419d6",
        ExternalSongId: null,
        Artists: [],
        Tags: [],
        Version: version,
        Exploratory: true);

    private static readonly PlaylistDto[] Playlists =
    [
        new("playlist-full", "user-1", "Road trip", CurrentSongId: "song-full", PercentUntilReplayable: 0.75m, Version: V1,
            SongIds: ["song-full", "song-sparse"]),
        // An audition mix as it lands: nothing played yet.
        new("playlist-audition", "user-1", "Audition 2026-10-01", CurrentSongId: null, PercentUntilReplayable: 0.5m, Version: V2,
            SongIds: [], IsExploratory: true),
    ];

    private static readonly UserDto Curator = new("user-1", "listener", V1, IsCurator: true);

    /// <summary>File name (without .json) → the value the endpoint returns (or, for a request, accepts).</summary>
    private static readonly Dictionary<string, object> Cases = new()
    {
        // POST /api/v1/songs/list. Carries SongDto; the app re-syncs whole rows from it.
        ["songs-list"] = new GetSongListResponse([FullSong(V1), SparseSong(V2)]),
        // POST /api/v1/songs/list — a REQUEST body: a bare array of the song ids to fetch.
        ["songs-list-request"] = new[] { "song-full", "song-sparse" },
        // PUT /api/v1/songs/{id} — a REQUEST body: the curator's metadata edit (ApiClient.songUpdate).
        ["songs-update"] = new UpdateSongRequest(
            Name: "Everybody Wants You",
            Bpm: 128,
            Heaviness: 6,
            Artists: ["Billy Squier"],
            Tags: [new SongTagEdit("Rock", TagType.Genre), new SongTagEdit("1980s", TagType.TimePeriod)]),
        // GET /api/v1/songs/changed. The app's sync feed and its (Version, SongId) cursor.
        ["songs-changed"] = new GetSongsChangedResponse([FullSong(V1), SparseSong(V2)], EndOfList: false),
        // GET /api/v1/user-songs. LikeStatus is an enum and goes out as its int.
        ["user-songs"] = new GetUserSongsResponse(
            [
                new UserSongDto("user-1", "song-full", LastPlayed: V1, Version: V1, LikeStatus.Like),
                new UserSongDto("user-1", "song-sparse", LastPlayed: V2, Version: V2, LikeStatus.Neutral),
            ],
            EndOfList: true),
        // PUT /api/v1/user-songs — a REQUEST body: the song ids to add to the caller's library.
        ["user-songs-create"] = new[] { "song-full", "song-sparse" },
        // POST /api/v1/user-songs/last-played — a REQUEST body. The app sends whole outbox rows; the API reads these two.
        ["user-songs-last-played"] = new List<SongLastPlayed>
        {
            new("song-full", SentAt),
            new("song-sparse", SentAt.AddMinutes(4)),
        },
        // POST /api/v1/user-songs/{id}/like — a REQUEST body: the LikeStatus as a bare int.
        ["user-songs-like"] = LikeStatus.Like,
        // GET /api/v1/playlists. The app builds its playlist_song rows from songIds, so a rename empties every playlist.
        ["playlists"] = new GetUserPlaylistsResponse(Playlists),
        // POST /api/v1/playlists/list. A different response type with, today, the same shape.
        ["playlists-list"] = new GetPlaylistsResponse(Playlists),
        // POST /api/v1/playlists/list — a REQUEST body: a bare array of the playlist ids to fetch.
        ["playlists-list-request"] = new[] { "playlist-full", "playlist-audition" },
        // PUT /api/v1/playlists?name=. The new playlist, synced into the app straight from this response.
        ["playlists-create"] = new CreatePlaylistResponse(Playlists[1] with { Name = "New playlist", IsExploratory = false }),
        // GET /api/v1/users/me. isCurator gates the app's curator screens.
        ["users-me"] = new GetCurrentUserResponse(Curator),
        // POST /api/v1/users/authenticate, and POST /api/v1/users (sign-up) returns the same envelope.
        ["users-authenticate"] = new AuthSessionResponse(Curator, "token-abc", new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)),
        // POST /api/v1/users/authenticate and POST /api/v1/users — a REQUEST body: both take the same credentials.
        ["users-authenticate-request"] = new CredentialsRequest("listener", "hash-abc"),
        // POST /api/v1/songs/keep. The app dead-letters anything whose outcome isn't "kept".
        ["songs-keep"] = new KeepSongsResponse(
        [
            new SongKeepResult("song-full", KeepOutcomes.Kept),
            new SongKeepResult("song-sparse", KeepOutcomes.Failed, "Song is not in your library."),
        ]),
        // POST /api/v1/songs/keep — a REQUEST body: the audition songs the user kept.
        ["songs-keep-request"] = new KeepSongsRequest(["song-full", "song-sparse"]),
        // POST /api/v1/listens — a REQUEST body: the listen log the app's outbox sends (ShuffullApp#57).
        ["listens-request"] = new RecordListensRequest(
        [
            new ListenItem("listen-full", "song-full", V1, V1.AddSeconds(214), 201.5, 214.0, "finished", "playlist",
                PlaylistId: "playlist-1", Platform: "android", UserId: "user-1"),
            new ListenItem("listen-sparse", "song-sparse", V2, V2.AddSeconds(12), 12, null, "skipped", "genreJam"),
        ]),
        // POST /api/v1/listens. The app reads each outcome: "recorded" and "duplicate" are done, "rejected" is final.
        ["listens"] = new RecordListensResponse(
        [
            new ListenResult("listen-full", ListenOutcomes.Recorded),
            new ListenResult("listen-sparse", ListenOutcomes.Duplicate),
            new ListenResult("listen-bad", ListenOutcomes.Rejected, "endedAt is before startedAt."),
        ]),
        // GET /api/v1/listens/stats. Per-song stats and the "maybe drop this" flag (ShuffullApp#115).
        ["listens-stats"] = new GetListenStatsResponse(
            MaybeDropRule.Default,
            [
                new SongListenStats("song-full", Plays: 12, Skips: 4, AverageSecondsListened: 151.25, CompletionPercent: 70.5,
                    LastPlayed: V1, RecentQuickSkips: 3, MaybeDrop: true),
                new SongListenStats("song-sparse", Plays: 1, Skips: 0, AverageSecondsListened: 12, CompletionPercent: null,
                    LastPlayed: V2, RecentQuickSkips: 0, MaybeDrop: false),
            ]),
        // GET /api/v1/recap/{year}. The yearly recap (ShuffullApp#116): months and the heatmap are in the asked zone.
        ["recap"] = new GetRecapResponse(
            Year: 2026,
            TimeZone: "America/New_York",
            From: new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc),
            To: new DateTime(2027, 1, 1, 5, 0, 0, DateTimeKind.Utc),
            TotalMinutes: 125.5,
            Plays: 40,
            MinutesByMonth: [10, 20.5, 0, 0, 0, 0, 0, 0, 0, 0, 45, 50],
            Heatmap: Enumerable.Range(0, 7)
                .Select(day => (IReadOnlyList<double>)Enumerable.Range(0, 24).Select(hour => day == 5 && hour == 21 ? 125.5 : 0.0).ToList())
                .ToList(),
            TopSongsByMinutes: [new RecapSong("song-full", "Song Full", ["Artist A", "Artist B"], 80, 25)],
            TopSongsByPlays: [new RecapSong("song-sparse", null, [], 45.5, 30)],
            TopArtistsByMinutes: [new RecapArtist("artist-a", "Artist A", 80, 25)],
            TopArtistsByPlays: [new RecapArtist("artist-a", "Artist A", 80, 25)],
            TopGenres: [new RecapGenre("tag-rock", "Rock", 80)],
            GenresByMonth: Enumerable.Range(1, 12)
                .Select(month => new RecapGenreMonth(month, month == 1 ? 10 : 0,
                    [new RecapGenreShare("tag-rock", "Rock", month == 1 ? 2.5 : 0, month == 1 ? 25 : 0)]))
                .ToList(),
            AuditionMinutes: 12.5,
            KeepRate: new RecapKeepRate(AuditionedSongs: 4, KeptSongs: 1, Percent: 25),
            LongestStreak: new RecapStreak(3, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4)),
            LongestSession: new RecapSession(V1, V1.AddMinutes(95), 90, 22),
            BiggestGrower: new RecapGrower(new RecapSong("song-full", "Song Full", ["Artist A"], 50, 15), Month: 12,
                PreviousMonthMinutes: 5),
            SongOfEachSeason:
            [
                new RecapSeason("winter", new RecapSong("song-full", "Song Full", ["Artist A"], 80, 25)),
                new RecapSeason("spring", null),
                new RecapSeason("summer", null),
                new RecapSeason("autumn", new RecapSong("song-sparse", null, [], 45, 29)),
            ],
            MostSkipped: new RecapMostSkipped(new RecapSong("song-sparse", null, [], 45.5, 30), Skips: 7)),
        // POST /api/v1/songs/retag. Per-item outcomes inside a 200; the app must read them, not the status.
        ["songs-retag"] = new RetagSongsResponse(
        [
            new SongRetagResult("song-full", RetagOutcomes.Enriched),
            new SongRetagResult("song-locked", RetagOutcomes.Skipped),
            new SongRetagResult("song-sparse", RetagOutcomes.Failed, "AI is not enabled; cannot enrich songs."),
        ]),
        // POST /api/v1/songs/retag — a REQUEST body: each song with the tier to re-tag it at.
        ["songs-retag-request"] = new RetagSongsRequest(
        [
            new SongRetagItem("song-full", RetagModels.Strong),
            new SongRetagItem("song-sparse", RetagModels.Weak),
        ]),
        // POST /api/v1/songs/retag-stale (curator-only). The app casts this rather than parsing it.
        ["songs-retag-stale"] = new RetagStaleSongsResponse(Enriched: 3, Failed: 1, Remaining: 120, StrongModel: "gpt-5.6-terra"),
        // GET /api/v1/tags. The app stores the type as its TagType number.
        ["tags"] = new GetAllTagsResponse(
        [
            new TagDto("tag-rock", "Rock", TagType.Genre),
            new TagDto("tag-1980s", "1980s", TagType.TimePeriod),
            new TagDto("tag-english", "English", TagType.Language),
        ]),
        // GET /api/v1/songs/pending-tags. The funnel's tagging queue.
        ["songs-pending-tags"] = new PendingTagSongsResponse(
            [
                new PendingTagSong(
                    SongId: "song-full",
                    ExternalSongId: "dQw4w9WgXcQ",
                    Name: "Everybody Wants You",
                    Artists: ["Billy Squier"],
                    Tier: TagTiers.Strong,
                    CurrentTagModel: "gpt-5.4-nano",
                    PlainLyrics: "First line",
                    SyncedLyrics: "[00:01.00] First line",
                    LyricsInstrumental: false,
                    MeasuredBpm: 128,
                    CrestFactorDb: 9.5,
                    LoudnessRangeLu: 6.25,
                    OnsetsPerSecond: 3.75,
                    OriginalReleaseYear: 1982,
                    FileHash: "0f343b0931126a20f133d67c2b018a3b",
                    FileExtension: ".mp3"),
                new PendingTagSong(
                    SongId: "song-sparse",
                    ExternalSongId: null,
                    Name: "Untitled",
                    Artists: [],
                    Tier: TagTiers.Weak,
                    CurrentTagModel: null,
                    PlainLyrics: null,
                    SyncedLyrics: null,
                    LyricsInstrumental: true,
                    MeasuredBpm: null,
                    CrestFactorDb: null,
                    LoudnessRangeLu: null,
                    OnsetsPerSecond: null,
                    OriginalReleaseYear: null,
                    FileHash: "9e107d9d372bb6826bd81d3542a419d6",
                    FileExtension: ".m4a"),
            ],
            Remaining: 42),
        // POST /api/v1/songs/{id}/tags — a REQUEST body: the funnel's tag write-back.
        ["songs-tags"] = new ApplySongTagsRequest(
            new GeneratedSongTags(
                MainGenres: ["Rock"],
                SubGenres: ["Arena Rock"],
                Languages: ["English"],
                TimePeriod: "1980s",
                Moods: ["Energetic"],
                Heaviness: 6,
                Themes: ["Rebellion"]),
            TagModel: "gpt-5.6-terra",
            TagRevision: 3),
        // POST /api/v1/songs/pending-tags/holds — a REQUEST body: the songs the funnel held for an AI batch.
        ["songs-pending-tags-holds"] = new HoldTagSongsRequest(["song-full", "song-sparse"]),
        // POST /api/v1/song-imports — a REQUEST: the multipart `details` part, every field set. The controller reads it
        // with Newtonsoft, not MVC's binder (see NewtonsoftBound).
        ["song-imports-details"] = new SongImportDetails(
            ExternalSongId: "dQw4w9WgXcQ",
            FileExtension: ".mp3",
            ExternalPlaylistId: "LL",
            TargetUserId: "user-1",
            TargetPlaylistId: "playlist-full",
            TargetPlaylistName: "Road trip",
            ExternalSource: ExternalSource.Youtube,
            GeneratedTags: new GeneratedSongTags(
                MainGenres: ["Rock"],
                SubGenres: ["Arena Rock"],
                Languages: ["English"],
                TimePeriod: "1980s",
                Moods: ["Energetic"],
                Heaviness: 6,
                Themes: ["Rebellion"]),
            MarkAsLiked: true,
            Bpm: 128,
            Lyrics: new SongLyrics("[00:01.00] First line", "First line", Instrumental: true, "lrclib"),
            ReplacesSongId: "song-old",
            Name: "Everybody Wants You",
            Artists: ["Billy Squier"],
            TagModel: "gpt-5.6-terra",
            MeasuredBpm: 126,
            LoudnessRangeLu: 6.25,
            CrestFactorDb: 9.5,
            OnsetsPerSecond: 3.75,
            OriginalReleaseYear: 1982,
            Exploratory: true,
            TagRevision: 3,
            HookStartPercent: 37.5),
        // PUT /api/v1/song-lyrics — a REQUEST body: lyrics the funnel found after the song was exported.
        ["song-lyrics"] = new UpdateSongLyricsRequest("dQw4w9WgXcQ", "[00:01.00] First line", "First line", Instrumental: true, "lrclib"),
        // GET /api/v1/songs/library. The funnel mirrors the site's songs into its duplicate registry from this.
        ["songs-library"] = new LibrarySongsPage(
            [
                new LibrarySong("song-full", "dQw4w9WgXcQ", "Everybody Wants You", ["Billy Squier"], "0f343b0931126a20f133d67c2b018a3b", ".mp3"),
                new LibrarySong("song-sparse", null, "Untitled", [], "9e107d9d372bb6826bd81d3542a419d6", ".m4a"),
            ],
            NextAfterSongId: "song-sparse"),
        // GET /api/v1/youtube-ratings. The funnel's like-parity queue.
        ["youtube-ratings"] = new List<YoutubeRatingDto>
        {
            new("rating-1", "song-full", "dQw4w9WgXcQ", "like"),
            new("rating-2", "song-other", "9bZkp7q19f0", "none"),
        },
        // POST /api/v1/youtube-ratings/ack — a REQUEST body: the shape the API binds, which the funnel must send.
        ["youtube-ratings-ack"] = new[]
        {
            new YoutubeRatingAckItem("rating-1", "like", RatingAckOutcomes.Applied),
            new YoutubeRatingAckItem("rating-2", "none", RatingAckOutcomes.Unratable),
        },
        // GET /api/v1/replacements. The funnel's re-source queue.
        ["replacements"] = new List<SongReplacementDto>
        {
            new("replacement-1", "song-full", "Everybody Wants You", ["Billy Squier"], "dQw4w9WgXcQ", "Clipped intro", V1),
            new("replacement-2", "song-sparse", "Untitled", [], null, null, V2),
        },
        // POST /api/v1/replacements — a REQUEST body: the app flags a song's audio. It never sends a note.
        ["replacements-request"] = new FlagReplacementRequest("song-full", Note: null),
    };

    /// <summary>
    /// The request bodies among <see cref="Cases"/>. The funnel writes its bodies with no naming policy, so their keys
    /// arrive PascalCase or mixed (songs-tags wraps PascalCase tags in a camelCase object, song-lyrics is all
    /// camelCase), and rely on the API binding case-insensitively. The app's arrive camelCase.
    /// </summary>
    private static readonly string[] RequestCases =
    [
        // The funnel's.
        "songs-tags", "youtube-ratings-ack", "songs-pending-tags-holds", "song-imports-details", "song-lyrics",
        // The app's.
        "listens-request", "users-authenticate-request", "songs-list-request", "playlists-list-request", "user-songs-create",
        "user-songs-last-played", "user-songs-like", "songs-update", "songs-keep-request", "songs-retag-request",
        "replacements-request",
    ];

    /// <summary>
    /// Requests the API reads with Newtonsoft rather than MVC's System.Text.Json binder: SongImportsController takes
    /// the import <c>details</c> as a form string and deserializes it itself, with Newtonsoft's defaults.
    /// </summary>
    private static readonly HashSet<string> NewtonsoftBound = ["song-imports-details"];

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    public static TheoryData<string, bool> RequestBodies
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var name in RequestCases)
            {
                data.Add(name, false);

                // A body with no keys (a bare id array, the like status) has no PascalCase spelling to test.
                var body = JsonSerializer.SerializeToNode(Cases[name], Cases[name].GetType(), ApiJsonOptions());
                if (!JsonNode.DeepEquals(body, PascalCaseNames(body)))
                {
                    data.Add(name, true);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ApiOutput_MatchesGoldenFile(string name)
    {
        var actual = JsonNode.Parse(JsonSerializer.Serialize(Cases[name], Cases[name].GetType(), ApiJsonOptions()))!;
        var path = Path.Combine(GoldenDirectory(), name + ".json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual.ToJsonString(Indented) + "\n");
            return;
        }

        Assert.True(File.Exists(path), $"Missing golden file {path}. Generate it with {UpdateVariable}=1.");
        var expected = JsonNode.Parse(File.ReadAllText(path));
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"The API's {name} output no longer matches {path}. Actual:\n{actual.ToJsonString(Indented)}\n"
            + $"If the change is intended, regenerate with {UpdateVariable}=1 and re-sync the consumers' copies.");
    }

    [Theory]
    [MemberData(nameof(RequestBodies))]
    public void ApiBinds_GoldenRequestBody(string name, bool pascalCase)
    {
        // A request is the API reading, not writing: what it binds is what matters. Bind the golden body (and the
        // PascalCase copy the funnel actually sends), write it back out, and it must come out as the golden file.
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDirectory(), name + ".json")))!;
        var sent = pascalCase ? PascalCaseNames(golden)! : golden;

        var type = Cases[name].GetType();
        var bound = NewtonsoftBound.Contains(name)
            ? JsonConvert.DeserializeObject(sent.ToJsonString(), type)
            : JsonSerializer.Deserialize(sent.ToJsonString(), type, ApiJsonOptions());
        var roundTripped = JsonNode.Parse(JsonSerializer.Serialize(bound, type, ApiJsonOptions()));

        Assert.True(JsonNode.DeepEquals(golden, roundTripped), $"The API bound {sent.ToJsonString()} as {roundTripped}");
    }

    [Fact]
    public void EveryGoldenFile_HasACase()
    {
        // A file nothing writes any more would keep the consumers testing a shape the API no longer sends.
        var files = Directory.GetFiles(GoldenDirectory(), "*.json").Select(Path.GetFileNameWithoutExtension);

        Assert.Empty(files.Except(Cases.Keys));
    }

    private static JsonNode? PascalCaseNames(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p =>
            KeyValuePair.Create(char.ToUpperInvariant(p.Key[0]) + p.Key[1..], PascalCaseNames(p.Value?.DeepClone())))),
        JsonArray a => new JsonArray(a.Select(i => PascalCaseNames(i?.DeepClone())).ToArray()),
        _ => node?.DeepClone(),
    };

    /// <summary>
    /// The options MVC serializes action results and binds request bodies with, from the same <see cref="MvcRegistration"/> Program.cs calls.
    /// Today those are the framework defaults (camelCase, enums as ints, nulls written).
    /// </summary>
    private static JsonSerializerOptions ApiJsonOptions()
    {
        var services = new ServiceCollection();
        services.AddShuffullControllers();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    }

    /// <summary>The source folder, not the build output: update mode writes back into the repo.</summary>
    private static string GoldenDirectory() => Path.Combine(RepoPaths.Root(), "Shuffull.Core.Tests", "Contracts", "Golden");
}
