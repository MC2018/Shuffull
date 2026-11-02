namespace Shuffull.Core.Features.Capabilities.GetCapabilities;

/// <summary>
/// The optional features this server has set up. A feature the app doesn't know is ignored, and one missing from an
/// older server reads as false, so new flags can be added freely.
/// </summary>
/// <param name="SongRequests">Song search for requests is configured (Shuffull#58): the producer's search URL and key are set.</param>
public record CapabilitiesResponse(bool SongRequests);
