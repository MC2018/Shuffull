using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.AddSongToPlaylist;

public record AddSongToPlaylistCommand(string UserId, string PlaylistId, string SongId) : IRequest<Result<AddSongToPlaylistResponse>>;
