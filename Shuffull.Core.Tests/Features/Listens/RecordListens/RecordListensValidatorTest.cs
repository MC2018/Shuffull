using Shuffull.Core.Features.Listens.RecordListens;

namespace Shuffull.Core.Tests.Features.Listens.RecordListens;

public class RecordListensValidatorTest
{
    private static readonly ListenItem Listen = new("listen", "song", DateTime.UtcNow, DateTime.UtcNow, 1, 1, "finished", "other");

    private readonly RecordListensValidator _validator = new();

    [Fact]
    public void Valid_Passes()
        => Assert.True(_validator.Validate(new RecordListensCommand("user", [Listen])).IsValid);

    [Fact]
    public void EmptyList_Passes() // the handler treats it as a no-op
        => Assert.True(_validator.Validate(new RecordListensCommand("user", [])).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MissingUser_Fails(string? userId)
        => Assert.False(_validator.Validate(new RecordListensCommand(userId!, [Listen])).IsValid);

    [Fact]
    public void NullListens_Fails()
        => Assert.False(_validator.Validate(new RecordListensCommand("user", null!)).IsValid);
}
