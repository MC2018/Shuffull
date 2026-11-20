using Shuffull.Core.Features.GenreJams.DeleteGenreJam;

namespace Shuffull.Core.Tests.Features.GenreJams.DeleteGenreJam;

public class DeleteGenreJamValidatorTest
{
    private readonly DeleteGenreJamValidator _validator = new();

    [Fact]
    public void Valid_Passes() => Assert.True(_validator.Validate(new DeleteGenreJamCommand("user", "jam-1")).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65, one past the column
    public void BadId_Fails(string id) => Assert.False(_validator.Validate(new DeleteGenreJamCommand("user", id)).IsValid);
}
