using Nut.Results;
using Shuffull.Core.Features.Songs.GetSongPage;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.GetSongPage;

public class GetSongPageHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetSongPageHandler _handler;

    public GetSongPageHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetSongPageHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Song NewSong(string id, string name = "Test Song")
        => new()
        {
            SongId = id,
            Name = name,
            FileExtension = ".mp3",
            FileHash = $"hash-{id}",
            ExternalSongId = null,
        };

    [Fact]
    public async Task Handle_ReturnsFirstPageOrderedById()
    {
        // Arrange — inserted out of id order to prove the spec orders the results.
        await _database.Context.Songs.AddRangeAsync(NewSong("c"), NewSong("a"), NewSong("b"));
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongPageQuery(0), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.Equal(0, response.PageIndex);
        Assert.Equal(new[] { "a", "b", "c" }, response.Songs.Select(s => s.SongId).ToArray());
    }

    [Fact]
    public async Task Handle_WithPageBeyondData_ReturnsEmptyList()
    {
        // Arrange
        await _database.Context.Songs.AddRangeAsync(NewSong("a"), NewSong("b"));
        await _database.Context.SaveChangesAsync();

        // Act — page 1 is past the end (page size is far larger than the seeded data).
        var result = await _handler.Handle(new GetSongPageQuery(1), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(1, result.Get().PageIndex);
        Assert.Empty(result.Get().Songs);
    }
}
