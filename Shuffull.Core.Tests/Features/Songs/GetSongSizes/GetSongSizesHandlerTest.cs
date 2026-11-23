using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Features.Songs.GetSongSizes;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.GetSongSizes;

/// <summary>
/// The auto-download sizes query (Shuffull#150), over real files on disk through the host's real media store. Covers
/// that it lists only the caller's library, that a missing audio file drops one song instead of failing the call, that
/// missing art counts as 0, and that it changes no <c>Version</c> (sizes must never re-sync songs to every device).
/// </summary>
public class GetSongSizesHandlerTest : IDisposable
{
    private static readonly DateTime SongVersion = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UserSongVersion = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();
    private readonly MediaFolders _media = new();
    private readonly User _me = NewUser("me");
    private readonly User _other = NewUser("other");

    public GetSongSizesHandlerTest()
    {
        _database.Context.Users.AddRange(_me, _other);
        _database.Context.SaveChanges();
    }

    public void Dispose()
    {
        _database.Dispose();
        _media.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static User NewUser(string id) => new() { UserId = id, Username = id, Version = SongVersion, ServerHash = "hash" };

    private void Seed(string songId, string owner, string extension = ".mp3")
    {
        _database.Context.Songs.Add(new Song
        {
            SongId = songId,
            Name = songId,
            FileHash = $"hash-{songId}",
            FileExtension = extension,
            Version = SongVersion,
        });
        _database.Context.UserSongs.Add(new UserSong { UserId = owner, SongId = songId, LastPlayed = SongVersion, Version = UserSongVersion });
        _database.Context.SaveChanges();
    }

    private async Task<IReadOnlyList<SongSize>> RunAsync(string userId)
    {
        var result = await new GetSongSizesHandler(_database.CreateContext(), _media.Store)
            .Handle(new GetSongSizesQuery(userId), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    [Fact]
    public async Task Handle_ReturnsAudioAndArtBytesOfTheCallersSongs()
    {
        Seed("song-a", _me.UserId);
        Seed("song-b", _me.UserId, ".m4a");
        _media.WriteAudio("hash-song-a", ".mp3", 3_570_000);
        _media.WriteArt("hash-song-a", 41_000);
        _media.WriteAudio("hash-song-b", ".m4a", 2_000_000);
        _media.WriteArt("hash-song-b", 30_000);

        var sizes = await RunAsync(_me.UserId);

        Assert.Equal(
            [new SongSize("song-a", 3_570_000, 41_000), new SongSize("song-b", 2_000_000, 30_000)],
            sizes);
    }

    [Fact]
    public async Task Handle_LeavesOutAnotherUsersSongs()
    {
        Seed("mine", _me.UserId);
        Seed("theirs", _other.UserId);
        _media.WriteAudio("hash-mine", ".mp3", 100);
        _media.WriteAudio("hash-theirs", ".mp3", 200);

        var sizes = await RunAsync(_me.UserId);

        Assert.Equal(["mine"], sizes.Select(s => s.SongId));
    }

    [Fact]
    public async Task Handle_MissingAudioFile_LeavesThatSongOut_AndStillAnswers()
    {
        Seed("present", _me.UserId);
        Seed("missing", _me.UserId);
        _media.WriteAudio("hash-present", ".mp3", 100);
        // The missing song's art is there, but without audio there is nothing to download.
        _media.WriteArt("hash-missing", 50);

        var sizes = await RunAsync(_me.UserId);

        Assert.Equal([new SongSize("present", 100, 0)], sizes);
    }

    [Fact]
    public async Task Handle_MissingArt_CountsAsZero()
    {
        Seed("no-art", _me.UserId);
        _media.WriteAudio("hash-no-art", ".mp3", 100);

        var sizes = await RunAsync(_me.UserId);

        Assert.Equal([new SongSize("no-art", 100, 0)], sizes);
    }

    [Fact]
    public async Task Handle_EmptyLibrary_IsAnEmptyList()
    {
        Assert.Empty(await RunAsync(_me.UserId));
    }

    [Fact]
    public async Task Handle_ChangesNoVersion()
    {
        Seed("song-a", _me.UserId);
        Seed("song-b", _me.UserId);
        _media.WriteAudio("hash-song-a", ".mp3", 100);

        await RunAsync(_me.UserId);

        await using var context = _database.CreateContext();
        Assert.All(await context.Songs.Select(s => s.Version).ToListAsync(), v => Assert.Equal(SongVersion, v));
        Assert.All(await context.UserSongs.Select(us => us.Version).ToListAsync(), v => Assert.Equal(UserSongVersion, v));
    }

    /// <summary>A throwaway music and album-art folder, and the host's real media store pointed at them.</summary>
    internal sealed class MediaFolders : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"shuffull-sizes-{Guid.NewGuid():N}");

        public string MusicRoot => Path.Combine(_root, "music");
        public string AlbumArt => Path.Combine(_root, "album_art");
        public SongMediaStore Store { get; }

        public MediaFolders()
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
}
