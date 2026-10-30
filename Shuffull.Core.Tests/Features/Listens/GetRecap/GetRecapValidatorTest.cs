using Shuffull.Core.Features.Listens.GetRecap;

namespace Shuffull.Core.Tests.Features.Listens.GetRecap;

public class GetRecapValidatorTest
{
    private readonly GetRecapValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("UTC")]
    [InlineData("Europe/Berlin")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("+02:00")]
    [InlineData("-0330")]
    [InlineData("+05:45")]
    public void KnownZone_IsValid(string? timeZone)
        => Assert.True(_validator.Validate(new GetRecapQuery("user", 2026, timeZone)).IsValid);

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe/../Europe/Berlin")]
    [InlineData("/etc/localtime")]
    [InlineData("+15:00")]
    [InlineData("+02:10")]
    [InlineData("2:00")]
    [InlineData("Europe/Berlin; DROP")]
    public void UnknownOrMalformedZone_IsInvalid(string timeZone)
    {
        var result = _validator.Validate(new GetRecapQuery("user", 2026, timeZone));

        Assert.False(result.IsValid);
        Assert.Contains("Unknown time zone", result.ToString());
    }

    [Theory]
    [InlineData(1999, false)]
    [InlineData(2000, true)]
    [InlineData(2999, true)]
    [InlineData(3000, false)]
    public void Year_MustBeInRange(int year, bool valid)
        => Assert.Equal(valid, _validator.Validate(new GetRecapQuery("user", year, null)).IsValid);

    [Fact]
    public void User_IsRequired()
        => Assert.False(_validator.Validate(new GetRecapQuery("", 2026, null)).IsValid);
}
