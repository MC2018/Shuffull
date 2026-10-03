using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.DeletePlaylist;

/// <summary>
/// Deletes one of the user's playlists. For an exploratory ("audition") playlist this also purges the songs the
/// user never kept (still <c>Exploratory</c> and on no other playlist). Scoped to the owning user.
/// </summary>
public record DeletePlaylistCommand(string UserId, string PlaylistId) : IRequest<Result<DeletePlaylistResponse>>;
