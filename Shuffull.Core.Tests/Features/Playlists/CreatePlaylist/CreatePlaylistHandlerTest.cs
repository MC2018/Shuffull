using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Playlists.CreatePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.CreatePlaylist;

public class CreatePlaylistHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly CreatePlaylistHandler _handler;

    public CreatePlaylistHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new CreatePlaylistHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "tester",
            Version = DateTime.UtcNow,
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_WithValidCommand_PersistsPlaylistAndReturnsDto()
    {
        // Arrange
        var user = await SeedUserAsync();

        // Act
        var result = await _handler.Handle(new CreatePlaylistCommand(user.UserId, "My Playlist"), CancellationToken.None);

        // Assert — returned DTO
        Assert.True(result.IsOk);
        var dto = result.Get().Playlist;
        Assert.Equal(user.UserId, dto.UserId);
        Assert.Equal("My Playlist", dto.Name);
        Assert.False(string.IsNullOrEmpty(dto.PlaylistId));
        Assert.Empty(dto.SongIds);

        // Assert — actually persisted
        var saved = await _database.Context.Playlists.AsNoTracking()
            .SingleOrDefaultAsync(p => p.PlaylistId == dto.PlaylistId);
        Assert.NotNull(saved);
        Assert.Equal(user.UserId, saved!.UserId);
        Assert.Equal("My Playlist", saved.Name);
    }

    [Fact]
    public async Task Handle_SetsExpectedDefaults()
    {
        // Arrange
        var user = await SeedUserAsync();

        // Act
        var result = await _handler.Handle(new CreatePlaylistCommand(user.UserId, "Defaults"), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = result.Get().Playlist;
        Assert.Null(dto.CurrentSongId);
        Assert.Equal(0.9m, dto.PercentUntilReplayable);
        Assert.True(dto.Version <= DateTime.UtcNow && dto.Version > DateTime.UtcNow.AddMinutes(-1));
    }
}
