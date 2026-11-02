using MediatR;
using Nut.Results;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Capabilities.GetCapabilities;

public class GetCapabilitiesHandler(ISourceSearchClient sourceSearch)
    : IRequestHandler<GetCapabilitiesQuery, Result<CapabilitiesResponse>>
{
    public Task<Result<CapabilitiesResponse>> Handle(GetCapabilitiesQuery request, CancellationToken cancellationToken)
        => Task.FromResult(Result.Ok(new CapabilitiesResponse(SongRequests: sourceSearch.IsConfigured)));
}
