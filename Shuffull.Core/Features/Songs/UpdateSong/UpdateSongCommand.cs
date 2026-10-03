using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.Songs.UpdateSong;

/// <summary>
/// Curator-only edit of a shared song's metadata, to fix mislabels (e.g. a half-rate BPM, a wrong energy, a
/// collapsed multi-artist credit). Replaces the song's scalar fields and its full artist + tag sets, then bumps
/// <c>Song.Version</c> so every client re-pulls the corrected song on its next sync. Gated by
/// <see cref="RequiresRoleAttribute"/>; the <see cref="Behaviors.RoleAuthorizationBehavior{TRequest,TResponse}"/>
/// rejects non-curators before this reaches its handler.
/// </summary>
[RequiresRole(Role.Curator)]
public record UpdateSongCommand(
    string SongId,
    string Name,
    int? Bpm,
    int? Energy,
    IReadOnlyList<string> Artists,
    IReadOnlyList<SongTagEdit> Tags)
    : IRequest<Result<UpdateSongResponse>>;
