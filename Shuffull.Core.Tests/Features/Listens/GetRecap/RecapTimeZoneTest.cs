using Shuffull.Core.Features.Listens.GetRecap;

namespace Shuffull.Core.Tests.Features.Listens.GetRecap;

public class RecapTimeZoneTest
{
    [Theory]
    [InlineData("+02:00", "+02:00", 120)]
    [InlineData("+0200", "+02:00", 120)]
    [InlineData("-03:30", "-03:30", -210)]
    [InlineData("+14:00", "+14:00", 840)]
    [InlineData("-00:00", "+00:00", 0)]
    public void Offset_IsAFixedZoneNamedByItsOffset(string value, string id, int minutes)
    {
        Assert.True(RecapTimeZone.TryParse(value, out var zone));
        Assert.Equal(id, zone.Id);
        Assert.Equal(TimeSpan.FromMinutes(minutes), zone.BaseUtcOffset);
        Assert.False(zone.SupportsDaylightSavingTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UTC")]
    [InlineData("Z")]
    public void BlankOrUtc_IsUtc(string? value)
    {
        Assert.True(RecapTimeZone.TryParse(value, out var zone));
        Assert.Equal(TimeZoneInfo.Utc, zone);
    }

    [Fact]
    public void StartOfYear_IsLocalMidnightInUtc()
    {
        Assert.True(RecapTimeZone.TryParse("America/New_York", out var newYork));
        Assert.True(RecapTimeZone.TryParse("Pacific/Auckland", out var auckland));

        Assert.Equal(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc), RecapTimeZone.StartOfYearUtc(2026, newYork));
        // Auckland is on daylight time (UTC+13) in January.
        Assert.Equal(new DateTime(2025, 12, 31, 11, 0, 0, DateTimeKind.Utc), RecapTimeZone.StartOfYearUtc(2026, auckland));
        Assert.Equal(DateTimeKind.Utc, RecapTimeZone.StartOfYearUtc(2026, newYork).Kind);
    }
}
