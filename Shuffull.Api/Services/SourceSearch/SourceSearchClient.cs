using System.Text.Json;
using Nut.Results;
using Shuffull.Api.Configuration;
using Shuffull.Core.Errors;
using Shuffull.Core.Services;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Api.Services.SourceSearch;

/// <summary>
/// Relays a song search to the producer: <c>GET {BaseUrl}/api/source-search?q=&amp;limit=</c> with the
/// <see cref="KeyHeader"/> header, answered by a JSON array of <see cref="SourceSearchResultDto"/> (YoutubeFunnel#21).
/// The golden files <c>source-search-request</c> and <c>source-search</c> pin both sides. A typed HttpClient, so the
/// timeout and the handler come from <see cref="SourceSearchServiceCollectionExtensions"/>.
///
/// <para>Every failure is the same <see cref="ErrorKind.Unavailable"/> answer to the app; the log says which one it
/// was. Neither the key nor the search text is ever logged.</para>
/// </summary>
public sealed class SourceSearchClient(HttpClient httpClient, SongRequestsConfiguration config, ILogger<SourceSearchClient> logger)
    : ISourceSearchClient
{
    public const string KeyHeader = "X-Search-Key";
    public const string Path = "api/source-search";
    public const string UnavailableMessage = "Song search is unavailable right now. Try again in a moment.";
    public const string NotConfiguredMessage = "Song search is not set up on this server.";

    /// <summary>How the producer's answer is read: camelCase or PascalCase names, as ASP.NET's own defaults bind.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => config.IsConfigured;

    public async Task<Result<IReadOnlyList<SourceSearchResultDto>>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (!config.IsConfigured)
        {
            return Unavailable(NotConfiguredMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Path}?q={Uri.EscapeDataString(query)}&limit={limit}");
        request.Headers.Add(KeyHeader, config.Key);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Song search failed: the producer answered {Status} {Reason}.",
                    (int)response.StatusCode, response.ReasonPhrase);
                return Unavailable(UnavailableMessage);
            }

            var results = await response.Content.ReadFromJsonAsync<List<SourceSearchResultDto>>(JsonOptions, cancellationToken);
            if (results is null)
            {
                logger.LogWarning("Song search failed: the producer answered null.");
                return Unavailable(UnavailableMessage);
            }

            return Result.Ok<IReadOnlyList<SourceSearchResultDto>>(results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Song search failed: the producer took longer than {Timeout}.", httpClient.Timeout);
            return Unavailable(UnavailableMessage);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException)
        {
            logger.LogWarning("Song search failed: {Error}", e.Message);
            return Unavailable(UnavailableMessage);
        }
    }

    private static Result<IReadOnlyList<SourceSearchResultDto>> Unavailable(string message)
        => Result.Error<IReadOnlyList<SourceSearchResultDto>>(ResultErrors.Unavailable(message));
}
