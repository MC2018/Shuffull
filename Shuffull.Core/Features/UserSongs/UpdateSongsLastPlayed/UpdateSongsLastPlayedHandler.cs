using MediatR;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Songs;
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
            return Result.Error<UpdateSongsLastPlayedResponse>(userSongsResult.GetError());
        }

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

        // A first play creates the play record, so a client needs no separate PUT /user-songs before it (Shuffull#2).
        // Only for songs that exist: an id with no song is most often one the server deleted while a client still holds
        // it locally. That is not a client error. Failing on it made the whole batch 400, which pinned the client's
        // outbox rows in place and, until the status was read correctly, wedged its entire sync. Such ids are skipped
        // and simply not counted, exactly as a stale (older) timestamp is.
        var missingSongIds = latestBySong.Keys.Except(userSongs.Select(us => us.SongId)).ToList();
        if (missingSongIds.Count > 0)
        {
            var songsResult = await unitOfWork.Repository<Song>()
                .ListAsync(new SongsByIdsSpec(missingSongIds).AsNoTracking(), cancellationToken);
            if (songsResult.IsError)
            {
                return Result.Error<UpdateSongsLastPlayedResponse>(songsResult.GetError());
            }

            foreach (var song in songsResult.Get())
            {
                var addResult = await userSongRepository.AddAsync(new UserSong
                {
                    UserId = request.UserId,
                    SongId = song.SongId,
                    LastPlayed = latestBySong[song.SongId],
                    Version = now,
                }, cancellationToken);
                if (addResult.IsError)
                {
                    return Result.Error<UpdateSongsLastPlayedResponse>(addResult.GetError());
                }

                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            var userResult = await unitOfWork.Repository<User>().GetByIdAsync(request.UserId, cancellationToken);
            if (userResult.IsError)
            {
                return Result.Error<UpdateSongsLastPlayedResponse>(userResult.GetError().NotFoundAs("User not found."));
            }

            userResult.Get().Version = now;

            var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsError)
            {
                return Result.Error<UpdateSongsLastPlayedResponse>(saveResult.GetError());
            }
        }

        return Result.Ok(new UpdateSongsLastPlayedResponse(updatedCount));
    }
}
