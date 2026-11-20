using FluentValidation;
using Nut.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Core.Authentication;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;
using Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;
using Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.SongDuplicateReports;

/// <summary>
/// The duplicate-report commands through the real MediatR pipeline (validation + role authorization, wired as in
/// Program.cs): anyone can report, but only a curator can read the queue or resolve a pair.
/// </summary>
public class SongDuplicateReportsPipelineTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly User _listener;
    private readonly User _curator;
    private readonly Song _a;
    private readonly Song _b;

    private sealed class FakeCurrentUser(User user) : ICurrentUser
    {
        public User? User { get; } = user;
    }

    public SongDuplicateReportsPipelineTest()
    {
        _listener = NewUser("listener", curator: false);
        _curator = NewUser("curator", curator: true);
        _a = NewSong("A");
        _b = NewSong("B");
        _database.Context.Users.AddRange(_listener, _curator);
        _database.Context.Songs.AddRange(_a, _b);
        foreach (var song in new[] { _a, _b })
        {
            _database.Context.UserSongs.Add(new UserSong
            {
                UserId = _listener.UserId, SongId = song.SongId, LastPlayed = DateTime.MinValue, Version = DateTime.UtcNow,
            });
        }
        _database.Context.SaveChanges();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static User NewUser(string name, bool curator) => new()
    {
        UserId = Guid.NewGuid().ToString(), Username = name, Version = DateTime.UtcNow, ServerHash = "server-hash", IsCurator = curator,
    };

    private static Song NewSong(string name) => new()
    {
        SongId = Guid.NewGuid().ToString(), Name = name, FileExtension = ".mp3", FileHash = Guid.NewGuid().ToString(), Version = DateTime.UtcNow,
    };

    private async Task<TResponse> SendAsAsync<TResponse>(User user, IRequest<TResponse> request)
    {
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _database.CreateContext());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ShuffullContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser(user));
        services.AddValidatorsFromAssemblies([coreAssembly]);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(RoleAuthorizationBehavior<,>));
        });
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private async Task ReportAsListenerAsync()
    {
        var result = await SendAsAsync(_listener, new ReportSongDuplicatesCommand(_listener.UserId,
            [new SongDuplicateReportItem("r1", _a.SongId, _b.SongId, "duplicate", null, null)]));
        Assert.True(result.IsOk);
        Assert.Equal(SongDuplicateReportOutcomes.Recorded, Assert.Single(result.Get().Results).Outcome);
    }

    [Fact]
    public async Task NonCurator_CanReport_ButIsForbiddenFromTheQueue()
    {
        await ReportAsListenerAsync();

        var result = await SendAsAsync(_listener, new GetSongDuplicateQueueQuery());

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Forbidden, ResultErrors.KindOf(result.GetError()));
    }

    [Fact]
    public async Task NonCurator_IsForbiddenFromResolving_AndTheReportStaysOpen()
    {
        await ReportAsListenerAsync();

        var result = await SendAsAsync(_listener, new ResolveSongDuplicateReportsCommand(_listener.UserId, _a.SongId, _b.SongId, "notDuplicate"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Forbidden, ResultErrors.KindOf(result.GetError()));
        await using var context = _database.CreateContext();
        Assert.Equal(SongDuplicateReportStatus.Pending, (await context.SongDuplicateReports.SingleAsync()).Status);
    }

    [Fact]
    public async Task Curator_SeesTheQueueAndResolvesThePair()
    {
        await ReportAsListenerAsync();

        var queue = await SendAsAsync(_curator, new GetSongDuplicateQueueQuery());
        Assert.True(queue.IsOk);
        Assert.Single(queue.Get().Pairs);

        var resolved = await SendAsAsync(_curator, new ResolveSongDuplicateReportsCommand(_curator.UserId, _a.SongId, _b.SongId, "keptBoth"));
        Assert.True(resolved.IsOk);
        Assert.Equal("keptBoth", Assert.Single(resolved.Get().Reports).Status);
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("withdrawn")]
    [InlineData("nope")]
    [InlineData(null)]
    public async Task Resolve_OnlyAcceptsNotDuplicateOrKeptBoth(string? resolution)
    {
        var result = await SendAsAsync(_curator, new ResolveSongDuplicateReportsCommand(_curator.UserId, _a.SongId, _b.SongId, resolution));

        Assert.True(result.IsError);
        Assert.Contains("resolution must be", result.GetError().Message);
    }
}
