using Nut.Results;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Models.Database;
using Microsoft.Extensions.Configuration;
using Shuffull.Core.Tools;
using Shuffull.Metadata.Tools;
using Shuffull.Core.Persistence.Specifications.Songs;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.GetSongsChanged;

public class GetSongsChangedHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetSongsChangedHandler _handler;

    public GetSongsChangedHandlerTest()
    {
        _database = new DatabaseFixture();
        // An empty strengths map => the judge's fail-safe kicks in and nothing is ever stale, matching the
        // pre-TagsStale behavior these tests assert.
        _handler = new GetSongsChangedHandler(_database.UnitOfWork, new TagStalenessJudge(new ModelStrengths(), new ConfigurationBuilder().Build()));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Song NewSong(string id, DateTime version, string name = "Song")
        => new()
        {
            SongId = id,
            Name = name,
            FileExtension = ".mp3",
            FileHash = $"hash-{id}",
            ExternalSongId = null,
            Version = version,
        };

    [Fact]
    public async Task Handle_ReturnsOnlySongsChangedAfterCursorOrderedByVersion()
    {
        // Arrange
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _database.Context.Songs.AddRangeAsync(
            NewSong("old", cursor.AddDays(-1)),
            NewSong("at-cursor", cursor),
            NewSong("c", cursor.AddDays(3)),
            NewSong("a", cursor.AddDays(1)),
            NewSong("b", cursor.AddDays(2)));
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongsChangedQuery(cursor), CancellationToken.None);

        // Assert — strictly-after the cursor, ascending by version (so the client can keep paging by version).
        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.Equal(new[] { "a", "b", "c" }, response.Songs.Select(s => s.SongId).ToArray());
        Assert.True(response.EndOfList);
    }

    [Fact]
    public async Task Handle_FirstSyncSentinelReturnsAllSongs()
    {
        // Arrange — DateTime.MinValue is the "never synced" cursor; everything is newer.
        await _database.Context.Songs.AddRangeAsync(
            NewSong("x", new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            NewSong("y", new DateTime(2021, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongsChangedQuery(DateTime.MinValue), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(2, result.Get().Songs.Count);
        Assert.True(result.Get().EndOfList);
    }

    [Fact]
    public async Task Handle_WithNoChangedSongs_ReturnsEmptyEndOfList()
    {
        // Arrange
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _database.Context.Songs.AddAsync(NewSong("old", cursor.AddDays(-5)));
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongsChangedQuery(cursor), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Empty(result.Get().Songs);
        Assert.True(result.Get().EndOfList);
    }

    [Fact]
    public async Task Handle_MapsArtistsAndTagsAndEnrichmentOntoDto()
    {
        // Arrange
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var song = NewSong("enriched", cursor.AddDays(1));
        song.Bpm = 128;
        song.Heaviness = 7;
        song.SyncedLyrics = "[00:00.00] hi";
        await _database.Context.Songs.AddAsync(song);

        var artist = new Artist { ArtistId = "artist-1", Name = "The Band" };
        var tag = new Genre { TagId = "tag-1", Name = "Rock" };
        await _database.Context.Artists.AddAsync(artist);
        await _database.Context.Tags.AddAsync(tag);
        await _database.Context.SongArtists.AddAsync(new SongArtist
        {
            SongArtistId = "sa-1",
            SongId = song.SongId,
            ArtistId = artist.ArtistId,
        });
        await _database.Context.SongTags.AddAsync(new SongTag
        {
            SongTagId = "st-1",
            SongId = song.SongId,
            TagId = tag.TagId,
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongsChangedQuery(cursor), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = Assert.Single(result.Get().Songs);
        Assert.Equal(new[] { "The Band" }, dto.Artists.ToArray());
        Assert.Equal(new[] { "Rock" }, dto.Tags.ToArray());
        Assert.Equal(128, dto.Bpm);
        Assert.Equal(7, dto.Heaviness);
        Assert.Equal("[00:00.00] hi", dto.SyncedLyrics);
    }

    [Fact]
    public async Task Handle_WhenMoreThanPageSizeChanged_ReportsNotEndOfListAndCapsPage()
    {
        // Arrange — one row beyond a full page; the spec fetches PageSize+1 to detect the extra page.
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var songs = Enumerable.Range(0, SongsByVersionAfterDateSpec.PageSize + 1)
            .Select(i => NewSong($"song-{i:0000}", cursor.AddSeconds(i + 1)));
        await _database.Context.Songs.AddRangeAsync(songs);
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongsChangedQuery(cursor), CancellationToken.None);

        // Assert — page is capped at PageSize and the trailing detection row is dropped.
        Assert.True(result.IsOk);
        Assert.Equal(SongsByVersionAfterDateSpec.PageSize, result.Get().Songs.Count);
        Assert.False(result.Get().EndOfList);
    }

    [Fact]
    public async Task Handle_WhenExactlyPageSizeChanged_ReportsEndOfList()
    {
        // Arrange — exactly a full page means no extra row, so this is the final page.
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var songs = Enumerable.Range(0, SongsByVersionAfterDateSpec.PageSize)
            .Select(i => NewSong($"song-{i:0000}", cursor.AddSeconds(i + 1)));
        await _database.Context.Songs.AddRangeAsync(songs);
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongsChangedQuery(cursor), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(SongsByVersionAfterDateSpec.PageSize, result.Get().Songs.Count);
        Assert.True(result.Get().EndOfList);
    }

    [Fact]
    public async Task Handle_KeysetCursor_PagesThroughATieLargerThanAPage_EachSongOnce()
    {
        // Arrange — 1,200 songs stamped with one Version, as a bulk SQL write does (shuffull-workspace#38). Ids are
        // inserted shuffled so the order has to come from the query, not from insertion.
        var version = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
        var ids = Enumerable.Range(0, 1200).Select(i => $"song-{i:0000}").ToList();
        await _database.Context.Songs.AddRangeAsync(Enumerable.Range(0, ids.Count).Select(i => NewSong(ids[i * 7 % ids.Count], version)));
        await _database.Context.SaveChangesAsync();

        // Act — page the way a keyset client does: from the last song's exact Version and SongId.
        var pages = new List<GetSongsChangedResponse>();
        var cursor = new GetSongsChangedQuery(DateTime.MinValue);
        while (pages.Count < 10)
        {
            var result = await _handler.Handle(cursor, CancellationToken.None);
            Assert.True(result.IsOk);
            var page = result.Get();
            pages.Add(page);
            if (page.EndOfList)
            {
                break;
            }

            cursor = new GetSongsChangedQuery(page.Songs[^1].Version, page.Songs[^1].SongId);
        }

        // Assert
        Assert.Equal(new[] { 500, 500, 200 }, pages.Select(p => p.Songs.Count).ToArray());
        Assert.Equal(new[] { false, false, true }, pages.Select(p => p.EndOfList).ToArray());
        Assert.Equal(ids, pages.SelectMany(p => p.Songs).Select(s => s.SongId).ToList());
    }

    [Fact]
    public async Task Handle_KeysetCursor_SkipsTiedSongsUpToTheIdAndKeepsLaterVersions()
    {
        // Arrange
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _database.Context.Songs.AddRangeAsync(
            NewSong("a", cursor),
            NewSong("b", cursor),
            NewSong("c", cursor),
            NewSong("0-later", cursor.AddTicks(10)),
            NewSong("z-earlier", cursor.AddTicks(-10)));
        await _database.Context.SaveChangesAsync();

        // Act — the client already has everything up to and including (cursor, "b").
        var result = await _handler.Handle(new GetSongsChangedQuery(cursor, "b"), CancellationToken.None);

        // Assert — the rest of the tie by id, then later versions regardless of id.
        Assert.True(result.IsOk);
        Assert.Equal(new[] { "c", "0-later" }, result.Get().Songs.Select(s => s.SongId).ToArray());
        Assert.True(result.Get().EndOfList);
    }

    [Fact]
    public async Task Handle_WithoutAfterId_KeepsTheLegacyStrictlyAfterCursor()
    {
        // Arrange — installed apps send afterDate alone; a song at exactly that Version stays excluded whatever its id.
        var cursor = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _database.Context.Songs.AddRangeAsync(NewSong("zzz", cursor), NewSong("next", cursor.AddTicks(10)));
        await _database.Context.SaveChangesAsync();

        // Act
        var withoutId = await _handler.Handle(new GetSongsChangedQuery(cursor), CancellationToken.None);
        var emptyId = await _handler.Handle(new GetSongsChangedQuery(cursor, ""), CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "next" }, withoutId.Get().Songs.Select(s => s.SongId).ToArray());
        Assert.Equal(new[] { "next" }, emptyId.Get().Songs.Select(s => s.SongId).ToArray());
    }
}
