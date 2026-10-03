using System.Text;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Tools;

public class HasherTest
{
    [Fact]
    public void Argon2Hash_IsDeterministicForSameInput()
    {
        var first = Hasher.Argon2Hash("correct-horse");
        var second = Hasher.Argon2Hash("correct-horse");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Argon2Hash_ReturnsLowercaseHexOf16Bytes()
    {
        var hash = Hasher.Argon2Hash("password");

        // 16 bytes -> 32 hex characters; "-" separators stripped and lowercased.
        Assert.Equal(32, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void Argon2Hash_DifferentInputsProduceDifferentHashes()
    {
        Assert.NotEqual(Hasher.Argon2Hash("alpha"), Hasher.Argon2Hash("beta"));
    }

    [Fact]
    public void Argon2Hash_ThrowsOnEmptyInput()
    {
        // The underlying Argon2id requires a non-empty password, so an empty input surfaces as an
        // ArgumentException rather than a hash.
        Assert.Throws<ArgumentException>(() => Hasher.Argon2Hash(string.Empty));
    }

    [Fact]
    public void ShaHash_IsDeterministicForSameBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("some file content");

        Assert.Equal(Hasher.ShaHash(bytes), Hasher.ShaHash(bytes));
    }

    [Fact]
    public void ShaHash_ReturnsLowercaseHexOf32Bytes()
    {
        var hash = Hasher.ShaHash(Encoding.UTF8.GetBytes("x"));

        // SHA-256 produces 32 bytes -> 64 hex characters.
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void ShaHash_MatchesKnownVectorForEmptyInput()
    {
        // Well-known SHA-256 of the empty byte array.
        var hash = Hasher.ShaHash(Array.Empty<byte>());

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public void ShaHash_DifferentBytesProduceDifferentHashes()
    {
        Assert.NotEqual(
            Hasher.ShaHash(Encoding.UTF8.GetBytes("a")),
            Hasher.ShaHash(Encoding.UTF8.GetBytes("b")));
    }
}
