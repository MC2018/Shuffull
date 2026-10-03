using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playlists.GetUserPlaylists;

public record GetUserPlaylistsResponse(IReadOnlyList<PlaylistDto> Playlists);
