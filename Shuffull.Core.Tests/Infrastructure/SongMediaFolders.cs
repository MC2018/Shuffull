using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;

namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>A throwaway music and album-art folder, and the host's real media store pointed at them.</summary>
public sealed class SongMediaFolders : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shuffull-sizes-{Guid.NewGuid():N}");

    public string MusicRoot => Path.Combine(_root, "music");
    public string AlbumArt => Path.Combine(_root, "album_art");
    public SongMediaStore Store { get; }

    public SongMediaFolders()
    {
        Directory.CreateDirectory(MusicRoot);
        Directory.CreateDirectory(AlbumArt);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shuffull:Files:MusicRootDirectory"] = MusicRoot,
                ["Shuffull:Files:AlbumArtDirectory"] = AlbumArt,
            })
            .Build();
        Store = new SongMediaStore(configuration, new LocalFileStorageService(), NullLogger<SongMediaStore>.Instance);
    }

    public void WriteAudio(string fileHash, string extension, int bytes) => Write(Path.Combine(MusicRoot, $"{fileHash}{extension}"), bytes);

    public void WriteArt(string fileHash, int bytes) => Write(Path.Combine(AlbumArt, $"{fileHash}.jpg"), bytes);

    private static void Write(string path, int bytes)
    {
        using var file = File.Create(path);
        file.SetLength(bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
