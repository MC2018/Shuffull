using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Shared.Enums;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="Tag"/> (the TPH base of Genre/Language/TimePeriod). Built
/// through the static <see cref="Create"/> factory so the concrete tag type is surfaced uniformly.
/// </summary>
public record TagDto(string TagId, string Name, TagType Type)
{
    public static Result<TagDto> Create(Tag tag)
    {
        if (tag is null)
        {
            return Result.Error<TagDto>("Tag cannot be null.");
        }

        return Result.Ok(new TagDto(tag.TagId, tag.Name, tag.Type));
    }
}
