using Microsoft.Extensions.DependencyInjection.Extensions;
using Shuffull.Core.Services;

namespace Shuffull.Api.Hubs;

/// <summary>Wiring for the cross-device playback channel (Shuffull#6).</summary>
public static class PlaybackChannelRegistration
{
    /// <summary>SignalR, the in-memory presence map, and the notifier the playback handlers signal through.</summary>
    public static IServiceCollection AddPlaybackChannel(this IServiceCollection services)
    {
        services.AddSignalR();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PlaybackPresence>();
        services.AddSingleton<IPlaybackPresence>(sp => sp.GetRequiredService<PlaybackPresence>());
        services.AddSingleton<IPlaybackNotifier, SignalRPlaybackNotifier>();
        return services;
    }

    /// <summary>The hub's sign-in gate. Must run after <c>JwtMiddleware</c>, which resolves the token.</summary>
    public static IApplicationBuilder UsePlaybackHubSignIn(this IApplicationBuilder app) => app.UseMiddleware<PlaybackHubSignIn>();

    public static HubEndpointConventionBuilder MapPlaybackHub(this IEndpointRouteBuilder endpoints)
        => endpoints.MapHub<PlaybackHub>(PlaybackHub.Path);
}
