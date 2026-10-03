using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Tags;

namespace Shuffull.Core.Features.Tags.GetAllTags;

public class GetAllTagsHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetAllTagsQuery, Result<GetAllTagsResponse>>
{
    public async Task<Result<GetAllTagsResponse>> Handle(GetAllTagsQuery request, CancellationToken cancellationToken)
    {
        var tagRepository = unitOfWork.Repository<Tag>();
        var tagsResult = await tagRepository.ListAsync(new AllTagsSpec().AsNoTracking(), cancellationToken);
        if (tagsResult.IsError)
        {
            return Result.Error<GetAllTagsResponse>(tagsResult.GetError().Message);
        }

        var dtos = new List<TagDto>();
        foreach (var tag in tagsResult.Get())
        {
            var dtoResult = TagDto.Create(tag);
            if (dtoResult.IsError)
            {
                return Result.Error<GetAllTagsResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new GetAllTagsResponse(dtos));
    }
}
