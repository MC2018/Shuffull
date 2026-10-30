using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The external drop-folder loop survives a failed scan instead of stopping the API (MC2018/Shuffull#76).
/// </summary>
public class ExternalSongImporterServiceLoopTest
{
    [Fact]
    public async Task ExecuteAsync_AFailedScanPausesTheLoopInsteadOfEndingIt()
    {
        // The first scan throws, as a storage or database blip would. Before the fix that faulted ExecuteAsync, which
        // stops the whole API under the host's default BackgroundServiceExceptionBehavior.
        var scans = 0;
        var fileStorage = new Mock<IFileStorageService>();
        fileStorage
            .Setup(x => x.GetFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref scans) == 1
                ? throw new InvalidOperationException("simulated outage")
                : Task.FromResult(Result.Ok(new List<string>())));
        var services = new ServiceCollection()
            .AddSingleton(fileStorage.Object)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Shuffull:Files:ExternalSongImportDirectory"] = "external" })
            .Build();
        var importer = new ExternalSongImporterService(services, configuration, NullLogger<ExternalSongImporterService>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
        };

        await importer.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref scans) < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        await importer.StopAsync(CancellationToken.None);

        Assert.True(Volatile.Read(ref scans) >= 3, $"the loop scanned {scans} time(s) after the error");
        Assert.False(importer.ExecuteTask!.IsFaulted, importer.ExecuteTask.Exception?.ToString());
    }

    [Fact]
    public void LoopErrorDelay_DoublesFromThePollIntervalAndCapsAtFiveMinutes()
    {
        var interval = TimeSpan.FromSeconds(20);

        Assert.Equal(TimeSpan.FromSeconds(20), ExternalSongImporterService.LoopErrorDelay(1, interval));
        Assert.Equal(TimeSpan.FromSeconds(40), ExternalSongImporterService.LoopErrorDelay(2, interval));
        Assert.Equal(TimeSpan.FromSeconds(160), ExternalSongImporterService.LoopErrorDelay(4, interval));
        Assert.Equal(TimeSpan.FromMinutes(5), ExternalSongImporterService.LoopErrorDelay(5, interval));
        Assert.Equal(TimeSpan.FromMinutes(5), ExternalSongImporterService.LoopErrorDelay(1000, interval));
    }
}
