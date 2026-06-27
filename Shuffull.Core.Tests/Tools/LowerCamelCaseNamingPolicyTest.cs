using Shuffull.Api.Tools;

namespace Shuffull.Core.Tests.Tools;

public class LowerCamelCaseNamingPolicyTest
{
    private readonly LowerCamelCaseNamingPolicy _policy = new();

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("SongId", "songId")]
    [InlineData("UserId", "userId")]
    [InlineData("FileExtension", "fileExtension")]
    public void ConvertName_LowercasesLeadingUppercaseRun(string input, string expected)
    {
        Assert.Equal(expected, _policy.ConvertName(input));
    }

    [Fact]
    public void ConvertName_LowercasesEntireAllUppercaseAcronym()
    {
        // No lowercase character is ever encountered, so the whole string is lowered.
        Assert.Equal("id", _policy.ConvertName("ID"));
        Assert.Equal("url", _policy.ConvertName("URL"));
    }

    [Fact]
    public void ConvertName_LowercasesEntireLeadingUppercaseRun()
    {
        // The policy lowers every char of the leading uppercase run (here I, O, S), not just the first,
        // so an embedded acronym followed by a lowercase tail collapses: "IOStream" -> "iostream".
        Assert.Equal("iostream", _policy.ConvertName("IOStream"));
    }

    [Fact]
    public void ConvertName_LeavesAlreadyLowerLeadingCharsThrows()
    {
        Assert.Throws<ArgumentException>(() => _policy.ConvertName("songId"));
    }

    [Fact]
    public void ConvertName_HandlesEmptyString()
    {
        Assert.Equal(string.Empty, _policy.ConvertName(string.Empty));
    }
}
