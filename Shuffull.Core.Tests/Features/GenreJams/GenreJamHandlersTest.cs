using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.GenreJams.DeleteGenreJam;
using Shuffull.Core.Features.GenreJams.GetGenreJams;
using Shuffull.Core.Features.GenreJams.SaveGenreJam;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.GenreJams;

/// <summary>
/// Saved Genre Jams against a real database: a save creates or replaces the caller's jam under the app's id and leaves
/// another user's alone, a delete is safe to resend, and the list holds only the caller's jams.
/// </summary>
public class GenreJamHandlersTest : IDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private static SaveGenreJamCommand Save(string userId, string id, string name = "Loud", GenreJamFilters? whitelists = null,
        GenreJamFilters? blacklists = null, int? min = null, int? max = null)
        => new(userId, id, name, whitelists, blacklists, min, max);

    private async Task<Result<SaveGenreJamResponse>> RunAsync(SaveGenreJamCommand command)
    {
        await using var context = _database.CreateContext();
        return await new SaveGenreJamHandler(context).Handle(command, CancellationToken.None);
    }

    private async Task<Result<DeleteGenreJamResponse>> RunAsync(DeleteGenreJamCommand command)
    {
        await using var context = _database.CreateContext();
        return await new DeleteGenreJamHandler(context).Handle(command, CancellationToken.None);
    }

    private async Task<IReadOnlyList<GenreJamDto>> ListAsync(string userId)
    {
        await using var context = _database.CreateContext();
        var result = await new GetGenreJamsHandler(context).Handle(new GetGenreJamsQuery(userId), CancellationToken.None);
        Assert.True(result.IsOk);
        return result.Get().GenreJams;
    }

    [Fact]
    public async Task Save_NewId_CreatesTheJamWithItsFilters()
    {
        var me = await SeedUserAsync();
        var whitelists = new GenreJamFilters(GenreIds: ["rock", "metal"], ArtistIds: ["artist-1"]);
        var blacklists = new GenreJamFilters(MoodIds: ["calm"]);

        var result = await RunAsync(Save(me.UserId, "jam-1", "  Loud  ", whitelists, blacklists, 6, 10));

        Assert.True(result.IsOk);
        var saved = Assert.Single(await ListAsync(me.UserId));
        Assert.Equal("jam-1", saved.GenreJamId);
        Assert.Equal("Loud", saved.Name);
        Assert.Equal(["rock", "metal"], saved.Whitelists.GenreIds!);
        Assert.Equal(["artist-1"], saved.Whitelists.ArtistIds!);
        Assert.Empty(saved.Whitelists.PlaylistIds!);
        Assert.Equal(["calm"], saved.Blacklists.MoodIds!);
        Assert.Equal(6, saved.HeavinessMin);
        Assert.Equal(10, saved.HeavinessMax);
        Assert.Equal(("jam-1", "Loud"), (result.Get().GenreJam.GenreJamId, result.Get().GenreJam.Name));
    }

    [Fact]
    public async Task Save_ExistingId_ReplacesTheWholeJam()
    {
        var me = await SeedUserAsync();
        Assert.True((await RunAsync(Save(me.UserId, "jam-1", "Loud", new GenreJamFilters(GenreIds: ["rock"]), null, 6, 10))).IsOk);

        var result = await RunAsync(Save(me.UserId, "jam-1", "Quiet", null, new GenreJamFilters(GenreIds: ["rock"])));

        Assert.True(result.IsOk);
        var saved = Assert.Single(await ListAsync(me.UserId));
        Assert.Equal("Quiet", saved.Name);
        Assert.Empty(saved.Whitelists.GenreIds!);
        Assert.Equal(["rock"], saved.Blacklists.GenreIds!);
        Assert.Null(saved.HeavinessMin);
        Assert.Null(saved.HeavinessMax);
    }

    [Fact]
    public async Task Save_TheSameJamTwice_LeavesOneRow()
    {
        var me = await SeedUserAsync();
        var command = Save(me.UserId, "jam-1", "Loud", new GenreJamFilters(GenreIds: ["rock"]));

        Assert.True((await RunAsync(command)).IsOk);
        Assert.True((await RunAsync(command)).IsOk);

        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.GenreJams.CountAsync());
    }

    [Fact]
    public async Task Save_AnotherUsersId_IsAConflictAndLeavesTheirJamAlone()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        Assert.True((await RunAsync(Save(other.UserId, "jam-1", "Theirs"))).IsOk);

        var result = await RunAsync(Save(me.UserId, "jam-1", "Mine"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Conflict, ResultErrors.KindOf(result.GetError()));
        Assert.Empty(await ListAsync(me.UserId));
        Assert.Equal("Theirs", Assert.Single(await ListAsync(other.UserId)).Name);
    }

    [Fact]
    public async Task Delete_TheCallersJam_RemovesIt()
    {
        var me = await SeedUserAsync();
        Assert.True((await RunAsync(Save(me.UserId, "jam-1"))).IsOk);

        var result = await RunAsync(new DeleteGenreJamCommand(me.UserId, "jam-1"));

        Assert.True(result.IsOk);
        Assert.True(result.Get().Deleted);
        Assert.Empty(await ListAsync(me.UserId));
    }

    [Fact]
    public async Task Delete_AMissingJam_SucceedsWithNothingDeleted()
    {
        var me = await SeedUserAsync();

        var result = await RunAsync(new DeleteGenreJamCommand(me.UserId, "never-synced"));

        Assert.True(result.IsOk);
        Assert.False(result.Get().Deleted);
    }

    [Fact]
    public async Task Delete_AnotherUsersJam_LeavesIt()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        Assert.True((await RunAsync(Save(other.UserId, "jam-1", "Theirs"))).IsOk);

        var result = await RunAsync(new DeleteGenreJamCommand(me.UserId, "jam-1"));

        Assert.True(result.IsOk);
        Assert.False(result.Get().Deleted);
        Assert.Single(await ListAsync(other.UserId));
    }

    [Fact]
    public async Task List_ReturnsOnlyTheCallersJams_ByName()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        Assert.True((await RunAsync(Save(me.UserId, "jam-b", "Zen"))).IsOk);
        Assert.True((await RunAsync(Save(me.UserId, "jam-a", "Angry"))).IsOk);
        Assert.True((await RunAsync(Save(other.UserId, "jam-c", "Theirs"))).IsOk);

        var jams = await ListAsync(me.UserId);

        Assert.Equal(["Angry", "Zen"], jams.Select(j => j.Name));
    }
}
