using Shuffull.Core.Features.Songs.KeepSongs;

namespace Shuffull.Core.Tests.Features.Songs.KeepSongs;

public class KeepSongsValidatorTest
{
    private readonly KeepSongsValidator _validator = new();

    [Fact]
    public void Valid_Passes()
        => Assert.True(_validator.Validate(new KeepSongsCommand("user", ["song"])).IsValid);

    [Fact]
    public void EmptyList_Passes() // the handler treats it as a no-op
        => Assert.True(_validator.Validate(new KeepSongsCommand("user", [])).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MissingUser_Fails(string? userId)
        => Assert.False(_validator.Validate(new KeepSongsCommand(userId!, ["song"])).IsValid);

    [Fact]
    public void NullSongIds_Fails()
        => Assert.False(_validator.Validate(new KeepSongsCommand("user", null!)).IsValid);
}
