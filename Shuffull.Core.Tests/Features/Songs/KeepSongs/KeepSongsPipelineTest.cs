using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Authentication;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Features.Songs.KeepSongs;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.KeepSongs;

/// <summary>
/// Shuffull#37 end to end through the real MediatR pipeline (validation + role authorization, wired as in
/// Program.cs), as a NON-curator, with site AI off. Before the Keep endpoint the only way to promote was
/// RetagSongs, which the role gate rejects before promotion runs — so the Keep left no trace and deleting the
/// audition mix purged the song. Now the Keep is recorded on its own and the song survives the purge.
/// </summary>
public class KeepSongsPipelineTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly ServiceProvider _provider;
    private readonly User _listener;

    private sealed class FakeCurrentUser(User user) : ICurrentUser
    {
        public User? User { get; } = user;
    }

    /// <summary>Site AI disabled, as in prod: every enrichment fails the way the real service does.</summary>
    private sealed class DisabledEnrichment : ISongEnrichmentService
    {
        public int Calls { get; private set; }
        public Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, EnrichmentModel model = EnrichmentModel.Strong, CancellationToken cancellationToken = default!)
        {
            Calls++;
            return Task.FromResult(Result.Error<SongEnrichmentStatus>("AI is not enabled"));
        }
    }

    private sealed class NoOpMediaStore : ISongMediaStore
    {
        public Task<Result> DeleteSongMediaAsync(string fileHash, string fileExtension, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Ok());
    }

    public KeepSongsPipelineTest()
    {
        _database = new DatabaseFixture();
        _listener = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "listener",
            Version = DateTime.UtcNow,
            ServerHash = "server-hash",
            IsCurator = false,
        };
        _database.Context.Users.Add(_listener);
        _database.Context.SaveChanges();

        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _database.CreateContext());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ShuffullContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser(_listener));
        services.AddSingleton<IAuditionPromotionService, AuditionPromotionService>();
        services.AddSingleton<ISongEnrichmentService, DisabledEnrichment>();
        services.AddSingleton<ISongMediaStore, NoOpMediaStore>();
        services.AddValidatorsFromAssemblies([coreAssembly]);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(RoleAuthorizationBehavior<,>));
        });
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    /// <summary>An audition mix holding one exploratory song in the listener's library, as an import leaves it.</summary>
    private async Task<(Playlist Mix, Song Song)> SeedAuditionAsync()
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Audition",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            Exploratory = true,
            Version = DateTime.UtcNow,
        };
        var mix = new Playlist
        {
            PlaylistId = Guid.NewGuid().ToString(),
            UserId = _listener.UserId,
            Name = "Audition mix",
            Version = DateTime.UtcNow,
            IsExploratory = true,
        };
        _database.Context.Songs.Add(song);
        _database.Context.Playlists.Add(mix);
        _database.Context.PlaylistSongs.Add(new PlaylistSong
        {
            PlaylistSongId = Guid.NewGuid().ToString(), PlaylistId = mix.PlaylistId, SongId = song.SongId,
        });
        _database.Context.UserSongs.Add(new UserSong
        {
            UserId = _listener.UserId, SongId = song.SongId, LastPlayed = DateTime.MinValue, Version = DateTime.UtcNow,
        });
        await _database.Context.SaveChangesAsync();
        return (mix, song);
    }

    private async Task<Song?> FindSongAsync(string songId)
    {
        await using var context = _database.CreateContext();
        return await context.Songs.AsNoTracking().SingleOrDefaultAsync(s => s.SongId == songId);
    }

    [Fact]
    public async Task NonCuratorKeep_WithAiDisabled_ClearsExploratory()
    {
        var (_, song) = await SeedAuditionAsync();

        var result = await SendAsync(new KeepSongsCommand(_listener.UserId, [song.SongId]));

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(KeepOutcomes.Kept, Assert.Single(result.Get().Results).Outcome);
        Assert.False((await FindSongAsync(song.SongId))!.Exploratory);
        Assert.Equal(0, ((DisabledEnrichment)_provider.GetRequiredService<ISongEnrichmentService>()).Calls);
    }

    [Fact]
    public async Task NonCuratorKeep_SurvivesDeletingTheAuditionMix()
    {
        // The 2026-08-22 sequence, through the non-curator door: Keep, then delete the expired mix.
        var (mix, song) = await SeedAuditionAsync();

        await SendAsync(new KeepSongsCommand(_listener.UserId, [song.SongId]));
        var deleted = await SendAsync(new DeletePlaylistCommand(_listener.UserId, mix.PlaylistId));

        Assert.True(deleted.IsOk);
        Assert.Empty(deleted.Get().PurgedSongIds);
        Assert.NotNull(await FindSongAsync(song.SongId));
    }

    [Fact]
    public async Task Retag_StaysCuratorOnly_AndNeverWasAPathForANonCuratorsKeep()
    {
        // The old path. Pins down both that re-tag (AI spend) is still gated, and why Keep needed its own door:
        // the gate rejects the batch before RetagSongsHandler can promote, and the purge then takes the song.
        var (mix, song) = await SeedAuditionAsync();

        var retag = await SendAsync(new RetagSongsCommand([new SongRetagItem(song.SongId, RetagModels.Weak)]));
        Assert.True(retag.IsError);
        Assert.StartsWith("Forbidden", retag.GetError().Message);
        Assert.True((await FindSongAsync(song.SongId))!.Exploratory);

        var deleted = await SendAsync(new DeletePlaylistCommand(_listener.UserId, mix.PlaylistId));
        Assert.Equal([song.SongId], deleted.Get().PurgedSongIds);
    }
}
