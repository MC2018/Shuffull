using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Songs.GetDeletedSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// The user's deleted-songs list (ShuffullApp#59): only the caller's own tombstones, newest first, with keyset paging
/// that neither skips nor repeats inside one purge (every song a purge removes shares its DeletedAt).
/// </summary>
public class GetDeletedSongsHandlerTest : IDisposable
{
    private static readonly DateTime Earlier = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database;

    public GetDeletedSongsHandlerTest() => _database = new DatabaseFixture();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync(string songId, string userId, DateTime deletedAt, string? artistNames = null,
        string? externalId = null, string? playlistName = null)
    {
        _database.Context.SongTombstones.Add(new SongTombstone
        {
            SongId = songId,
            Name = $"Name {songId}",
            ArtistNames = artistNames,
            ExternalSongId = externalId,
            FileHash = $"hash-{songId}",
            FileExtension = ".mp3",
            DeletedByUserId = userId,
            PlaylistId = "playlist-1",
            PlaylistName = playlistName,
            DeletedAt = deletedAt,
        });
        await _database.Context.SaveChangesAsync();
    }

    private Task<Result<DeletedSongsPage>> RunAsync(string userId, string? before = null, int limit = 0)
        => new GetDeletedSongsHandler(_database.Context)
            .Handle(new GetDeletedSongsQuery(userId, before, limit), CancellationToken.None);

    [Fact]
    public async Task Handle_ReturnsTheTombstoneFields()
    {
        await SeedAsync("s1", "user-a", Later, "Billy Squier, Burnard", "dQw4w9WgXcQ", "Audition · week of Oct 20");

        var page = (await RunAsync("user-a")).Get();

        var song = Assert.Single(page.Songs);
        Assert.Equal("s1", song.SongId);
        Assert.Equal("Name s1", song.Name);
        Assert.Equal("Billy Squier, Burnard", song.ArtistNames);
        Assert.Equal("dQw4w9WgXcQ", song.ExternalSongId);
        Assert.Equal("Audition · week of Oct 20", song.PlaylistName);
        Assert.Equal(Later, song.DeletedAt);
        Assert.Null(page.NextBefore);
    }

    [Fact]
    public async Task Handle_OldTombstoneWithoutNames_ReturnsNulls()
    {
        await SeedAsync("old", "user-a", Earlier);

        var song = Assert.Single((await RunAsync("user-a")).Get().Songs);

        Assert.Null(song.ArtistNames);
        Assert.Null(song.ExternalSongId);
        Assert.Null(song.PlaylistName);
    }

    [Fact]
    public async Task Handle_NeverReturnsAnotherUsersTombstones()
    {
        await SeedAsync("mine", "user-a", Earlier);
        await SeedAsync("theirs-1", "user-b", Later);
        await SeedAsync("theirs-2", "user-b", Earlier);

        var forA = (await RunAsync("user-a")).Get();
        var forB = (await RunAsync("user-b")).Get();
        var forNobody = (await RunAsync("user-c")).Get();

        Assert.Equal(["mine"], forA.Songs.Select(s => s.SongId));
        Assert.Equal(["theirs-1", "theirs-2"], forB.Songs.Select(s => s.SongId));
        Assert.Empty(forNobody.Songs);
        Assert.Null(forNobody.NextBefore);
    }

    [Fact]
    public async Task Handle_NewestFirst_ThenByIdDescending()
    {
        await SeedAsync("a", "user-a", Earlier);
        await SeedAsync("b", "user-a", Later);
        await SeedAsync("c", "user-a", Later);

        var page = (await RunAsync("user-a")).Get();

        Assert.Equal(["c", "b", "a"], page.Songs.Select(s => s.SongId));
    }

    [Fact]
    public async Task Handle_PagesThroughOnePurge_WithoutSkippingOrRepeating()
    {
        // Five songs from one purge share a DeletedAt, plus one from an earlier purge and one of another user's.
        foreach (var id in new[] { "p1", "p2", "p3", "p4", "p5" })
        {
            await SeedAsync(id, "user-a", Later);
        }
        await SeedAsync("e1", "user-a", Earlier);
        await SeedAsync("x1", "user-b", Later);

        var seen = new List<string>();
        string? before = null;
        var pages = 0;
        do
        {
            var page = (await RunAsync("user-a", before, limit: 2)).Get();
            Assert.True(page.Songs.Count <= 2);
            seen.AddRange(page.Songs.Select(s => s.SongId));
            before = page.NextBefore;
            pages++;
        } while (before is not null && pages < 10);

        Assert.Equal(["p5", "p4", "p3", "p2", "p1", "e1"], seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task Handle_ExactlyOnePageOfRows_HasNoNextCursor()
    {
        await SeedAsync("a", "user-a", Earlier);
        await SeedAsync("b", "user-a", Later);

        var page = (await RunAsync("user-a", limit: 2)).Get();

        Assert.Equal(2, page.Songs.Count);
        Assert.Null(page.NextBefore);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("_s1")]
    [InlineData("123_")]
    [InlineData("-5_s1")]
    [InlineData("99999999999999999999_s1")]
    public async Task Handle_MalformedCursor_IsInvalid(string before)
    {
        await SeedAsync("a", "user-a", Earlier);

        var result = await RunAsync("user-a", before);

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Invalid, ResultErrors.KindOf(result.GetError()));
    }
}
