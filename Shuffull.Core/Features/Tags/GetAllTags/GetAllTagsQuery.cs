using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Tags.GetAllTags;

public record GetAllTagsQuery : IRequest<Result<GetAllTagsResponse>>;
