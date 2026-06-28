using Shuffull.Api.Models.Files;
using Shuffull.Core.Models.Database;
using Shuffull.Metadata.Models;

namespace Shuffull.Core.Tests.Models.Files;

/// <summary>
/// Guards the producer-tags → EF-tag mapping against creating an empty-named tag. The producer now enforces a
/// valid era, but a legacy/cached response could still carry an empty TimePeriod, which must NOT become a tag.
/// </summary>
public class GeneratedSongTagsExtensionsTest
{
    private static GeneratedSongTags Tags(string timePeriod) =>
        new(MainGenres: ["House"], SubGenres: [], Languages: ["Instrumental"], TimePeriod: timePeriod);

    [Fact]
    public void ToTagList_WithValidTimePeriod_AddsTimePeriodTag()
    {
        var tags = Tags("2010s").ToTagList();

        var timePeriod = tags.OfType<TimePeriod>().SingleOrDefault();
        Assert.NotNull(timePeriod);
        Assert.Equal("2010s", timePeriod!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToTagList_WithEmptyTimePeriod_AddsNoTimePeriodTagAndNoEmptyNames(string timePeriod)
    {
        var tags = Tags(timePeriod).ToTagList();

        Assert.Empty(tags.OfType<TimePeriod>());
        Assert.DoesNotContain(tags, t => string.IsNullOrWhiteSpace(t.Name));
    }
}
