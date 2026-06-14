using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playlists.GetPlaylists;

public record GetPlaylistsResponse(IReadOnlyList<PlaylistDto> Playlists);
