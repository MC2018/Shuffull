using FluentValidation.TestHelper;
using Shuffull.Core.Features.Songs.GetSongsChanged;

namespace Shuffull.Core.Tests.Features.Songs.GetSongsChanged;

public class GetSongsChangedValidatorTest
{
    // afterDate is an open cursor (any timestamp, including the year-0001 sentinel for a first sync), so the
    // validator intentionally imposes no constraints — every value is accepted.
    public static IEnumerable<object[]> ValidCursors() => new List<object[]>
    {
        new object[] { DateTime.MinValue },
        new object[] { DateTime.MaxValue },
        new object[] { new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
        new object[] { DateTime.UtcNow },
    };

    [Theory]
    [MemberData(nameof(ValidCursors))]
    public void AnyCursor_ProducesNoValidationErrors(DateTime afterDate)
    {
        var validator = new GetSongsChangedValidator();
        var errors = validator.TestValidate(new GetSongsChangedQuery(afterDate)).Errors;
        Assert.Empty(errors);
    }
}
