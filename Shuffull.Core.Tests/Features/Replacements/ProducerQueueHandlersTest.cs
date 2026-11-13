using Nut.Results;
using Shuffull.Core.Features.Replacements.FlagSongReplacement;
using Shuffull.Core.Features.Replacements.GetSongReplacementQueue;
using Shuffull.Core.Features.Songs.UpdateSongLyrics;
using Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;
using Shuffull.Core.Features.YoutubeRatings.GetYoutubeRatingQueue;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Replacements;

/// <summary>
/// What the HTTP tests (ReplacementsEndpointTest, SongLyricsEndpointTest, YoutubeRatingsAckTest) can't see from
/// outside: these handlers let a database fault escape, so the endpoints answer it with a 500 as they did before
/// the logic left the controllers. Their siblings catch and classify with <c>ResultErrors.FromException</c>, which
/// would turn some faults into a 400; this test stops them being "tidied" to match.
/// </summary>
public class ProducerQueueHandlersTest : IDisposable
{
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Flag_says_whether_it_queued_a_row()
    {
        _database.Context.Songs.Add(new Song
        {
            SongId = "song-1",
            Name = "Song 1",
            FileExtension = ".mp3",
            FileHash = "hash-1",
            ExternalSongId = "video-1",
            Version = DateTime.UtcNow,
        });
        await _database.Context.SaveChangesAsync();
        var handler = new FlagSongReplacementHandler(_database.Context, TimeProvider.System);

        var first = await handler.Handle(new FlagSongReplacementCommand("song-1", null), CancellationToken.None);
        var second = await handler.Handle(new FlagSongReplacementCommand("song-1", null), CancellationToken.None);

        Assert.True(first.Get().Queued);
        Assert.False(second.Get().Queued);
    }

    public static TheoryData<string> Handlers => new() { "flag", "replacement queue", "lyrics", "rating queue", "rating ack" };

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task A_database_fault_is_thrown_not_returned(string name)
    {
        var context = _database.CreateContext();
        await context.DisposeAsync();
        var time = TimeProvider.System;

        Func<Task> handle = name switch
        {
            "flag" => () => new FlagSongReplacementHandler(context, time)
                .Handle(new FlagSongReplacementCommand("song-1", null), CancellationToken.None),
            "replacement queue" => () => new GetSongReplacementQueueHandler(context)
                .Handle(new GetSongReplacementQueueQuery(SongReplacementStatus.Pending), CancellationToken.None),
            "lyrics" => () => new UpdateSongLyricsHandler(context, time)
                .Handle(new UpdateSongLyricsCommand("video-1", null, "la la", false, null), CancellationToken.None),
            "rating queue" => () => new GetYoutubeRatingQueueHandler(context)
                .Handle(new GetYoutubeRatingQueueQuery(YoutubeRatingStatus.Pending), CancellationToken.None),
            "rating ack" => () => new AckYoutubeRatingsHandler(context, time)
                .Handle(new AckYoutubeRatingsCommand([new YoutubeRatingAck("rating-1", "like", null)]), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        await Assert.ThrowsAsync<ObjectDisposedException>(handle);
    }
}
