using Shuffull.Core.Features.Songs.MergeSongs;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Features.Songs.MergeSongs;

public class MergeSongsValidatorTest
{
    private readonly MergeSongsValidator _validator = new();
    private static readonly string TooLong = new('a', SongMerge.MaxIdLength + 1);

    [Fact]
    public void Valid_Passes()
        => Assert.True(_validator.Validate(new MergeSongsCommand("user", "survivor", "loser")).IsValid);

    [Fact]
    public void LongestId_Passes()
        => Assert.True(_validator.Validate(new MergeSongsCommand("user", new string('a', SongMerge.MaxIdLength), "loser")).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MissingUser_Fails(string? userId)
        => Assert.False(_validator.Validate(new MergeSongsCommand(userId!, "survivor", "loser")).IsValid);

    [Theory]
    [InlineData("", "loser")]
    [InlineData(null, "loser")]
    [InlineData("survivor", "")]
    [InlineData("survivor", null)]
    public void MissingSong_Fails(string? survivorId, string? loserId)
        => Assert.False(_validator.Validate(new MergeSongsCommand("user", survivorId!, loserId!)).IsValid);

    [Fact]
    public void OverLongIds_Fail()
    {
        Assert.False(_validator.Validate(new MergeSongsCommand("user", TooLong, "loser")).IsValid);
        Assert.False(_validator.Validate(new MergeSongsCommand("user", "survivor", TooLong)).IsValid);
    }

    [Fact]
    public void SameSongTwice_Fails()
        => Assert.False(_validator.Validate(new MergeSongsCommand("user", "song", "song")).IsValid);
}
