using Shuffull.Api.Configuration;
using Shuffull.Core.Services;

namespace Shuffull.Api.Services.SourceSearch;

public static class SourceSearchServiceCollectionExtensions
{
    /// <summary>A search answer bigger than this is refused. Twenty-five results are a few kilobytes.</summary>
    public const long MaxResponseBytes = 1024 * 1024;

    /// <summary>
    /// The producer's song search (<see cref="SourceSearchClient"/>) from <c>Shuffull:SongRequests</c>. Registered
    /// whether or not it is configured: unconfigured, it answers every search as unavailable without a call.
    /// </summary>
    public static IHttpClientBuilder AddSourceSearch(this IServiceCollection services, IConfiguration configuration)
    {
        var config = configuration.GetSection(SongRequestsConfiguration.SongRequestsConfigurationSection).Get<SongRequestsConfiguration>()
            ?? new SongRequestsConfiguration();
        services.AddSingleton(config);

        return services.AddHttpClient<ISourceSearchClient, SourceSearchClient>(client =>
            {
                client.Timeout = config.Timeout;
                client.MaxResponseContentBufferSize = MaxResponseBytes;
                if (config.BaseUri is { } baseUri)
                {
                    client.BaseAddress = baseUri;
                }
            })
            // The default logger writes each request's URL, search text included, to the log. The client logs its own
            // failures instead, without the text or the key.
            .RemoveAllLoggers();
    }
}
