using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Models;

namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>
/// Runs the real intake and import code over a <see cref="DatabaseFixture"/>, with file storage mocked. One copy, so
/// a change to either service's dependencies or storage calls is made here rather than in every test that imports.
/// </summary>
public static class SongImportHarness
{
    /// <summary>A few silent MPEG-1 Layer III frames (128 kbps, 44.1 kHz): enough for TagLib to open as an mp3.</summary>
    public static byte[] Mp3Frames(byte fill = 0)
    {
        const int frameLength = 417; // 144 * 128000 / 44100
        var bytes = new byte[frameLength * 10];
        for (var offset = 0; offset < bytes.Length; offset += frameLength)
        {
            bytes[offset] = 0xFF;
            bytes[offset + 1] = 0xFB;
            bytes[offset + 2] = 0x90;
            bytes[offset + 3] = 0x00;
            for (var i = offset + 4; i < offset + frameLength; i++)
            {
                bytes[i] = fill;
            }
        }
        return bytes;
    }

    /// <summary>The intake service over <paramref name="database"/>, with staging uploads that always succeed.</summary>
    public static SongImportIntakeService Intake(DatabaseFixture database)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Shuffull:Files:SongImportDirectory"] = "/imports",
        }).Build();
        return new SongImportIntakeService(database.Context, storage.Object, configuration, NullLogger<SongImportIntakeService>.Instance);
    }

    /// <summary>
    /// Queues <paramref name="songImport"/> and runs one pass of the real import loop over <paramref name="audio"/>.
    /// Returns the import row as the loop left it.
    /// </summary>
    public static async Task<SongImport> RunImportAsync(DatabaseFixture database, SongImport songImport, byte[] audio)
    {
        database.Context.SongImports.Add(songImport);
        await database.Context.SaveChangesAsync();

        var services = new ServiceCollection()
            .AddScoped<ShuffullContext>(_ => database.CreateContext())
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Shuffull:Files:MusicRootDirectory"] = "music" })
            .Build();
        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(x => x.DownloadFileBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(audio));
        fileStorage.Setup(x => x.DownloadSerializableObjectAsync<GeneratedSongTags>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Error<GeneratedSongTags>("no saved AI response"));
        fileStorage.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        fileStorage.Setup(x => x.MoveFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        fileStorage.Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var importer = new SongImportService(configuration, services, NullLogger<SongImportService>.Instance, fileStorage.Object);

        Assert.True(await importer.ImportNextAsync(CancellationToken.None));
        return await database.CreateContext().SongImports.AsNoTracking().SingleAsync(si => si.SongImportId == songImport.SongImportId);
    }
}
