using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.Songs.RetagStaleSongs;

/// <summary>
/// Curator-only: re-tag a bounded batch of "stale" songs - those whose <c>TagModel</c> is weaker than the
/// current strong model (per <see cref="Tools.ModelStrengths"/>), skipping curator-locked ones. Bounded +
/// resumable by design: it processes up to <see cref="Limit"/> songs and reports how many remain, so the
/// caller (the app's upgrade button) drives a whole-library upgrade in quota-friendly chunks rather than one
/// unbounded, minutes-long request. Gated by <see cref="RequiresRoleAttribute"/>.
/// </summary>
[RequiresRole(Role.Curator)]
public record RetagStaleSongsCommand(int Limit) : IRequest<Result<RetagStaleSongsResponse>>;

/// <summary>
/// Batch outcome. <see cref="Remaining"/> is the number of still-stale songs AFTER this batch, so the caller
/// knows whether to run again. <see cref="StrongModel"/> is the model everything was (re)tagged toward; null
/// when no strong model is registered in the strength map (in which case nothing is ever stale).
/// </summary>
public record RetagStaleSongsResponse(int Enriched, int Failed, int Remaining, string? StrongModel);
