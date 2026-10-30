using Shuffull.Core.Features.Listens.GetListenStats;

namespace Shuffull.Core.Tests.Features.Listens.GetListenStats;

public class GetListenStatsValidatorTest
{
    private readonly GetListenStatsValidator _validator = new();

    [Fact]
    public void DefaultRule_Passes()
        => Assert.True(_validator.Validate(new GetListenStatsQuery("user", MaybeDropRule.Default)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MissingUser_Fails(string? userId)
        => Assert.False(_validator.Validate(new GetListenStatsQuery(userId!, MaybeDropRule.Default)).IsValid);

    [Fact]
    public void NullRule_Fails()
        => Assert.False(_validator.Validate(new GetListenStatsQuery("user", null!)).IsValid);

    [Theory]
    [InlineData(0, 3, 5)]  // no time limit
    [InlineData(30, 0, 5)] // flags every song
    [InlineData(30, 6, 5)] // can never fire
    public void ImpossibleRule_Fails(double seconds, int skips, int ofLastPlays)
        => Assert.False(_validator.Validate(new GetListenStatsQuery("user", new MaybeDropRule(seconds, skips, ofLastPlays))).IsValid);
}
