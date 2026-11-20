using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.SongMerges;
using Shuffull.Core.Features.SongMerges.GetSongMerges;
using Shuffull.Core.Features.Songs.MergeSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.MergeSongs;

/// <summary>
/// A curator's merge of two copies of one song (Shuffull#142): everything users decided moves to the survivor in one
/// transaction, the loser leaves a tombstone and a merge row, and every moved row gets its own Version.
/// </summary>
public class MergeSongsHandlerTest : IDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(string? id = null, bool curator = false)
    {
        var user = new User
        {
            UserId = id ?? Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash",
            IsCurator = curator,
        };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<Song> SeedSongAsync(string id, string? externalSongId = null, bool exploratory = false, string? fileHash = null)
    {
        var song = new Song
        {
            SongId = id,
            Name = id,
            FileExtension = ".mp3",
            FileHash = fileHash ?? $"hash-{id}",
            ExternalSongId = externalSongId,
            Exploratory = exploratory,
            Version = T0,
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task OwnAsync(User user, Song song, LikeStatus likeStatus = LikeStatus.Neutral, DateTime? lastPlayed = null)
    {
        _database.Context.UserSongs.Add(new UserSong
        {
            UserId = user.UserId, SongId = song.SongId, LastPlayed = lastPlayed ?? DateTime.MinValue, Version = T0, LikeStatus = likeStatus,
        });
        await _database.Context.SaveChangesAsync();
    }

    private async Task<Playlist> SeedPlaylistAsync(User owner, string id, string? currentSongId = null, params Song[] songs)
    {
        var playlist = new Playlist
        {
            PlaylistId = id, UserId = owner.UserId, Name = id, PercentUntilReplayable = 0.5m, Version = T0, CurrentSongId = currentSongId,
        };
        _database.Context.Playlists.Add(playlist);
        foreach (var song in songs)
        {
            _database.Context.PlaylistSongs.Add(new PlaylistSong { PlaylistSongId = $"{id}-{song.SongId}", PlaylistId = id, SongId = song.SongId });
        }
        await _database.Context.SaveChangesAsync();
        return playlist;
    }

    private async Task<SongDuplicateReport> SeedReportAsync(User reporter, string songId, string otherSongId,
        SongDuplicateReportStatus status = SongDuplicateReportStatus.Pending)
    {
        var report = new SongDuplicateReport
        {
            ReportId = Guid.NewGuid().ToString(),
            ReporterUserId = reporter.UserId,
            SongId = songId,
            OtherSongId = otherSongId,
            Kind = SongDuplicateReportKind.Duplicate,
            HiddenSongId = SongDuplicateReport.HiddenSongOf(songId, otherSongId, null),
            PairKey = SongDuplicateReport.PairKeyOf(songId, otherSongId),
            Status = status,
            CreatedAt = T0,
            Version = T0,
        };
        _database.Context.SongDuplicateReports.Add(report);
        await _database.Context.SaveChangesAsync();
        return report;
    }

    private async Task<Result<MergeSongsResponse>> MergeAsync(string curatorId, string survivorId, string loserId)
    {
        await using var context = _database.CreateContext();
        return await new MergeSongsHandler(context).Handle(new MergeSongsCommand(curatorId, survivorId, loserId), CancellationToken.None);
    }

    private async Task<MergeSongsResponse> MergeOkAsync(string curatorId, string survivorId, string loserId)
    {
        var result = await MergeAsync(curatorId, survivorId, loserId);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    private async Task<UserSong?> UserSongAsync(string userId, string songId)
    {
        await using var context = _database.CreateContext();
        return await context.UserSongs.AsNoTracking().SingleOrDefaultAsync(us => us.UserId == userId && us.SongId == songId);
    }

    [Theory]
    [InlineData(LikeStatus.Like, LikeStatus.Neutral, LikeStatus.Like)]
    [InlineData(LikeStatus.Neutral, LikeStatus.Like, LikeStatus.Like)]
    [InlineData(LikeStatus.Dislike, LikeStatus.Love, LikeStatus.Love)]
    [InlineData(LikeStatus.Love, LikeStatus.Like, LikeStatus.Love)]
    [InlineData(LikeStatus.Dislike, LikeStatus.Neutral, LikeStatus.Dislike)]
    [InlineData(LikeStatus.Neutral, LikeStatus.Dislike, LikeStatus.Dislike)]
    [InlineData(LikeStatus.Dislike, LikeStatus.Like, LikeStatus.Like)]
    public async Task UserWithBothCopies_KeepsOneRow_WithTheStrongerSentiment(LikeStatus onSurvivor, LikeStatus onLoser, LikeStatus expected)
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        await OwnAsync(me, survivor, onSurvivor);
        await OwnAsync(me, loser, onLoser);

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        Assert.Equal(expected, (await UserSongAsync(me.UserId, survivor.SongId))!.LikeStatus);
        Assert.Null(await UserSongAsync(me.UserId, loser.SongId));
    }

    [Fact]
    public async Task UserWithOnlyTheLoser_GetsTheSurvivor_WithTheirSentimentAndPlayHistory()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var bystander = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        await OwnAsync(me, loser, LikeStatus.Dislike, lastPlayed: T0.AddDays(-1));
        await OwnAsync(bystander, survivor, LikeStatus.Like);

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        var mine = (await UserSongAsync(me.UserId, survivor.SongId))!;
        Assert.Equal(LikeStatus.Dislike, mine.LikeStatus);
        Assert.Equal(T0.AddDays(-1), mine.LastPlayed);
        Assert.True(mine.Version > T0);

        // A user who only had the survivor is left as they were.
        var theirs = (await UserSongAsync(bystander.UserId, survivor.SongId))!;
        Assert.Equal(LikeStatus.Like, theirs.LikeStatus);
        Assert.Equal(T0, theirs.Version);
    }

    [Fact]
    public async Task UserWithBothCopies_KeepsTheLaterLastPlayed()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        await OwnAsync(me, survivor, lastPlayed: T0.AddDays(-5));
        await OwnAsync(me, loser, lastPlayed: T0.AddDays(-1));

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        Assert.Equal(T0.AddDays(-1), (await UserSongAsync(me.UserId, survivor.SongId))!.LastPlayed);
    }

    [Fact]
    public async Task Playlists_HoldTheSurvivorOnce_AndTheCurrentSongFollows()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        var untouched = await SeedSongAsync("untouched");
        await SeedPlaylistAsync(me, "both", currentSongId: loser.SongId, survivor, loser);
        await SeedPlaylistAsync(other, "loser-only", null, loser, untouched);
        await SeedPlaylistAsync(other, "unrelated", null, untouched);

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        await using var read = _database.CreateContext();
        var entries = await read.PlaylistSongs.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(entries, ps => ps.SongId == loser.SongId);
        Assert.Single(entries, ps => ps.PlaylistId == "both");
        Assert.Contains(entries, ps => ps.PlaylistId == "loser-only" && ps.SongId == survivor.SongId);
        Assert.Contains(entries, ps => ps.PlaylistId == "loser-only" && ps.SongId == untouched.SongId);

        var playlists = await read.Playlists.AsNoTracking().ToDictionaryAsync(p => p.PlaylistId);
        Assert.Equal(survivor.SongId, playlists["both"].CurrentSongId);
        Assert.True(playlists["both"].Version > T0);
        Assert.True(playlists["loser-only"].Version > T0);
        Assert.Equal(T0, playlists["unrelated"].Version);

        // Both owners' syncs notice.
        var users = await read.Users.AsNoTracking().ToDictionaryAsync(u => u.UserId);
        Assert.True(users[me.UserId].Version > T0);
        Assert.True(users[other.UserId].Version > T0);
    }

    [Fact]
    public async Task History_MovesToTheSurvivor()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor", externalSongId: "yt-survivor");
        var loser = await SeedSongAsync("loser", externalSongId: "yt-loser");
        await OwnAsync(me, loser, LikeStatus.Like);
        _database.Context.Listens.Add(new Listen
        {
            ListenId = "listen-1", UserId = me.UserId, SongId = loser.SongId, StartedAt = T0, EndedAt = T0.AddMinutes(3),
            SecondsListened = 180, SongDurationSeconds = 200, RecordedAt = T0,
        });
        _database.Context.YoutubeRatingRequests.Add(new YoutubeRatingRequest
        {
            YoutubeRatingRequestId = "rating-1", SongId = loser.SongId, VideoId = "yt-loser", Rating = YoutubeRating.Like, CreatedAt = T0, UpdatedAt = T0,
        });
        var import = new SongImport
        {
            SongImportId = "import-1", Name = "loser", ImportFolder = "folder", FileType = ".mp3", UserId = me.UserId, SongId = loser.SongId,
            ExternalSongId = "yt-loser", LastUpdatedAt = T0,
        };
        _database.Context.SongImports.Add(import);
        _database.Context.SongReplacements.Add(new SongReplacement { SongReplacementId = "done", SongId = loser.SongId, Status = SongReplacementStatus.Completed, CreatedAt = T0 });
        _database.Context.SongReplacements.Add(new SongReplacement { SongReplacementId = "open", SongId = loser.SongId, OriginalExternalSongId = "yt-loser", CreatedAt = T0.AddMinutes(1) });
        await _database.Context.SaveChangesAsync();

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        await using var read = _database.CreateContext();
        Assert.Equal(survivor.SongId, (await read.Listens.AsNoTracking().SingleAsync()).SongId);
        var rating = await read.YoutubeRatingRequests.AsNoTracking().SingleAsync();
        Assert.Equal(survivor.SongId, rating.SongId);
        Assert.Equal("yt-loser", rating.VideoId);
        Assert.Equal(survivor.SongId, (await read.SongImports.AsNoTracking().SingleAsync()).SongId);
        var replacements = await read.SongReplacements.AsNoTracking().ToDictionaryAsync(r => r.SongReplacementId);
        Assert.Equal(survivor.SongId, replacements["done"].SongId);
        Assert.Equal(survivor.SongId, replacements["open"].SongId);
        Assert.Equal("yt-survivor", replacements["open"].OriginalExternalSongId);
    }

    [Fact]
    public async Task OpenReplacements_TheSurvivorKeepsItsOwn()
    {
        var curator = await SeedUserAsync(curator: true);
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        _database.Context.SongReplacements.Add(new SongReplacement { SongReplacementId = "survivor-open", SongId = survivor.SongId, CreatedAt = T0 });
        _database.Context.SongReplacements.Add(new SongReplacement { SongReplacementId = "loser-open", SongId = loser.SongId, Status = SongReplacementStatus.InProgress, CreatedAt = T0 });
        await _database.Context.SaveChangesAsync();

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        await using var read = _database.CreateContext();
        var replacement = await read.SongReplacements.AsNoTracking().SingleAsync();
        Assert.Equal("survivor-open", replacement.SongReplacementId);
    }

    [Theory]
    // survivorExploratory, loserExploratory, loser liked, expected
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public async Task Survivor_StopsBeingAnAudition_WhenEitherSideWasKeptOrLiked(bool survivorExploratory, bool loserExploratory, bool loserLiked, bool expected)
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor", exploratory: survivorExploratory);
        var loser = await SeedSongAsync("loser", exploratory: loserExploratory);
        await OwnAsync(me, loser, loserLiked ? LikeStatus.Like : LikeStatus.Neutral);

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        await using var read = _database.CreateContext();
        var stored = await read.Songs.AsNoTracking().SingleAsync(s => s.SongId == survivor.SongId);
        Assert.Equal(expected, stored.Exploratory);
        Assert.True(stored.Version > T0);
    }

    [Fact]
    public async Task Loser_IsDeleted_WithItsMetadata_AndLeavesATombstoneAndAMergeRow()
    {
        var curator = await SeedUserAsync(curator: true);
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser", externalSongId: "yt-loser", fileHash: "loser-hash");
        var artist = new Artist { ArtistId = "artist-1", Name = "Billy" };
        _database.Context.Artists.Add(artist);
        _database.Context.SongArtists.Add(new SongArtist { SongArtistId = "sa-loser", SongId = loser.SongId, ArtistId = artist.ArtistId });
        _database.Context.SongArtists.Add(new SongArtist { SongArtistId = "sa-survivor", SongId = survivor.SongId, ArtistId = artist.ArtistId });
        var tag = new Mood { TagId = "tag-1", Name = "Chill" };
        _database.Context.Tags.Add(tag);
        _database.Context.SongTags.Add(new SongTag { SongTagId = "st-loser", SongId = loser.SongId, TagId = tag.TagId });
        await _database.Context.SaveChangesAsync();

        var response = await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        Assert.False(response.AlreadyMerged);
        Assert.Equal(loser.SongId, response.Merge.LoserSongId);
        Assert.Equal(survivor.SongId, response.Merge.SurvivorSongId);

        await using var read = _database.CreateContext();
        Assert.False(await read.Songs.AnyAsync(s => s.SongId == loser.SongId));
        Assert.Equal("sa-survivor", (await read.SongArtists.AsNoTracking().SingleAsync()).SongArtistId);
        Assert.False(await read.SongTags.AnyAsync());
        Assert.True(await read.Artists.AnyAsync(a => a.ArtistId == artist.ArtistId));

        var tombstone = await read.SongTombstones.AsNoTracking().SingleAsync();
        Assert.Equal(loser.SongId, tombstone.SongId);
        Assert.Equal("loser-hash", tombstone.FileHash);
        Assert.Equal(curator.UserId, tombstone.DeletedByUserId);
        Assert.Null(tombstone.PlaylistId);
        Assert.False(tombstone.MediaDeleted);

        var merge = await read.SongMerges.AsNoTracking().SingleAsync();
        Assert.Equal(survivor.SongId, merge.SurvivorSongId);
        Assert.Equal("yt-loser", merge.LoserExternalSongId);
        Assert.Equal("loser-hash", merge.LoserFileHash);
        Assert.Equal(curator.UserId, merge.MergedByUserId);
    }

    [Fact]
    public async Task OpenReportsOnThePair_CloseAsMerged_AndNothingElseChanges()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var you = await SeedUserAsync();
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        var third = await SeedSongAsync("third");
        var mine = await SeedReportAsync(me, loser.SongId, survivor.SongId);
        var yours = await SeedReportAsync(you, survivor.SongId, loser.SongId);
        var withdrawn = await SeedReportAsync(curator, survivor.SongId, loser.SongId, SongDuplicateReportStatus.Withdrawn);
        var otherPair = await SeedReportAsync(me, loser.SongId, third.SongId);

        var response = await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        Assert.Equal(new[] { mine.ReportId, yours.ReportId }.Order(), response.Reports.Select(r => r.ReportId).Order());
        await using var read = _database.CreateContext();
        var reports = await read.SongDuplicateReports.AsNoTracking().ToDictionaryAsync(r => r.ReportId);
        Assert.Equal(SongDuplicateReportStatus.Merged, reports[mine.ReportId].Status);
        Assert.Equal(curator.UserId, reports[mine.ReportId].ResolvedByUserId);
        Assert.NotNull(reports[mine.ReportId].ResolvedAt);
        Assert.NotEqual(reports[mine.ReportId].Version, reports[yours.ReportId].Version);
        Assert.Equal(SongDuplicateReportStatus.Merged, reports[yours.ReportId].Status);
        Assert.Equal(SongDuplicateReportStatus.Withdrawn, reports[withdrawn.ReportId].Status);
        Assert.Equal(SongDuplicateReportStatus.Pending, reports[otherPair.ReportId].Status);
        Assert.Equal(T0, reports[otherPair.ReportId].Version);
    }

    [Fact]
    public async Task EveryTouchedRow_GetsItsOwnMillisecond()
    {
        var curator = await SeedUserAsync(curator: true);
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        for (var i = 0; i < 30; i++)
        {
            var user = await SeedUserAsync($"user-{i:D2}");
            await OwnAsync(user, loser, LikeStatus.Like);
            if (i % 2 == 0)
            {
                await OwnAsync(user, survivor);
                await SeedPlaylistAsync(user, $"playlist-{i:D2}", null, survivor, loser);
            }
        }

        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        await using var read = _database.CreateContext();
        var versions = new List<DateTime>();
        versions.AddRange(await read.UserSongs.Where(us => us.Version > T0).Select(us => us.Version).ToListAsync());
        versions.AddRange(await read.Songs.Where(s => s.Version > T0).Select(s => s.Version).ToListAsync());
        versions.AddRange(await read.Playlists.Where(p => p.Version > T0).Select(p => p.Version).ToListAsync());
        versions.AddRange(await read.Users.Where(u => u.Version > T0).Select(u => u.Version).ToListAsync());
        versions.AddRange(await read.SongMerges.Select(m => m.Version).ToListAsync());

        Assert.Equal(30 + 1 + 15 + 30 + 1, versions.Count);
        var maxPerMs = versions.GroupBy(v => v.Ticks / TimeSpan.TicksPerMillisecond).Max(g => g.Count());
        Assert.Equal(1, maxPerMs);
    }

    [Fact]
    public async Task Resend_IsIdempotent()
    {
        var curator = await SeedUserAsync(curator: true);
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        var first = await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        var again = await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        Assert.True(again.AlreadyMerged);
        Assert.Empty(again.Reports);
        Assert.Equal(first.Merge, again.Merge);
        await using var read = _database.CreateContext();
        Assert.Equal(1, await read.SongTombstones.CountAsync());
    }

    [Fact]
    public async Task LoserMergedElsewhere_IsAConflict()
    {
        var curator = await SeedUserAsync(curator: true);
        var survivor = await SeedSongAsync("survivor");
        var loser = await SeedSongAsync("loser");
        var other = await SeedSongAsync("other");
        await MergeOkAsync(curator.UserId, survivor.SongId, loser.SongId);

        var result = await MergeAsync(curator.UserId, other.SongId, loser.SongId);

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Conflict, ResultErrors.KindOf(result.GetError()));
        Assert.Contains("already merged into 'survivor'", result.GetError().Message);
    }

    [Fact]
    public async Task SurvivorThatWasMergedAway_IsAConflictNamingItsSurvivor()
    {
        var curator = await SeedUserAsync(curator: true);
        var a = await SeedSongAsync("a");
        var b = await SeedSongAsync("b");
        var c = await SeedSongAsync("c");
        await MergeOkAsync(curator.UserId, a.SongId, b.SongId);

        var result = await MergeAsync(curator.UserId, b.SongId, c.SongId);

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Conflict, ResultErrors.KindOf(result.GetError()));
        Assert.Contains("merged into 'a'", result.GetError().Message);
        await using var read = _database.CreateContext();
        Assert.True(await read.Songs.AnyAsync(s => s.SongId == c.SongId));
    }

    [Theory]
    [InlineData("missing", "survivor")]
    [InlineData("survivor", "missing")]
    public async Task MissingSong_IsNotFound(string survivorId, string loserId)
    {
        var curator = await SeedUserAsync(curator: true);
        await SeedSongAsync("survivor");

        var result = await MergeAsync(curator.UserId, survivorId, loserId);

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(result.GetError()));
    }

    [Fact]
    public async Task SameSongTwice_IsInvalid()
    {
        var curator = await SeedUserAsync(curator: true);
        await SeedSongAsync("survivor");

        var result = await MergeAsync(curator.UserId, "survivor", "survivor");

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Invalid, ResultErrors.KindOf(result.GetError()));
    }

    [Theory]
    [InlineData(LikeStatus.Love, LikeStatus.Neutral, LikeStatus.Love)]
    [InlineData(LikeStatus.Neutral, LikeStatus.Dislike, LikeStatus.Dislike)]
    [InlineData(LikeStatus.Like, LikeStatus.Like, LikeStatus.Like)]
    public void Stronger_RanksLoveLikeDislikeNeutral(LikeStatus a, LikeStatus b, LikeStatus expected)
    {
        Assert.Equal(expected, MergeSongsHandler.Stronger(a, b));
        Assert.Equal(expected, MergeSongsHandler.Stronger(b, a));
    }

    // ---- The feed devices page, and the alias lookups the importer uses ----

    private async Task<GetSongMergesResponse> FeedAsync(DateTime afterDate, string? afterId = null)
    {
        await using var context = _database.CreateContext();
        var result = await new GetSongMergesHandler(context).Handle(new GetSongMergesQuery(afterDate, afterId), CancellationToken.None);
        Assert.True(result.IsOk);
        return result.Get();
    }

    private async Task SeedMergeRowsAsync(int count, DateTime version)
    {
        for (var i = 0; i < count; i++)
        {
            _database.Context.SongMerges.Add(new SongMerge
            {
                LoserSongId = $"loser-{i:D4}", SurvivorSongId = "survivor", LoserFileHash = $"h{i}", MergedByUserId = "curator",
                MergedAt = version, Version = version,
            });
        }
        await _database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Feed_ReturnsAMergeAfterTheCursor()
    {
        var curator = await SeedUserAsync(curator: true);
        await SeedSongAsync("survivor");
        await SeedSongAsync("loser");
        await MergeOkAsync(curator.UserId, "survivor", "loser");

        var page = await FeedAsync(DateTime.MinValue);

        var merge = Assert.Single(page.Merges);
        Assert.Equal("loser", merge.LoserSongId);
        Assert.True(page.EndOfList);
        Assert.Empty((await FeedAsync(merge.Version, merge.LoserSongId)).Merges);
    }

    [Fact]
    public async Task Feed_PagesPastATieOn501Rows()
    {
        await SeedMergeRowsAsync(501, T0);

        var first = await FeedAsync(DateTime.MinValue);
        Assert.Equal(500, first.Merges.Count);
        Assert.False(first.EndOfList);

        var last = first.Merges[^1];
        var second = await FeedAsync(last.Version, last.LoserSongId);
        Assert.Equal("loser-0500", Assert.Single(second.Merges).LoserSongId);
        Assert.True(second.EndOfList);
    }

    [Fact]
    public async Task Aliases_FollowAChainToTheLiveSong()
    {
        var curator = await SeedUserAsync(curator: true);
        await SeedSongAsync("a", externalSongId: "yt-a", fileHash: "hash-a");
        await SeedSongAsync("b", externalSongId: "yt-b", fileHash: "hash-b");
        await SeedSongAsync("c", fileHash: "hash-c");
        await MergeOkAsync(curator.UserId, "b", "a");
        await MergeOkAsync(curator.UserId, "c", "b");

        await using var context = _database.CreateContext();
        Assert.Equal("c", (await SongMergeAliases.FindLiveSongAsync(context, "a", CancellationToken.None))?.SongId);
        Assert.Equal("c", (await SongMergeAliases.FindLiveSongAsync(context, "c", CancellationToken.None))?.SongId);
        Assert.Null(await SongMergeAliases.FindLiveSongAsync(context, "nope", CancellationToken.None));
        Assert.Equal("c", (await SongMergeAliases.FindSurvivorOfSourceAsync(context, "yt-a", "unrelated-hash", CancellationToken.None))?.SongId);
        Assert.Equal("c", (await SongMergeAliases.FindSurvivorOfSourceAsync(context, null, "hash-b", CancellationToken.None))?.SongId);
        Assert.Null(await SongMergeAliases.FindSurvivorOfSourceAsync(context, "yt-zzz", "hash-zzz", CancellationToken.None));
    }
}
