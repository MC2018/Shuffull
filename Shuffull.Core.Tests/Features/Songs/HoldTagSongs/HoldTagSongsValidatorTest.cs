using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.HoldTagSongs;

namespace Shuffull.Core.Tests.Features.Songs.HoldTagSongs;

public class HoldTagSongsValidatorTest
{
    [Fact]
    public void AReport_IsCappedAtOnePage()
    {
        var validator = new HoldTagSongsValidator();
        var page = Enumerable.Range(0, GetPendingTagSongsHandler.MaxLimit).Select(i => $"song-{i}").ToList();

        Assert.True(validator.Validate(new HoldTagSongsCommand(page)).IsValid);
        Assert.False(validator.Validate(new HoldTagSongsCommand([.. page, "one-more"])).IsValid);
        Assert.False(validator.Validate(new HoldTagSongsCommand(null!)).IsValid);
    }
}
