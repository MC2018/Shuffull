using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;

public record RemoveSongFromPlaylistCommand(string UserId, string PlaylistId, string SongId) : IRequest<Result<RemoveSongFromPlaylistResponse>>;
