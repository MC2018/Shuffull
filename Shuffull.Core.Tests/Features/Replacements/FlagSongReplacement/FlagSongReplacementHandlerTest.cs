using Nut.Results;
using Shuffull.Core.Features.Replacements.FlagSongReplacement;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Replacements.FlagSongReplacement;

/// <summary>
/// What ReplacementsEndpointTest can't see over HTTP: the endpoint answers an empty 200 either way, but the handler
/// reports whether it queued a row.
/// </summary>
public class FlagSongReplacementHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Flag_SecondFlagWhileOpen_ReportsNotQueued()
    {
        _database.Context.Songs.Add(new Song
        {
            SongId = "song-1",
            Name = "Song 1",
            FileExtension = ".mp3",
            FileHash = "hash-1",
            ExternalSongId = "video-1",
            Version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _database.Context.SaveChangesAsync();
        var handler = new FlagSongReplacementHandler(_database.Context, TimeProvider.System);

        var first = await handler.Handle(new FlagSongReplacementCommand("song-1", null), CancellationToken.None);
        var second = await handler.Handle(new FlagSongReplacementCommand("song-1", null), CancellationToken.None);

        Assert.True(first.Get().Queued);
        Assert.False(second.Get().Queued);
    }
}
