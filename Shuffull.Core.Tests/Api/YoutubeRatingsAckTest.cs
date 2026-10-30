using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Controllers;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The like-parity ack resolves a row only while it is still Pending with the exact rating the producer applied.
/// That check has to hold at the moment of the write: a re-rate that commits between the ack's read and its write
/// must leave the row Pending with the new rating, not Completed with a rating YouTube never got (Shuffull#43).
/// </summary>
public class YoutubeRatingsAckTest : IDisposable
{
    private readonly DatabaseFixture _database = new();

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

    private async Task<YoutubeRatingRequest> SeedPendingLikeAsync()
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            ExternalSongId = "video-1",
            Version = DateTime.UtcNow,
        };
        var row = new YoutubeRatingRequest
        {
            YoutubeRatingRequestId = Guid.NewGuid().ToString(),
            SongId = song.SongId,
            VideoId = song.ExternalSongId,
            Rating = YoutubeRating.Like,
            Status = YoutubeRatingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _database.Context.Songs.Add(song);
        _database.Context.YoutubeRatingRequests.Add(row);
        await _database.Context.SaveChangesAsync();
        return row;
    }

    private YoutubeRatingsController Controller(params IInterceptor[] interceptors) =>
        new(_database.CreateContext(interceptors), new RecordingHealthSignals());

    private async Task<YoutubeRatingRequest> ReloadAsync(string id) =>
        await _database.CreateContext().YoutubeRatingRequests.AsNoTracking().SingleAsync(r => r.YoutubeRatingRequestId == id);

    [Fact]
    public async Task Ack_MatchingRating_CompletesTheRow()
    {
        var row = await SeedPendingLikeAsync();

        var result = await Controller().Ack([new(row.YoutubeRatingRequestId, "like")], CancellationToken.None);

        Assert.Equal(new YoutubeRatingAckResponse(1, 0), Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(YoutubeRatingStatus.Completed, (await ReloadAsync(row.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_Unratable_SkipsTheRow()
    {
        var row = await SeedPendingLikeAsync();

        var result = await Controller().Ack([new(row.YoutubeRatingRequestId, "like", YoutubeRatingOutcomes.Unratable)], CancellationToken.None);

        Assert.Equal(new YoutubeRatingAckResponse(0, 1), Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(YoutubeRatingStatus.Skipped, (await ReloadAsync(row.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_StaleRating_LeavesTheRowPending()
    {
        var row = await SeedPendingLikeAsync();

        var result = await Controller().Ack([new(row.YoutubeRatingRequestId, "dislike")], CancellationToken.None);

        Assert.Equal(new YoutubeRatingAckResponse(0, 0), Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(YoutubeRatingStatus.Pending, (await ReloadAsync(row.YoutubeRatingRequestId)).Status);
    }

    [Fact]
    public async Task Ack_WhenTheUserReRatesMidAck_LeavesTheNewRatingPending()
    {
        var row = await SeedPendingLikeAsync();
        var reRate = new ReRateAfterFirstRatingStatement(row.YoutubeRatingRequestId);

        await Controller(reRate).Ack([new(row.YoutubeRatingRequestId, "like")], CancellationToken.None);

        Assert.True(reRate.Fired);
        var saved = await ReloadAsync(row.YoutubeRatingRequestId);
        Assert.Equal(YoutubeRating.Dislike, saved.Rating);
        Assert.Equal(YoutubeRatingStatus.Pending, saved.Status);
    }
}
