using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The media sweep retries a tombstone only when the store reports a failure, so the store must not swallow one. A
/// missing file still counts as deleted, which is what makes the retry safe.
/// </summary>
public class SongMediaStoreTest
{
    private const string MusicRoot = "/music";
    private const string AlbumArt = "/art";

    private readonly Mock<IFileStorageService> _storage = new();
    private readonly SongMediaStore _store;

    public SongMediaStoreTest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shuffull:Files:MusicRootDirectory"] = MusicRoot,
                ["Shuffull:Files:AlbumArtDirectory"] = AlbumArt,
            })
            .Build();
        _store = new SongMediaStore(configuration, _storage.Object, NullLogger<SongMediaStore>.Instance);
    }

    [Fact]
    public async Task Delete_BothFilesDeleted_ReturnsOk()
    {
        _storage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());

        var result = await _store.DeleteSongMediaAsync("hash", ".mp3");

        Assert.True(result.IsOk);
        _storage.Verify(s => s.DeleteFileAsync(Path.Combine(MusicRoot, "hash.mp3"), It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteFileAsync(Path.Combine(AlbumArt, "hash.jpg"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_StorageFails_ReturnsErrorAfterTryingEveryFile()
    {
        var audioPath = Path.Combine(MusicRoot, "hash.mp3");
        _storage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _storage.Setup(s => s.DeleteFileAsync(audioPath, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Error("permission denied"));

        var result = await _store.DeleteSongMediaAsync("hash", ".mp3");

        Assert.True(result.IsError);
        Assert.Contains(audioPath, result.GetError().Message);
        _storage.Verify(s => s.DeleteFileAsync(Path.Combine(AlbumArt, "hash.jpg"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
