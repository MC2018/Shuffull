using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shuffull.Core.Alerts;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The like-parity queue the funnel polls and acks, over HTTP. The ack resolves a row only while it is still Pending
/// with the exact rating the producer applied. That check has to hold at the moment of the write: a re-rate that
/// commits between the ack's read and its write must leave the row Pending with the new rating, not Completed with a
/// rating YouTube never got (Shuffull#43).
/// </summary>
public class YoutubeRatingsAckTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly RecordingHealthSignals _signals = new();
    private int _seeded;

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Stands in for the user re-rating the song while the ack runs. Right after the ack's first statement against
    /// <c>YoutubeRatingRequests</c> finishes, it re-queues the row as Pending with a dislike, the way
    /// SetSongLikeStatusHandler does. For an ack that reads and then saves, that lands between the two.
    /// </summary>
    private sealed class ReRateAfterFirstRatingStatement(string ratingRequestId) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        private void ReRateOnce(DbCommand command)
        {
            if (Fired || !command.CommandText.Contains("\"YoutubeRatingRequests\""))
            {
                return;
            }

            Fired = true;
            using var reRate = command.Connection!.CreateCommand();
            reRate.Transaction = command.Transaction;
            reRate.CommandText =
                $"UPDATE \"YoutubeRatingRequests\" SET \"Rating\" = {(int)YoutubeRating.Dislike}, " +
                $"\"Status\" = {(int)YoutubeRatingStatus.Pending} WHERE \"YoutubeRatingRequestId\" = $id";
            var id = reRate.CreateParameter();
            id.ParameterName = "$id";
            id.Value = ratingRequestId;
            reRate.Parameters.Add(id);
            reRate.ExecuteNonQuery();
        }

        public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            ReRateOnce(command);
            return base.DataReaderDisposing(command, eventData, result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            ReRateOnce(command);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task<YoutubeRatingRequest> SeedAsync(
        YoutubeRating rating = YoutubeRating.Like,
        YoutubeRatingStatus status = YoutubeRatingStatus.Pending,
        DateTime? updatedAt = null)
    {
        var n = ++_seeded;
        var song = new Song
        {
            SongId = $"song-{n}",
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = $"hash-{n}",
            ExternalSongId = $"video-{n}",
            Version = DateTime.UtcNow,
        };
        var stamp = updatedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var row = new YoutubeRatingRequest
        {
            YoutubeRatingRequestId = $"rating-{n}",
            SongId = song.SongId,
            VideoId = song.ExternalSongId,
            Rating = rating,
            Status = status,
            CreatedAt = stamp,
            UpdatedAt = stamp,
        };
        _database.Context.Songs.Add(song);
        _database.Context.YoutubeRatingRequests.Add(row);
        await _database.Context.SaveChangesAsync();
        return row;
    }

    private Task<ApiTestHost> StartAsync(params IInterceptor[] interceptors) =>
        ProducerEndpointsHost.StartAsync(_database, _signals, null, interceptors);

    private static async Task<(HttpStatusCode Status, string Body)> AckAsync(ApiTestHost host, string json)
    {
        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Post, "api/v1/youtube-ratings/ack", json));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Ack(string id, string rating, string? outcome = null) => outcome == null
        ? $$"""{"ratingRequestId":"{{id}}","rating":"{{rating}}"}"""
        : $$"""{"ratingRequestId":"{{id}}","rating":"{{rating}}","outcome":"{{outcome}}"}""";

    private async Task<YoutubeRatingRequest> ReloadAsync(string id) =>
        await _database.CreateContext().YoutubeRatingRequests.AsNoTracking().SingleAsync(r => r.YoutubeRatingRequestId == id);

    // ── Queue ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_ReturnsRowsAtThatStatus_OldestUpdateFirst_InTheWireShape()
    {
        var newer = await SeedAsync(YoutubeRating.Like, updatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var older = await SeedAsync(YoutubeRating.Dislike, updatedAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(YoutubeRating.None, updatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(status: YoutubeRatingStatus.Completed);
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/youtube-ratings?status=0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """[{"ratingRequestId":"rating-3","songId":"song-3","videoId":"video-3","rating":"none"},""" +
            $$"""{"ratingRequestId":"{{older.YoutubeRatingRequestId}}","songId":"song-2","videoId":"video-2","rating":"dislike"},""" +
            $$"""{"ratingRequestId":"{{newer.YoutubeRatingRequestId}}","songId":"song-1","videoId":"video-1","rating":"like"}]""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Queue_StatusByName_IsAccepted_AnUndefinedStatusIsABindingError()
    {
        await SeedAsync(status: YoutubeRatingStatus.Skipped);
        await using var host = await StartAsync();

        var byName = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/youtube-ratings?status=skipped"));
        var undefined = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/youtube-ratings?status=99"));

        Assert.Equal(HttpStatusCode.OK, byName.StatusCode);
        Assert.Contains("\"rating-1\"", await byName.Content.ReadAsStringAsync());
        await ProducerEndpointsHost.AssertFrameworkValidation400Async(undefined, "status");
        Assert.Empty(_signals.Successes);
    }

    [Fact]
    public async Task Queue_NoStatus_MeansPending_AndRecordsThePoll()
    {
        await SeedAsync();
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/youtube-ratings"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"rating-1\"", await response.Content.ReadAsStringAsync());
        Assert.Equal([ShuffullSignalRules.YoutubeRatingsPoll], _signals.Successes);
    }

    // ── Ack ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ack_MatchingRating_CompletesTheRow_AndStampsIt()
    {
        var row = await SeedAsync();
        await using var host = await StartAsync();
        var before = DateTime.UtcNow;

        var (status, body) = await AckAsync(host, $"[{Ack(row.YoutubeRatingRequestId, "like")}]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"completed":1,"skipped":0}""", body);
        var saved = await ReloadAsync(row.YoutubeRatingRequestId);
        Assert.Equal(YoutubeRatingStatus.Completed, saved.Status);
        Assert.True(saved.UpdatedAt >= before.AddSeconds(-1), $"UpdatedAt {saved.UpdatedAt:o} not stamped");
    }

    [Fact]
    public async Task Ack_Unratable_SkipsTheRow()
    {
        var row = await SeedAsync();
        await using var host = await StartAsync();

        var (status, body) = await AckAsync(host, $"[{Ack(row.YoutubeRatingRequestId, "like", "unratable")}]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"completed":0,"skipped":1}""", body);
        Assert.Equal(YoutubeRatingStatus.Skipped, (await ReloadAsync(row.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_OutcomeAndRating_AreReadLoosely()
    {
        // "UNRATABLE" in any case skips; an unknown outcome completes (older producers); the rating is trimmed and
        // case-folded; "applied" completes.
        var shouty = await SeedAsync();
        var unknown = await SeedAsync();
        var padded = await SeedAsync(YoutubeRating.Dislike);
        var applied = await SeedAsync();
        await using var host = await StartAsync();

        var (_, body) = await AckAsync(host, "[" + string.Join(",",
            Ack(shouty.YoutubeRatingRequestId, "like", "UNRATABLE"),
            Ack(unknown.YoutubeRatingRequestId, "like", "something-new"),
            Ack(padded.YoutubeRatingRequestId, " DisLike "),
            Ack(applied.YoutubeRatingRequestId, "like", "applied")) + "]");

        Assert.Equal("""{"completed":3,"skipped":1}""", body);
        Assert.Equal(YoutubeRatingStatus.Skipped, (await ReloadAsync(shouty.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(unknown.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(padded.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(applied.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_AnUnknownRatingString_MatchesOnlyARowRatedNone()
    {
        var none = await SeedAsync(YoutubeRating.None);
        var like = await SeedAsync();
        await using var host = await StartAsync();

        var (_, body) = await AckAsync(host, "[" + string.Join(",",
            Ack(none.YoutubeRatingRequestId, "meh"),
            Ack(like.YoutubeRatingRequestId, "meh")) + "]");

        Assert.Equal("""{"completed":1,"skipped":0}""", body);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(none.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Pending, (await ReloadAsync(like.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_StaleRating_LeavesTheRowPending()
    {
        var row = await SeedAsync();
        await using var host = await StartAsync();

        var (status, body) = await AckAsync(host, $"[{Ack(row.YoutubeRatingRequestId, "dislike")}]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"completed":0,"skipped":0}""", body);
        Assert.Equal(YoutubeRatingStatus.Pending, (await ReloadAsync(row.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_ARowNoLongerPending_IsLeftAlone()
    {
        var completed = await SeedAsync(status: YoutubeRatingStatus.Completed);
        var skipped = await SeedAsync(status: YoutubeRatingStatus.Skipped);
        await using var host = await StartAsync();

        var (_, body) = await AckAsync(host, "[" + string.Join(",",
            Ack(completed.YoutubeRatingRequestId, "like", "unratable"),
            Ack(skipped.YoutubeRatingRequestId, "like")) + "]");

        Assert.Equal("""{"completed":0,"skipped":0}""", body);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(completed.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Skipped, (await ReloadAsync(skipped.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_AnUnknownId_CountsNothing()
    {
        await using var host = await StartAsync();

        var (status, body) = await AckAsync(host, $"[{Ack("no-such-row", "like")}]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"completed":0,"skipped":0}""", body);
    }

    [Fact]
    public async Task Ack_TheSameIdTwice_OnlyTheLastCounts()
    {
        var lastSkips = await SeedAsync();
        var lastCompletes = await SeedAsync();
        var lastIsStale = await SeedAsync();
        await using var host = await StartAsync();

        var (_, body) = await AckAsync(host, "[" + string.Join(",",
            Ack(lastSkips.YoutubeRatingRequestId, "like"),
            Ack(lastCompletes.YoutubeRatingRequestId, "like", "unratable"),
            Ack(lastIsStale.YoutubeRatingRequestId, "like"),
            Ack(lastSkips.YoutubeRatingRequestId, "like", "unratable"),
            Ack(lastCompletes.YoutubeRatingRequestId, "like"),
            Ack(lastIsStale.YoutubeRatingRequestId, "dislike")) + "]");

        Assert.Equal("""{"completed":1,"skipped":1}""", body);
        Assert.Equal(YoutubeRatingStatus.Skipped, (await ReloadAsync(lastSkips.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(lastCompletes.YoutubeRatingRequestId)).Status);
        Assert.Equal(YoutubeRatingStatus.Pending, (await ReloadAsync(lastIsStale.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_AnEmptyList_Is200WithNoBody()
    {
        await using var host = await StartAsync();

        var (status, body) = await AckAsync(host, "[]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("", body);
    }

    [Fact]
    public async Task Ack_ANullBody_IsABindingError()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Post, "api/v1/youtube-ratings/ack", "null"));

        await ProducerEndpointsHost.AssertFrameworkValidation400Async(response, "items");
    }

    [Fact]
    public async Task Ack_AnItemMissingItsId_IsRejectedBeforeAnyWrite()
    {
        var row = await SeedAsync();
        await using var host = await StartAsync();

        var (status, _) = await AckAsync(host, $$"""[{{Ack(row.YoutubeRatingRequestId, "like")}},{"rating":"like"}]""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(YoutubeRatingStatus.Pending, (await ReloadAsync(row.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_WhenTheUserReRatesMidAck_LeavesTheNewRatingPending()
    {
        var row = await SeedAsync();
        var reRate = new ReRateAfterFirstRatingStatement(row.YoutubeRatingRequestId);
        await using var host = await StartAsync(reRate);

        await AckAsync(host, $"[{Ack(row.YoutubeRatingRequestId, "like")}]");

        Assert.True(reRate.Fired);
        var saved = await ReloadAsync(row.YoutubeRatingRequestId);
        Assert.Equal(YoutubeRating.Dislike, saved.Rating);
        Assert.Equal(YoutubeRatingStatus.Pending, saved.Status);
    }
}
