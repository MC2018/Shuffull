using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.UserSongs;

namespace Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;

public class UpdateSongsLastPlayedHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateSongsLastPlayedCommand, Result<UpdateSongsLastPlayedResponse>>
{
    public async Task<Result<UpdateSongsLastPlayedResponse>> Handle(UpdateSongsLastPlayedCommand request, CancellationToken cancellationToken)
    {
        var songIds = request.Updates.Select(u => u.SongId).ToList();

        // Tracked load (no AsNoTracking) so the timestamp/version edits below are persisted by the
        // unit of work's change tracker.
        var userSongRepository = unitOfWork.Repository<UserSong>();
        var userSongsResult = await userSongRepository
            .ListAsync(new UserSongsByUserAndSongIdsSpec(request.UserId, songIds), cancellationToken);
        if (userSongsResult.IsError)
        {
            return Result.Error<UpdateSongsLastPlayedResponse>(userSongsResult.GetError().Message);
        }

        // No matching play records is NOT a client error: the ids are simply songs this user has none for --
        // most often because the song was deleted server-side while a client still holds it locally. Reporting
        // that as a failure made the whole batch 400, which pinned the client's outbox rows in place and, until
        // the status was read correctly, wedged its entire sync. Fall through and report 0 updated instead,
        // exactly as a batch of stale (older) timestamps already does.
        var userSongs = userSongsResult.Get();

        // Keep the latest requested timestamp per song in case a song appears more than once.
        var latestBySong = request.Updates
            .GroupBy(u => u.SongId)
            .ToDictionary(g => g.Key, g => g.Max(u => u.LastPlayed));

        var now = DateTime.UtcNow;
        var updatedCount = 0;

        foreach (var userSong in userSongs)
        {
            if (!latestBySong.TryGetValue(userSong.SongId, out var requestedLastPlayed))
            {
                continue;
            }

            // Last-played only advances; ignore stale (older) timestamps.
            if (userSong.LastPlayed < requestedLastPlayed)
            {
                userSong.LastPlayed = requestedLastPlayed;
                userSong.Version = now;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            var userResult = await unitOfWork.Repository<User>().GetByIdAsync(request.UserId, cancellationToken);
            if (userResult.IsError)
            {
                return Result.Error<UpdateSongsLastPlayedResponse>("User not found.");
            }

            userResult.Get().Version = now;

            var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsError)
            {
                return Result.Error<UpdateSongsLastPlayedResponse>(saveResult.GetError().Message);
            }
        }

        return Result.Ok(new UpdateSongsLastPlayedResponse(updatedCount));
    }
}
