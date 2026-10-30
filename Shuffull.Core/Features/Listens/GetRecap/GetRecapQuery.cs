using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Listens.GetRecap;

/// <summary>
/// The caller's recap for one year, derived from their own listens and nobody else's.
/// </summary>
/// <param name="Year">The calendar year, in <paramref name="TimeZone"/>.</param>
/// <param name="TimeZone">An IANA zone id or a fixed offset, as <see cref="RecapTimeZone.TryParse"/> reads it; null is
/// UTC.</param>
public record GetRecapQuery(string UserId, int Year, string? TimeZone) : IRequest<Result<GetRecapResponse>>;
