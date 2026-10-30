using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.UserSongs.SetSongLikeStatus;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.UserSongs.SetSongLikeStatus;

public class SetSongLikeStatusHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly SetSongLikeStatusHandler _handler;

    public SetSongLikeStatusHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new SetSongLikeStatusHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(DateTime version)
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = $"user-{Guid.NewGuid():N}",
            Version = version,
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<UserSong> SeedUserSongAsync(string userId, LikeStatus likeStatus, DateTime version, string? externalSongId = null, bool exploratory = false)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            ExternalSongId = externalSongId,
            Exploratory = exploratory,
            Version = version,
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();

        var userSong = new UserSong
        {
            UserId = userId,
            SongId = song.SongId,
            LastPlayed = DateTime.MinValue,
            Version = version,
            LikeStatus = likeStatus,
        };
        await _database.Context.UserSongs.AddAsync(userSong);
        await _database.Context.SaveChangesAsync();
        return userSong;
    }

    [Fact]
    public async Task Handle_WithNewStatus_UpdatesStatusAndBumpsVersions()
    {
        // Arrange
        var userVersion = DateTime.UtcNow.AddDays(-10);
        var user = await SeedUserAsync(userVersion);
        var songVersion = DateTime.UtcNow.AddDays(-5);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, songVersion);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Love),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(LikeStatus.Love, result.Get().LikeStatus);
        Assert.Equal(userSong.SongId, result.Get().SongId);

        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(LikeStatus.Love, savedUserSong.LikeStatus);
        Assert.True(savedUserSong.Version > songVersion);

        var savedUser = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.True(savedUser.Version > userVersion);
    }

    [Fact]
    public async Task Handle_WithUnchangedStatus_IsNoOpAndLeavesVersionsUntouched()
    {
        // Arrange — setting the status to its current value should not bump any version.
        var userVersion = DateTime.UtcNow.AddDays(-10);
        var user = await SeedUserAsync(userVersion);
        var songVersion = DateTime.UtcNow.AddDays(-5);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Like, songVersion);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Like),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(LikeStatus.Like, result.Get().LikeStatus);

        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(songVersion, savedUserSong.Version);

        var savedUser = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.Equal(userVersion, savedUser.Version);
    }

    [Fact]
    public async Task Handle_WhenNoMatchingUserSong_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync(DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, Guid.NewGuid().ToString(), LikeStatus.Like),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("No matching song", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(result.GetError()));
    }

    [Fact]
    public async Task Handle_WithSongBelongingToAnotherUser_ReturnsError()
    {
        // Arrange — the song record belongs to a different user, so the requester has no matching UserSong.
        var owner = await SeedUserAsync(DateTime.UtcNow);
        var otherUser = await SeedUserAsync(DateTime.UtcNow);
        var userSong = await SeedUserSongAsync(owner.UserId, LikeStatus.Neutral, DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(otherUser.UserId, userSong.SongId, LikeStatus.Dislike),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("No matching song", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(result.GetError()));
    }

    [Theory]
    [InlineData(LikeStatus.Like, YoutubeRating.Like)]
    [InlineData(LikeStatus.Love, YoutubeRating.Like)]
    [InlineData(LikeStatus.Dislike, YoutubeRating.Dislike)]
    [InlineData(LikeStatus.Neutral, YoutubeRating.None)]
    public void MapToYoutubeRating_IsFullParity(LikeStatus like, YoutubeRating expected)
        => Assert.Equal(expected, SetSongLikeStatusHandler.MapToYoutubeRating(like));

    [Fact]
    public async Task Handle_YoutubeSourcedSong_EnqueuesPendingRating()
    {
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-1));
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, DateTime.UtcNow.AddDays(-1), externalSongId: "yt-vid-1");

        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Love), CancellationToken.None);

        Assert.True(result.IsOk);
        var req = await _database.Context.YoutubeRatingRequests.AsNoTracking().SingleAsync();
        Assert.Equal("yt-vid-1", req.VideoId);
        Assert.Equal(userSong.SongId, req.SongId);
        Assert.Equal(YoutubeRating.Like, req.Rating); // Love -> like
        Assert.Equal(YoutubeRatingStatus.Pending, req.Status);
    }

    [Fact]
    public async Task Handle_ManualUpload_NoExternalId_DoesNotEnqueue()
    {
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-1));
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, DateTime.UtcNow.AddDays(-1)); // ExternalSongId null

        await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Like), CancellationToken.None);

        Assert.Equal(0, await _database.Context.YoutubeRatingRequests.CountAsync());
    }

    [Fact]
    public async Task Handle_SuccessiveChanges_CoalesceIntoOneRow_LatestWins()
    {
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-1));
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, DateTime.UtcNow.AddDays(-1), externalSongId: "yt-vid-2");

        await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Like), CancellationToken.None);
        await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Dislike), CancellationToken.None);

        var req = await _database.Context.YoutubeRatingRequests.AsNoTracking().SingleAsync(); // still exactly one row
        Assert.Equal(YoutubeRating.Dislike, req.Rating);
        Assert.Equal(YoutubeRatingStatus.Pending, req.Status);
    }

    [Theory]
    [InlineData(LikeStatus.Like)]
    [InlineData(LikeStatus.Love)]
    [InlineData(LikeStatus.Dislike)]
    [InlineData(LikeStatus.Neutral)]
    public async Task Handle_PersistsEachLikeStatusValue(LikeStatus target)
    {
        // Arrange — start from a status different from the target so the update path runs.
        var start = target == LikeStatus.Neutral ? LikeStatus.Like : LikeStatus.Neutral;
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-1));
        var userSong = await SeedUserSongAsync(user.UserId, start, DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, target),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(target, savedUserSong.LikeStatus);
    }

    // Shuffull#38: a Like/Love is the user's decision to keep the song, so it must take the song out of the audition
    // pool in the like's own commit. It used to depend on the client's follow-up /retag call; when that failed,
    // was rejected or abandoned, the liked song stayed Exploratory and an audition-mix delete could purge it.
    // The handler has no AI dependency at all, so this holds with the site's AI disabled.
    [Theory]
    [InlineData(LikeStatus.Like)]
    [InlineData(LikeStatus.Love)]
    public async Task Handle_LikingAnExploratorySong_PromotesItInTheSameCommit(LikeStatus liked)
    {
        var old = DateTime.UtcNow.AddDays(-3);
        var user = await SeedUserAsync(old);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, old, externalSongId: "yt-audition", exploratory: true);

        var result = await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, liked), CancellationToken.None);

        Assert.True(result.IsOk);
        var song = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == userSong.SongId);
        Assert.False(song.Exploratory);
        Assert.True(song.Version > old); // clients re-pull the song and drop their local audition state
        Assert.Null(song.TagModel);       // left untagged, so the RetagStaleSongs query still finds it
        var savedUserSong = await _database.Context.UserSongs.AsNoTracking().SingleAsync(us => us.SongId == userSong.SongId);
        Assert.Equal(liked, savedUserSong.LikeStatus);
        Assert.Equal(YoutubeRatingStatus.Pending, (await _database.Context.YoutubeRatingRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(LikeStatus.Dislike)]
    [InlineData(LikeStatus.Neutral)]
    public async Task Handle_NonKeepingStatus_LeavesTheSongInAudition(LikeStatus target)
    {
        var old = DateTime.UtcNow.AddDays(-3);
        var user = await SeedUserAsync(old);
        var start = target == LikeStatus.Neutral ? LikeStatus.Dislike : LikeStatus.Neutral;
        var userSong = await SeedUserSongAsync(user.UserId, start, old, exploratory: true);

        var result = await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, target), CancellationToken.None);

        Assert.True(result.IsOk);
        var song = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == userSong.SongId);
        Assert.True(song.Exploratory);
        Assert.Equal(old, song.Version);
    }

    [Fact]
    public async Task Handle_ResendingALikeForAStillExploratorySong_HealsIt_WithoutTouchingTheLike()
    {
        // Liked before this fix, so the like is already recorded but the song never left the audition pool.
        var old = DateTime.UtcNow.AddDays(-3);
        var user = await SeedUserAsync(old);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Like, old, externalSongId: "yt-healed", exploratory: true);

        var result = await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Like), CancellationToken.None);

        Assert.True(result.IsOk);
        var song = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == userSong.SongId);
        Assert.False(song.Exploratory);
        Assert.True(song.Version > old);
        // The sentiment didn't change, so neither its versions nor the YouTube mirror do.
        var savedUserSong = await _database.Context.UserSongs.AsNoTracking().SingleAsync(us => us.SongId == userSong.SongId);
        Assert.Equal(old, savedUserSong.Version);
        Assert.Equal(old, (await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId)).Version);
        Assert.Equal(0, await _database.Context.YoutubeRatingRequests.CountAsync());
    }

    [Fact]
    public async Task Handle_LikingAnAlreadyPromotedSong_DoesNotChurnTheSongVersion()
    {
        var old = DateTime.UtcNow.AddDays(-3);
        var user = await SeedUserAsync(old);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, old, exploratory: false);

        var result = await _handler.Handle(new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Love), CancellationToken.None);

        Assert.True(result.IsOk);
        var song = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == userSong.SongId);
        Assert.False(song.Exploratory);
        Assert.Equal(old, song.Version);
    }

    /// <summary>
    /// Stands in for a duplicate like request for the same video. Just before the handler's save opens its
    /// transaction (after its lookup found no rating row), it commits a rating row for <paramref name="videoId"/>.
    /// </summary>
    private sealed class CommitRatingRowBeforeSave(string songId, string videoId) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO \"YoutubeRatingRequests\" (\"YoutubeRatingRequestId\", \"SongId\", \"VideoId\", \"Rating\", \"Status\", \"CreatedAt\", \"UpdatedAt\") " +
                    $"VALUES ('competing-row', $song, $video, {(int)YoutubeRating.Dislike}, {(int)YoutubeRatingStatus.Completed}, '2026-01-01 00:00:00', '2026-01-01 00:00:00')";
                var song = insert.CreateParameter();
                song.ParameterName = "$song";
                song.Value = songId;
                insert.Parameters.Add(song);
                var video = insert.CreateParameter();
                video.ParameterName = "$video";
                video.Value = videoId;
                insert.Parameters.Add(video);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Handle_WhenADuplicateRequestCommitsTheRatingRowFirst_StillSavesTheLike()
    {
        // The app can send one like several times at once; prod had 4 rating rows for one video, 20ms apart.
        // VideoId is unique now, so the loser's insert is refused. That must not fail the like itself (Shuffull#43).
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-1));
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, DateTime.UtcNow.AddDays(-1), externalSongId: "video-1");
        var competing = new CommitRatingRowBeforeSave(userSong.SongId, "video-1");
        using var unitOfWork = new UnitOfWork(_database.CreateContext(competing), NullLogger<UnitOfWork>.Instance);

        var result = await new SetSongLikeStatusHandler(unitOfWork).Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Like), CancellationToken.None);

        Assert.True(competing.Fired);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        var check = _database.CreateContext();
        Assert.Equal(LikeStatus.Like, (await check.UserSongs.AsNoTracking().SingleAsync(us => us.SongId == userSong.SongId)).LikeStatus);
        var row = Assert.Single(await check.YoutubeRatingRequests.AsNoTracking().ToListAsync());
        Assert.Equal("competing-row", row.YoutubeRatingRequestId);
        Assert.Equal(YoutubeRating.Like, row.Rating);
        Assert.Equal(YoutubeRatingStatus.Pending, row.Status);
    }
}
