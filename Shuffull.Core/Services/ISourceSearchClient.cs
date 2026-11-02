using Nut.Results;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Core.Services;

/// <summary>
/// The producer's search of the external source (Shuffull#58), which the app uses to find songs to request. It is
/// read-only: a search that fails loses nothing, so it is a plain relay, not a durable row. Requests themselves stay
/// on <c>SongRequest</c> rows (Shuffull#57).
///
/// <para>Defined in Core so the search slice can drive it; implemented in Shuffull.Api as a typed HttpClient.</para>
/// </summary>
public interface ISourceSearchClient
{
    /// <summary>
    /// Whether this server has the producer's search URL and key. Without them search is off, and the capabilities
    /// endpoint tells the app so, which hides the feature.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Up to <paramref name="limit"/> songs (audio only, never music videos) matching <paramref name="query"/>, as the
    /// producer sent them. Any failure, including no configuration, a timeout or a bad answer, is an
    /// <c>ErrorKind.Unavailable</c> error. Throws only when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<Result<IReadOnlyList<SourceSearchResultDto>>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}
