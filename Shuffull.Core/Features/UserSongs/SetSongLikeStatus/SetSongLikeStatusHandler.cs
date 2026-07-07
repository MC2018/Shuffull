using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.UserSongs;
using Shuffull.Core.Persistence.Specifications.YoutubeRatings;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.UserSongs.SetSongLikeStatus;

public class SetSongLikeStatusHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<SetSongLikeStatusCommand, Result<SetSongLikeStatusResponse>>
{
    public async Task<Result<SetSongLikeStatusResponse>> Handle(SetSongLikeStatusCommand request, CancellationToken cancellationToken)
    {
        // Tracked load (no AsNoTracking) so the edit below is persisted by the unit of work's change tracker.
        var userSongRepository = unitOfWork.Repository<UserSong>();
        var userSongsResult = await userSongRepository
            .ListAsync(new UserSongsByUserAndSongIdsSpec(request.UserId, [request.SongId]), cancellationToken);
        if (userSongsResult.IsError)
        {
            return Result.Error<SetSongLikeStatusResponse>(userSongsResult.GetError().Message);
        }

        var userSong = userSongsResult.Get().FirstOrDefault();
        if (userSong is null)
        {
            return Result.Error<SetSongLikeStatusResponse>("No matching song was found for this user.");
        }

        if (userSong.LikeStatus != request.LikeStatus)
        {
            var now = DateTime.UtcNow;
            userSong.LikeStatus = request.LikeStatus;
            userSong.Version = now;

            // Bump the user's version so clients pick up the change on their next sync.
            var userResult = await unitOfWork.Repository<User>().GetByIdAsync(request.UserId, cancellationToken);
            if (userResult.IsError)
            {
                return Result.Error<SetSongLikeStatusResponse>("User not found.");
            }
            userResult.Get().Version = now;

            // Like-parity: mirror the new sentiment onto the song's YouTube video (if it has one). Staged into
            // the same unit of work so it commits atomically with the like below.
            await EnqueueYoutubeRatingAsync(request.SongId, request.LikeStatus, now, cancellationToken);

            var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsError)
            {
                return Result.Error<SetSongLikeStatusResponse>(saveResult.GetError().Message);
            }
        }

        return Result.Ok(new SetSongLikeStatusResponse(request.SongId, userSong.LikeStatus));
    }

    /// <summary>Maps a Shuffull sentiment to a YouTube rating: Like/Love -&gt; like, Dislike -&gt; dislike, else none.</summary>
    public static YoutubeRating MapToYoutubeRating(LikeStatus likeStatus) => likeStatus switch
    {
        LikeStatus.Like or LikeStatus.Love => YoutubeRating.Like,
        LikeStatus.Dislike => YoutubeRating.Dislike,
        _ => YoutubeRating.None,
    };

    /// <summary>
    /// Upserts the like-parity queue row for the song's YouTube video. Only YouTube-sourced songs carry an
    /// <see cref="Song.ExternalSongId"/> (the video id) — manual uploads are skipped. Keeps a single row per
    /// video, re-queued Pending with the latest rating, so the producer applies only the newest value.
    /// </summary>
    private async Task EnqueueYoutubeRatingAsync(string songId, LikeStatus likeStatus, DateTime now, CancellationToken cancellationToken)
    {
        var songResult = await unitOfWork.Repository<Song>().GetByIdAsync(songId, cancellationToken);
        if (songResult.IsError || string.IsNullOrWhiteSpace(songResult.Get().ExternalSongId))
        {
            return;
        }

        var videoId = songResult.Get().ExternalSongId!;
        var rating = MapToYoutubeRating(likeStatus);
        var repo = unitOfWork.Repository<YoutubeRatingRequest>();

        var existingResult = await repo.GetAsync(new YoutubeRatingRequestByVideoIdSpec(videoId), cancellationToken);
        if (existingResult.IsOk)
        {
            var existing = existingResult.Get();
            existing.Rating = rating;
            existing.Status = YoutubeRatingStatus.Pending;
            existing.UpdatedAt = now;
        }
        else
        {
            await repo.AddAsync(new YoutubeRatingRequest
            {
                YoutubeRatingRequestId = IdGenerator.Generate(),
                SongId = songId,
                VideoId = videoId,
                Rating = rating,
                Status = YoutubeRatingStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            }, cancellationToken);
        }
    }
}
