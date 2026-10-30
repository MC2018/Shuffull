using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Listens.GetListenStats;

/// <summary>
/// The caller's per-song listen stats, derived from their own rows in the listen log and nobody else's.
/// </summary>
/// <param name="Rule">The "maybe drop this" rule; the API always passes <see cref="MaybeDropRule.Default"/>.</param>
public record GetListenStatsQuery(string UserId, MaybeDropRule Rule) : IRequest<Result<GetListenStatsResponse>>;
