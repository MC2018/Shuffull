using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Playlists;

namespace Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;

public class RemoveSongFromPlaylistHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveSongFromPlaylistCommand, Result<RemoveSongFromPlaylistResponse>>
{
    public async Task<Result<RemoveSongFromPlaylistResponse>> Handle(RemoveSongFromPlaylistCommand request, CancellationToken cancellationToken)
    {
        // Load the playlist tracked (no AsNoTracking) and scoped to the user, so we can bump its Version
        // below and so a user cannot modify someone else's playlist.
        var playlistRepository = unitOfWork.Repository<Playlist>();
        var playlistResult = await playlistRepository.GetAsync(new PlaylistByUserAndIdSpec(request.UserId, request.PlaylistId), cancellationToken);
        if (playlistResult.IsError)
        {
            return Result.Error<RemoveSongFromPlaylistResponse>("Playlist not found.");
        }

        // Load the join row tracked so it can be deleted. Absent -> idempotent no-op success.
        var playlistSongRepository = unitOfWork.Repository<PlaylistSong>();
        var playlistSongResult = await playlistSongRepository.GetAsync(new PlaylistSongSpec(request.PlaylistId, request.SongId), cancellationToken);
        if (playlistSongResult.IsError)
        {
            return Result.Ok(new RemoveSongFromPlaylistResponse(false));
        }

        var deleteResult = playlistSongRepository.Delete(playlistSongResult.Get());
        if (deleteResult.IsError)
        {
            return Result.Error<RemoveSongFromPlaylistResponse>(deleteResult.GetError().Message);
        }

        // Bump the playlist version so clients can detect the change (tracked entity, persisted below).
        playlistResult.Get().Version = DateTime.UtcNow;

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<RemoveSongFromPlaylistResponse>(saveResult.GetError().Message);
        }

        return Result.Ok(new RemoveSongFromPlaylistResponse(true));
    }
}
