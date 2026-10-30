using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSongsChanged;

/// <param name="AfterId">SongId of the last row the client holds at <paramref name="AfterDate"/>; null = the
/// legacy <c>Version > AfterDate</c> cursor that installed apps send.</param>
public record GetSongsChangedQuery(DateTime AfterDate, string? AfterId = null) : IRequest<Result<GetSongsChangedResponse>>;
