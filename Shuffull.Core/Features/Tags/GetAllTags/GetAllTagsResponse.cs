using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Tags.GetAllTags;

public record GetAllTagsResponse(IReadOnlyList<TagDto> Tags);
