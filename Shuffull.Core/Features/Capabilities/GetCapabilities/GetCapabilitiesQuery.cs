using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Capabilities.GetCapabilities;

/// <summary>Which optional features this server has set up, so the app can hide the rest.</summary>
public record GetCapabilitiesQuery : IRequest<Result<CapabilitiesResponse>>;
