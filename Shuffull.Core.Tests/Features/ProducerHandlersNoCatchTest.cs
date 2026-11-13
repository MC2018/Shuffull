using Shuffull.Core.Features.Replacements.FlagSongReplacement;
using Shuffull.Core.Features.Replacements.GetSongReplacementQueue;
using Shuffull.Core.Features.Songs.UpdateSongLyrics;
using Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;
using Shuffull.Core.Features.YoutubeRatings.GetYoutubeRatingQueue;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features;

/// <summary>
/// What the HTTP tests (ReplacementsEndpointTest, SongLyricsEndpointTest, YoutubeRatingsAckTest) can't see from
/// outside: these handlers let a database fault escape, so the endpoints answer it with a 500 as they did before
/// the logic left the controllers. Their siblings catch and classify with <c>ResultErrors.FromException</c>, which
/// would turn some faults into a 400; this test stops them being "tidied" to match.
/// </summary>
public class ProducerHandlersNoCatchTest : IDisposable
{
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Handlers => new() { "flag", "replacement queue", "lyrics", "rating queue", "rating ack" };

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task Handle_DatabaseFault_ThrowsInsteadOfReturningError(string name)
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
