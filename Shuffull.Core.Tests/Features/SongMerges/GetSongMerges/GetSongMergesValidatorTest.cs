using Shuffull.Core.Features.SongMerges.GetSongMerges;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Features.SongMerges.GetSongMerges;

public class GetSongMergesValidatorTest
{
    private readonly GetSongMergesValidator _validator = new();

    [Fact]
    public void FirstSync_Passes()
        => Assert.True(_validator.Validate(new GetSongMergesQuery(DateTime.MinValue)).IsValid);

    [Fact]
    public void LongestAfterId_Passes()
        => Assert.True(_validator.Validate(new GetSongMergesQuery(DateTime.UtcNow, new string('a', SongMerge.MaxIdLength))).IsValid);

    [Fact]
    public void OverLongAfterId_Fails()
        => Assert.False(_validator.Validate(new GetSongMergesQuery(DateTime.UtcNow, new string('a', SongMerge.MaxIdLength + 1))).IsValid);
}
