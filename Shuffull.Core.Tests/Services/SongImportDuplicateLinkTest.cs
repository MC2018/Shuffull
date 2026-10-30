using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// A duplicate import still links the song it matched to the user and playlist it was for (MC2018/Shuffull#56),
/// without changing the existing song.
/// </summary>
public class SongImportDuplicateLinkTest : IDisposable
{
    private static readonly byte[] AudioBytes = [1, 2, 3, 4, 5];
    private static readonly string AudioHash = Hasher.ShaHash(AudioBytes).Substring(0, 32);
    private static readonly DateTime SongVersion = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(string name)
    {
        var user = new User { UserId = $"user-{name}", Username = name, Version = SongVersion, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<Playlist> SeedPlaylistAsync(string userId, string name, bool isExploratory = false)
    {
        var playlist = new Playlist
        {
            PlaylistId = $"playlist-{name}",
            UserId = userId,
            Name = name,
            PercentUntilReplayable = 0.9m,
            IsExploratory = isExploratory,
            Version = SongVersion,
        };
        _database.Context.Playlists.Add(playlist);
        await _database.Context.SaveChangesAsync();
        return playlist;
    }

    /// <summary>User A's kept, tagged song, in A's library and one of A's playlists.</summary>
    private async Task<Song> SeedOwnedSongAsync(string ownerId, bool exploratory = false)
    {
        var song = new Song
        {
            SongId = "song-1",
            Name = "Everybody Wants You",
            FileExtension = ".mp3",
            FileHash = AudioHash,
            ExternalSongId = "yt-1",
            Exploratory = exploratory,
            TagModel = "gpt-5.6-terra",
            MetadataLocked = true,
            Version = SongVersion,
        };
        var playlist = await SeedPlaylistAsync(ownerId, "A's mix");
        _database.Context.Songs.Add(song);
        _database.Context.PlaylistSongs.Add(new PlaylistSong { PlaylistSongId = "ps-a", PlaylistId = playlist.PlaylistId, SongId = song.SongId });
        _database.Context.UserSongs.Add(new UserSong { UserId = ownerId, SongId = song.SongId, LastPlayed = DateTime.MinValue, Version = SongVersion, LikeStatus = LikeStatus.Like });
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private static SongImport NewImport(string userId, string? playlistId = null, string? playlistName = null, bool exploratory = false, bool liked = false) => new()
    {
        SongImportId = Guid.NewGuid().ToString(),
        Name = "Everybody Wants You",
        ImportFolder = "folder",
        FileType = ".mp3",
        UserId = userId,
        ExternalSongId = "yt-1",
        PlaylistId = playlistId,
        TargetPlaylistName = playlistName,
        Exploratory = exploratory,
        MarkAsLiked = liked,
        LastUpdatedAt = DateTime.UtcNow,
    };

    private async Task<Result> LinkAsync(SongImport songImport)
    {
        using var context = _database.CreateContext();
        return await SongImportService.LinkDuplicateCoreAsync(context, "song-1", songImport);
    }

    private Task<Song> ReadSongAsync() => _database.CreateContext().Songs.AsNoTracking().SingleAsync(s => s.SongId == "song-1");

    [Fact]
    public async Task ImportNext_DuplicateForAnotherUser_LinksTheExistingSongAndCompletesTheImport()
    {
        // Before the fix the import was dropped (and marked Failed) before either link was made, and SongId stayed null.
        var userA = await SeedUserAsync("a");
        var userB = await SeedUserAsync("b");
        await SeedOwnedSongAsync(userA.UserId);
        var playlistB = await SeedPlaylistAsync(userB.UserId, "B's mix");
        var songImport = NewImport(userB.UserId, playlistId: playlistB.PlaylistId);
        _database.Context.SongImports.Add(songImport);
        await _database.Context.SaveChangesAsync();

        var services = new ServiceCollection()
            .AddScoped<ShuffullContext>(_ => _database.CreateContext())
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Shuffull:Files:MusicRootDirectory"] = "music" })
            .Build();
        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(x => x.DownloadFileBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(AudioBytes));
        var importer = new SongImportService(configuration, services, NullLogger<SongImportService>.Instance, fileStorage.Object);

        Assert.True(await importer.ImportNextAsync(CancellationToken.None));

        using var read = _database.CreateContext();
        var userSong = await read.UserSongs.AsNoTracking().SingleAsync(us => us.UserId == userB.UserId && us.SongId == "song-1");
        Assert.Equal(LikeStatus.Neutral, userSong.LikeStatus);
        Assert.True(userSong.Version > SongVersion);
        Assert.True(await read.PlaylistSongs.AnyAsync(ps => ps.PlaylistId == playlistB.PlaylistId && ps.SongId == "song-1"));
        Assert.True((await read.Playlists.AsNoTracking().SingleAsync(p => p.PlaylistId == playlistB.PlaylistId)).Version > SongVersion);

        var import = await read.SongImports.AsNoTracking().SingleAsync(si => si.SongImportId == songImport.SongImportId);
        Assert.Equal(SongImportState.Completed, import.State);
        Assert.Equal("song-1", import.SongId);

        // The existing song is untouched, and nothing was added for it twice.
        var song = await read.Songs.AsNoTracking().SingleAsync();
        Assert.False(song.Exploratory);
        Assert.True(song.MetadataLocked);
        Assert.Equal("gpt-5.6-terra", song.TagModel);
        Assert.Equal(SongVersion, song.Version);
        Assert.Equal(2, await read.UserSongs.CountAsync());
        Assert.Equal(2, await read.PlaylistSongs.CountAsync());
    }

    [Fact]
    public async Task AuditionImportOfAKeptSong_DoesNotDemoteIt_AndCreatesItsAuditionPlaylist()
    {
        var userA = await SeedUserAsync("a");
        var userB = await SeedUserAsync("b");
        await SeedOwnedSongAsync(userA.UserId);

        var result = await LinkAsync(NewImport(userB.UserId, playlistName: "Audition · week 44", exploratory: true));

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        var song = await ReadSongAsync();
        Assert.False(song.Exploratory);
        Assert.Equal(SongVersion, song.Version);
        using var read = _database.CreateContext();
        var playlist = await read.Playlists.AsNoTracking().SingleAsync(p => p.UserId == userB.UserId && p.Name == "Audition · week 44");
        Assert.True(playlist.IsExploratory);
        Assert.True(await read.PlaylistSongs.AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == "song-1"));
        Assert.True(await read.UserSongs.AnyAsync(us => us.UserId == userB.UserId && us.SongId == "song-1"));
    }

    [Fact]
    public async Task RepeatedDuplicate_LinksOnce_AndKeepsTheUsersOwnLikeStatus()
    {
        var userA = await SeedUserAsync("a");
        await SeedOwnedSongAsync(userA.UserId);
        _database.Context.UserSongs.Single(us => us.UserId == userA.UserId).LikeStatus = LikeStatus.Dislike;
        await _database.Context.SaveChangesAsync();

        // The owner's own re-import, liked on the source, into the playlist the song is already on.
        Assert.True((await LinkAsync(NewImport(userA.UserId, playlistId: "playlist-A's mix", liked: true))).IsOk);
        Assert.True((await LinkAsync(NewImport(userA.UserId, playlistId: "playlist-A's mix", liked: true))).IsOk);

        using var read = _database.CreateContext();
        Assert.Equal(LikeStatus.Dislike, (await read.UserSongs.AsNoTracking().SingleAsync()).LikeStatus);
        Assert.Equal(1, await read.PlaylistSongs.CountAsync());
        Assert.Equal(SongVersion, (await read.Playlists.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task LikedDuplicateOfAnAuditionSong_PromotesIt()
    {
        // A like keeps the song (Shuffull#38), so it can't stay purge-eligible as Exploratory.
        var userA = await SeedUserAsync("a");
        var userB = await SeedUserAsync("b");
        await SeedOwnedSongAsync(userA.UserId, exploratory: true);

        Assert.True((await LinkAsync(NewImport(userB.UserId, liked: true))).IsOk);

        var song = await ReadSongAsync();
        Assert.False(song.Exploratory);
        Assert.True(song.Version > SongVersion);
        using var read = _database.CreateContext();
        Assert.Equal(LikeStatus.Like, (await read.UserSongs.AsNoTracking().SingleAsync(us => us.UserId == userB.UserId)).LikeStatus);
    }

    [Fact]
    public async Task NeutralDuplicateOfAnAuditionSong_LeavesItExploratory()
    {
        var userA = await SeedUserAsync("a");
        var userB = await SeedUserAsync("b");
        await SeedOwnedSongAsync(userA.UserId, exploratory: true);

        Assert.True((await LinkAsync(NewImport(userB.UserId))).IsOk);

        var song = await ReadSongAsync();
        Assert.True(song.Exploratory);
        Assert.Equal(SongVersion, song.Version);
    }

    [Fact]
    public async Task MissingSong_IsAnError()
    {
        var userB = await SeedUserAsync("b");
        using var context = _database.CreateContext();

        var result = await SongImportService.LinkDuplicateCoreAsync(context, "gone", NewImport(userB.UserId));

        Assert.True(result.IsError);
        Assert.False(await _database.CreateContext().UserSongs.AnyAsync());
    }
}
