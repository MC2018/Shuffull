using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The upload endpoint is reachable from the internet, so it takes the uploader from the token and nothing else.
/// </summary>
public class SongUploadsControllerTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly Mock<IFileStorageService> _storage = new();

    public SongUploadsControllerTest()
    {
        _storage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<ApiTestHost> StartAsync(User? signedInAs) => ApiTestHost.StartAsync(services =>
    {
        services.AddScoped<ShuffullContext>(_ => _database.CreateContext());
        services.AddSingleton(_storage.Object);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Shuffull:Files:SongImportDirectory"] = "/imports" })
            .Build());
    }, signedInAs);

    private async Task<User> SeedUserAsync(string username)
    {
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = username, Version = DateTime.UtcNow, ServerHash = "h" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private static MultipartFormDataContent Upload(string? playlistName = null, string? username = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "files", "song.mp3");
        if (playlistName != null) form.Add(new StringContent(playlistName), "playlistName");
        if (username != null) form.Add(new StringContent(username), "username");
        return form;
    }

    [Fact]
    public async Task Upload_WithoutAToken_IsRefusedAndWritesNothing()
    {
        var alice = await SeedUserAsync("alice");
        await using var host = await StartAsync(signedInAs: null);

        // The old contract: name any user in the form.
        var response = await host.Client.PostAsync("/api/v1/song-uploads", Upload(username: alice.Username));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await _database.CreateContext().SongImports.ToListAsync());
        _storage.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_IsFiledUnderTheTokensUser_NotTheNameInTheForm()
    {
        var alice = await SeedUserAsync("alice");
        var bob = await SeedUserAsync("bob");
        await using var host = await StartAsync(signedInAs: alice);

        var response = await host.Client.PostAsync("/api/v1/song-uploads", Upload(username: bob.Username));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var import = Assert.Single(await _database.CreateContext().SongImports.ToListAsync());
        Assert.Equal(alice.UserId, import.UserId);
    }

    [Fact]
    public async Task Upload_ToAPlaylistName_NeverLandsInAnotherUsersPlaylist()
    {
        var alice = await SeedUserAsync("alice");
        var bob = await SeedUserAsync("bob");
        var bobsMix = new Playlist { PlaylistId = Guid.NewGuid().ToString(), UserId = bob.UserId, Name = "Mix", Version = DateTime.UtcNow };
        _database.Context.Playlists.Add(bobsMix);
        await _database.Context.SaveChangesAsync();
        await using var host = await StartAsync(signedInAs: alice);

        var response = await host.Client.PostAsync("/api/v1/song-uploads", Upload(playlistName: "Mix"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var context = _database.CreateContext();
        var import = Assert.Single(await context.SongImports.ToListAsync());
        Assert.NotEqual(bobsMix.PlaylistId, import.PlaylistId);
        var alicesMix = await context.Playlists.SingleAsync(p => p.PlaylistId == import.PlaylistId);
        Assert.Equal(alice.UserId, alicesMix.UserId);
        Assert.Equal("Mix", alicesMix.Name);
    }
}
