using FluentValidation.TestHelper;
using Shuffull.Core.Features.UserSongs.SetSongLikeStatus;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.UserSongs.SetSongLikeStatus;

public class SetSongLikeStatusValidatorTest
{
    private static SetSongLikeStatusCommand SafeValue()
        => new("user-123", "song-123", LikeStatus.Like);

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { SongId = null! }).ToObjectArray(),
        (SafeValue() with { SongId = "" }).ToObjectArray(),
        // Out-of-range enum value fails IsInEnum.
        (SafeValue() with { LikeStatus = (LikeStatus)99 }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { LikeStatus = LikeStatus.Neutral }).ToObjectArray(),
        (SafeValue() with { LikeStatus = LikeStatus.Love }).ToObjectArray(),
        (SafeValue() with { LikeStatus = LikeStatus.Dislike }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(SetSongLikeStatusCommand command)
    {
        var validator = new SetSongLikeStatusValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(SetSongLikeStatusCommand command)
    {
        var validator = new SetSongLikeStatusValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
