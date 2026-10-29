using Shuffull.Core.Tools;

namespace Shuffull.Core.Tests.Tools;

public class BackoffTest
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 60)]
    [InlineData(1000, 60)]
    [InlineData(0, 1)]
    public void Doubling_StartsAtFirstDoublesAndCaps(int attempt, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            Backoff.Doubling(TimeSpan.FromMinutes(1), attempt, TimeSpan.FromHours(1)));
    }
}
