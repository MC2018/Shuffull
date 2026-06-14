using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Tags.AddTagToSong;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Tags.AddTagToSong;

public class AddTagToSongHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly AddTagToSongHandler _handler;

    public AddTagToSongHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new AddTagToSongHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Song> SeedSongAsync()
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            ExternalSongId = null,
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task<Tag> SeedTagAsync()
    {
        var tag = new Genre { TagId = Guid.NewGuid().ToString(), Name = "Rock" };
        await _database.Context.Tags.AddAsync(tag);
        await _database.Context.SaveChangesAsync();
        return tag;
    }

    [Fact]
    public async Task Handle_WithValidCommand_AddsTag()
    {
        // Arrange
        var song = await SeedSongAsync();
        var tag = await SeedTagAsync();

        // Act
        var result = await _handler.Handle(
            new AddTagToSongCommand(song.SongId, tag.TagId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.True(result.Get().Added);

        var joinExists = await _database.Context.SongTags.AsNoTracking()
            .AnyAsync(st => st.SongId == song.SongId && st.TagId == tag.TagId);
        Assert.True(joinExists);
    }

    [Fact]
    public async Task Handle_WhenSongAlreadyTagged_ReturnsAddedFalse()
    {
        // Arrange
        var song = await SeedSongAsync();
        var tag = await SeedTagAsync();
        await _database.Context.SongTags.AddAsync(new SongTag
        {
            SongTagId = Guid.NewGuid().ToString(),
            SongId = song.SongId,
            TagId = tag.TagId,
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new AddTagToSongCommand(song.SongId, tag.TagId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.False(result.Get().Added);

        var joinCount = await _database.Context.SongTags.AsNoTracking()
            .CountAsync(st => st.SongId == song.SongId && st.TagId == tag.TagId);
        Assert.Equal(1, joinCount);
    }

    [Fact]
    public async Task Handle_WithMissingTag_ReturnsError()
    {
        // Arrange
        var song = await SeedSongAsync();

        // Act
        var result = await _handler.Handle(
            new AddTagToSongCommand(song.SongId, Guid.NewGuid().ToString()),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Tag not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithMissingSong_ReturnsError()
    {
        // Arrange
        var tag = await SeedTagAsync();

        // Act
        var result = await _handler.Handle(
            new AddTagToSongCommand(Guid.NewGuid().ToString(), tag.TagId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Song not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }
}
