using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Tools;

public class IdGeneratorTest
{
    [Fact]
    public void Generate_ReturnsNonEmptyId()
    {
        Assert.False(string.IsNullOrWhiteSpace(IdGenerator.Generate()));
    }

    [Fact]
    public void Generate_ReturnsLowercaseValue()
    {
        var id = IdGenerator.Generate();

        Assert.Equal(id.ToLowerInvariant(), id);
    }

    [Fact]
    public void Generate_ReturnsCanonical26CharacterUlid()
    {
        // A ULID is encoded in Crockford base32 and is always 26 characters long.
        var id = IdGenerator.Generate();

        Assert.Equal(26, id.Length);
        // Crockford base32 (lowercased) excludes i, l, o, u.
        Assert.Matches("^[0-9a-hjkmnp-tv-z]+$", id);
    }

    [Fact]
    public void Generate_ProducesUniqueValues()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => IdGenerator.Generate()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
