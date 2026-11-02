namespace Shuffull.Api.Configuration;

/// <summary>
/// The <c>Shuffull:SongRequests</c> section: where the producer's song search lives (Shuffull#58). Both values come
/// from env only (<c>Shuffull__SongRequests__BaseUrl</c>, <c>Shuffull__SongRequests__Key</c>), never a committed file:
/// this repo is public. Leave either unset and search is off: the API runs as before, the search endpoint answers 503
/// and <c>GET /api/v1/capabilities</c> says <c>songRequests: false</c>.
/// </summary>
public class SongRequestsConfiguration
{
    public const string SongRequestsConfigurationSection = "Shuffull:SongRequests";

    /// <summary>The producer's web API over the shared <c>shuffull-ingest-net</c>, e.g. <c>http://youtubefunnel.web-api:8080</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The search's own key, sent as <c>X-Search-Key</c>. Not the import key, which guards the other direction.</summary>
    public string? Key { get; set; }

    /// <summary>How long a search may take before the app is told search is unavailable.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary><see cref="BaseUrl"/> as an absolute http(s) address ending in a slash, so a path under it resolves; null if it isn't one.</summary>
    public Uri? BaseUri =>
        Uri.TryCreate(BaseUrl?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : null;

    public bool IsConfigured => BaseUri is not null && !string.IsNullOrWhiteSpace(Key);
}
