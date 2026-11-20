using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.GenreJams.GetGenreJams;
using Shuffull.Core.Features.GenreJams.SaveGenreJam;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.GenreJams;

/// <summary>
/// Saved Genre Jams on real Postgres, built by the real migrations (so this also proves AddGenreJams applies): the
/// filters round-trip through their JSON columns, and another user's id is still refused. Skipped unless
/// SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class GenreJamHandlersPostgresTest : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private PostgresDatabase? _database;

    private PostgresDatabase Database => _database ?? throw new InvalidOperationException("Postgres is not configured.");

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.BaseConnectionString is { Length: > 0 } baseConnectionString)
        {
            _database = await PostgresDatabase.CreateAsync(baseConnectionString);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private async Task<User> SeedUserAsync()
    {
        await using var context = Database.CreateContext();
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [PostgresFact]
    public async Task Save_ThenReplace_RoundTripsTheFiltersAndRefusesAnotherUser()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        var whitelists = new GenreJamFilters(GenreIds: ["tag-rock"], ArtistIds: ["a1b2c3"], ThemeIds: ["tag-rebellion"]);

        await using (var context = Database.CreateContext())
        {
            var created = await new SaveGenreJamHandler(context).Handle(
                new SaveGenreJamCommand(me.UserId, "jam-1", "Loud", whitelists, null, 6, 10), CancellationToken.None);
            Assert.True(created.IsOk);
        }
        await using (var context = Database.CreateContext())
        {
            var replaced = await new SaveGenreJamHandler(context).Handle(
                new SaveGenreJamCommand(me.UserId, "jam-1", "Louder", whitelists with { MoodIds = ["tag-energetic"] }, null, 8, 10),
                CancellationToken.None);
            Assert.True(replaced.IsOk);
        }
        await using (var context = Database.CreateContext())
        {
            var refused = await new SaveGenreJamHandler(context).Handle(
                new SaveGenreJamCommand(other.UserId, "jam-1", "Theirs", null, null, null, null), CancellationToken.None);
            Assert.Equal(ErrorKind.Conflict, ResultErrors.KindOf(refused.GetError()));
        }

        await using var check = Database.CreateContext();
        var listed = await new GetGenreJamsHandler(check).Handle(new GetGenreJamsQuery(me.UserId), CancellationToken.None);
        var jam = Assert.Single(listed.Get().GenreJams);
        Assert.Equal("Louder", jam.Name);
        Assert.Equal(["tag-rock"], jam.Whitelists.GenreIds!);
        Assert.Equal(["tag-energetic"], jam.Whitelists.MoodIds!);
        Assert.Equal(["tag-rebellion"], jam.Whitelists.ThemeIds!);
        Assert.Empty(jam.Blacklists.GenreIds!);
        Assert.Equal(8, jam.HeavinessMin);
        Assert.Equal(1, await check.GenreJams.CountAsync());
    }
}
