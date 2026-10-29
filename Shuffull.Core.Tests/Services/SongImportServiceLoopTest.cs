using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The import loop's queue (<see cref="SongImportService.NextDueImport"/>) and its survival of database errors
/// (MC2018/Shuffull#42).
/// </summary>
public class SongImportServiceLoopTest : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SongImport NewSongImport(string id, DateTime lastUpdatedAt) => new()
    {
        SongImportId = id,
        Name = id,
        ImportFolder = "folder",
        FileType = ".mp3",
        UserId = "user-1",
        LastUpdatedAt = lastUpdatedAt,
    };

    private async Task<List<string>> DueIdsAsync(DateTime now) =>
        await SongImportService.NextDueImport(_database.CreateContext().SongImports, now)
            .Select(x => x.SongImportId)
            .ToListAsync();

    [Fact]
    public async Task NextDueImport_TakesTheOldestFirst()
    {
        _database.Context.SongImports.AddRange(
            NewSongImport("newer", Now.AddMinutes(-1)),
            NewSongImport("oldest", Now.AddMinutes(-30)),
            NewSongImport("middle", Now.AddMinutes(-10)));
        await _database.Context.SaveChangesAsync();

        Assert.Equal(new[] { "oldest", "middle", "newer" }, await DueIdsAsync(Now));
    }

    [Fact]
    public async Task NextDueImport_SkipsARowWaitingOutItsBackoff_UntilItIsDue()
    {
        var failing = NewSongImport("failing", Now.AddHours(-1));
        failing.RecordTagFailure(Now);
        var completed = NewSongImport("completed", Now.AddHours(-2));
        completed.SetState(SongImportState.Completed);
        _database.Context.SongImports.AddRange(failing, completed, NewSongImport("waiting", Now.AddMinutes(-5)));
        await _database.Context.SaveChangesAsync();

        // The failing row is the oldest, but it waits out its backoff while the rest of the queue goes ahead.
        Assert.Equal(new[] { "waiting" }, await DueIdsAsync(Now));
        // Once its backoff passes it's due again, behind the rows that were waiting longer.
        Assert.Equal(new[] { "waiting", "failing" }, await DueIdsAsync(Now.AddMinutes(1)));
    }

    [Fact]
    public void LoopErrorDelay_DoublesFromThePollIntervalAndCapsAtFiveMinutes()
    {
        var interval = TimeSpan.FromSeconds(5);

        Assert.Equal(TimeSpan.FromSeconds(5), SongImportService.LoopErrorDelay(1, interval));
        Assert.Equal(TimeSpan.FromSeconds(10), SongImportService.LoopErrorDelay(2, interval));
        Assert.Equal(TimeSpan.FromSeconds(160), SongImportService.LoopErrorDelay(6, interval));
        Assert.Equal(TimeSpan.FromMinutes(5), SongImportService.LoopErrorDelay(7, interval));
        Assert.Equal(TimeSpan.FromMinutes(5), SongImportService.LoopErrorDelay(1000, interval));
    }

    [Fact]
    public void OnTagFailure_RetriesUntilTheLastAttempt()
    {
        var import = NewSongImport("import", Now);
        for (var i = 0; i < SongImport.MaxTagAttempts - 1; i++)
        {
            Assert.Equal(SongImportService.TagFailureOutcome.Retry, SongImportService.OnTagFailure(import));
            import.RecordTagFailure(Now);
        }
    }

    [Fact]
    public void OnTagFailure_OnTheLastAttempt_ImportsANewSongUntagged()
    {
        var import = FailedUntilLastAttempt(NewSongImport("import", Now));

        Assert.Equal(SongImportService.TagFailureOutcome.ImportUntagged, SongImportService.OnTagFailure(import));
    }

    [Fact]
    public void OnTagFailure_OnTheLastAttempt_FailsAReplacementRatherThanWipeTheSongsTags()
    {
        var import = NewSongImport("import", Now);
        import.ReplacesSongId = "song-1";
        FailedUntilLastAttempt(import);

        Assert.Equal(SongImportService.TagFailureOutcome.FailImport, SongImportService.OnTagFailure(import));
    }

    private static SongImport FailedUntilLastAttempt(SongImport import)
    {
        while (!import.IsLastTagAttempt)
        {
            import.RecordTagFailure(Now);
        }
        return import;
    }

    [Fact]
    public async Task ExecuteAsync_ADatabaseErrorPausesTheLoopInsteadOfEndingIt()
    {
        // The first poll query throws, as a Postgres blip would. Before the fix that faulted ExecuteAsync, which
        // stops the whole API under the host's default BackgroundServiceExceptionBehavior.
        var failFirstQuery = new FailFirstQueryInterceptor();
        var services = new ServiceCollection()
            .AddScoped<ShuffullContext>(_ => _database.CreateContext(failFirstQuery))
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Shuffull:Files:MusicRootDirectory"] = "music" })
            .Build();
        var importer = new SongImportService(configuration, services, NullLogger<SongImportService>.Instance, Mock.Of<IFileStorageService>())
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
        };

        await importer.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (failFirstQuery.Queries < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        await importer.StopAsync(CancellationToken.None);

        Assert.True(failFirstQuery.Queries >= 3, $"the loop polled {failFirstQuery.Queries} time(s) after the error");
        Assert.False(importer.ExecuteTask!.IsFaulted, importer.ExecuteTask.Exception?.ToString());
    }

    private sealed class FailFirstQueryInterceptor : DbCommandInterceptor
    {
        private int _queries;
        public int Queries => Volatile.Read(ref _queries);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _queries) == 1)
            {
                throw new InvalidOperationException("simulated database outage");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
