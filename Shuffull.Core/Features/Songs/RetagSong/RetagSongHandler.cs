using MediatR;
using Nut.Results;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Songs.RetagSong;

/// <summary>Delegates to the enrichment service (implemented in Shuffull.Api). Authorization is upstream.</summary>
public class RetagSongHandler(ISongEnrichmentService enrichment) : IRequestHandler<RetagSongCommand, Result<RetagSongResponse>>
{
    public async Task<Result<RetagSongResponse>> Handle(RetagSongCommand request, CancellationToken cancellationToken)
    {
        var result = await enrichment.EnrichSongAsync(request.SongId, cancellationToken);
        return result.IsError
            ? Result.Error<RetagSongResponse>(result.GetError().Message)
            : Result.Ok(new RetagSongResponse(request.SongId, result.Get()));
    }
}
