using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Playlists;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Playlists.AddSongToPlaylist;

public class AddSongToPlaylistHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddSongToPlaylistCommand, Result<AddSongToPlaylistResponse>>
{
    public async Task<Result<AddSongToPlaylistResponse>> Handle(AddSongToPlaylistCommand request, CancellationToken cancellationToken)
    {
        // Load the playlist tracked (no AsNoTracking) and scoped to the user, so we can bump its
        // Version below and so a user cannot modify someone else's playlist.
        var playlistRepository = unitOfWork.Repository<Playlist>();
        var playlistResult = await playlistRepository.GetAsync(new PlaylistByUserAndIdSpec(request.UserId, request.PlaylistId), cancellationToken);
        if (playlistResult.IsError)
        {
            return Result.Error<AddSongToPlaylistResponse>("Playlist not found.");
        }

        var songResult = await unitOfWork.Repository<Song>().GetByIdAsync(request.SongId, cancellationToken);
        if (songResult.IsError)
        {
            return Result.Error<AddSongToPlaylistResponse>("Song not found.");
        }

        var playlistSongRepository = unitOfWork.Repository<PlaylistSong>();
        var existsResult = await playlistSongRepository.AnyAsync(
            new PlaylistSongSpec(request.PlaylistId, request.SongId).AsNoTracking(), cancellationToken);
        if (existsResult.IsError)
        {
            return Result.Error<AddSongToPlaylistResponse>(existsResult.GetError().Message);
        }

        // Already present: treat as an idempotent no-op success.
        if (existsResult.Get())
        {
            return Result.Ok(new AddSongToPlaylistResponse(false));
        }

        var addResult = await playlistSongRepository.AddAsync(new PlaylistSong
        {
            PlaylistSongId = IdGenerator.Generate(),
            PlaylistId = request.PlaylistId,
            SongId = request.SongId,
        }, cancellationToken);
        if (addResult.IsError)
        {
            return Result.Error<AddSongToPlaylistResponse>(addResult.GetError().Message);
        }

        // Bump the playlist version so clients can detect the change (tracked entity, persisted below).
        playlistResult.Get().Version = DateTime.UtcNow;

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<AddSongToPlaylistResponse>(saveResult.GetError().Message);
        }

        return Result.Ok(new AddSongToPlaylistResponse(true));
    }
}
