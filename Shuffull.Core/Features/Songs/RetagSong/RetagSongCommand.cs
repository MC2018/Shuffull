using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Songs.RetagSong;

/// <summary>
/// Curator-only: re-tag one song from its already-stored inputs with the current strong model (no re-download).
/// Used to fix a single song, or to promote an exploratory song once it's been kept. A curator-locked song is
/// left untouched (reported back as skipped). Gated by <see cref="RequiresRoleAttribute"/>.
/// </summary>
[RequiresRole(Role.Curator)]
public record RetagSongCommand(string SongId) : IRequest<Result<RetagSongResponse>>;

/// <summary>The song and what happened to it.</summary>
public record RetagSongResponse(string SongId, SongEnrichmentStatus Status);
