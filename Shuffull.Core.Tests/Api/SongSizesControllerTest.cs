using System.Net;
using System.Text.Json.Nodes;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// <c>GET /api/v1/songs/sizes</c> through the real controller and auth filter: no token is a 401, any signed-in user
/// (no curator role) gets their library's sizes, and the literal route isn't taken for a song id by
/// <c>GET /api/v1/songs/{songId}</c>.
/// </summary>
public class SongSizesControllerTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly SongMediaFolders _media = new();
    private readonly User _listener = new() { UserId = "listener", Username = "listener", Version = DateTime.UtcNow, ServerHash = "hash" };

    public SongSizesControllerTest()
    {
        _database.Context.Users.Add(_listener);
        _database.Context.Songs.Add(new Song { SongId = "song-1", Name = "Song", FileHash = "hash-1", FileExtension = ".mp3", Version = DateTime.UtcNow });
        _database.Context.UserSongs.Add(new UserSong { UserId = _listener.UserId, SongId = "song-1", LastPlayed = DateTime.UtcNow, Version = DateTime.UtcNow });
        _database.Context.SaveChanges();
        _media.WriteAudio("hash-1", ".mp3", 1234);
        _media.WriteArt("hash-1", 56);
    }

    public void Dispose()
    {
        _database.Dispose();
        _media.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<ApiTestHost> StartAsync(User? signedInAs) => ApiTestHost.StartAsync(services =>
    {
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        services.AddScoped<ShuffullContext>(_ => _database.CreateContext());
        services.AddSingleton<ISongMediaSizes>(_media.Store);
        services.AddSingleton<IHealthSignals>(new RecordingHealthSignals());
        services.AddValidatorsFromAssemblies([coreAssembly]);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
    }, signedInAs);

    [Fact]
    public async Task NoToken_Is401()
    {
        await using var host = await StartAsync(signedInAs: null);

        var response = await host.Client.GetAsync("api/v1/songs/sizes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignedInListener_GetsTheirSongSizes()
    {
        await using var host = await StartAsync(_listener);

        var response = await host.Client.GetAsync("api/v1/songs/sizes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var song = Assert.Single(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray())!;
        Assert.Equal(("song-1", 1234L, 56L),
            (song["songId"]!.GetValue<string>(), song["audioBytes"]!.GetValue<long>(), song["artBytes"]!.GetValue<long>()));
    }
}
