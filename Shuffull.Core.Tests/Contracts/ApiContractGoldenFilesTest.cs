using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shuffull.Api.Controllers;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.Songs.KeepSongs;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using RatingAckOutcomes = Shuffull.Api.Controllers.Outcomes;
using RetagOutcomes = Shuffull.Core.Features.Songs.RetagSongs.Outcomes;

namespace Shuffull.Core.Tests.Contracts;

/// <summary>
/// The wire shapes other repos read, written with the API's real JSON options into checked-in files under
/// <c>Contracts/Golden</c>. The app (vitest, through its zod schemas) and the funnel (xunit, through its real HTTP
/// clients) parse copies of the same files, so a field renamed or retyped here fails a test on the consuming side
/// instead of surfacing in prod as a silently-null column. Same idea as ShuffullApp's hashParity.test.ts.
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
        TagsStale: true);

    // Every optional field at its default: what a manual upload with no enrichment looks like.
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

    /// <summary>File name (without .json) → the value the endpoint returns (or, for a request, accepts).</summary>
    private static readonly Dictionary<string, object> Cases = new()
    {
        // POST /api/v1/songs/list. Carries SongDto; the app re-syncs whole rows from it.
        ["songs-list"] = new GetSongListResponse([FullSong(V1), SparseSong(V2)]),
        // GET /api/v1/songs/changed. The app's sync feed and its (Version, SongId) cursor.
        ["songs-changed"] = new GetSongsChangedResponse([FullSong(V1), SparseSong(V2)], EndOfList: false),
        // GET /api/v1/user-songs. LikeStatus is an enum and goes out as its int.
        ["user-songs"] = new GetUserSongsResponse(
            [
                new UserSongDto("user-1", "song-full", LastPlayed: V1, Version: V1, LikeStatus.Like),
                new UserSongDto("user-1", "song-sparse", LastPlayed: V2, Version: V2, LikeStatus.Neutral),
            ],
            EndOfList: true),
        // POST /api/v1/songs/keep. The app dead-letters anything whose outcome isn't "kept".
        ["songs-keep"] = new KeepSongsResponse(
        [
            new SongKeepResult("song-full", KeepOutcomes.Kept),
            new SongKeepResult("song-sparse", KeepOutcomes.Failed, "Song is not in your library."),
        ]),
        // POST /api/v1/songs/retag. Per-item outcomes inside a 200; the app must read them, not the status.
        ["songs-retag"] = new RetagSongsResponse(
        [
            new SongRetagResult("song-full", RetagOutcomes.Enriched),
            new SongRetagResult("song-locked", RetagOutcomes.Skipped),
            new SongRetagResult("song-sparse", RetagOutcomes.Failed, "AI is not enabled; cannot enrich songs."),
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
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

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

    [Fact]
    public void EveryGoldenFile_HasACase()
    {
        // A file nothing writes any more would keep the consumers testing a shape the API no longer sends.
        var files = Directory.GetFiles(GoldenDirectory(), "*.json").Select(Path.GetFileNameWithoutExtension);

        Assert.Empty(files.Except(Cases.Keys));
    }

    /// <summary>
    /// The options MVC serializes action results with. Program.cs calls a bare <c>AddControllers()</c>, so these are
    /// the framework defaults (camelCase, enums as ints, nulls written). If Program.cs ever configures JSON, apply
    /// the same configuration here.
    /// </summary>
    private static JsonSerializerOptions ApiJsonOptions()
    {
        var services = new ServiceCollection();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    }

    /// <summary>The source folder, not the build output: update mode writes back into the repo.</summary>
    private static string GoldenDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shuffull.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Shuffull.Core.Tests", "Contracts", "Golden");
    }
}
