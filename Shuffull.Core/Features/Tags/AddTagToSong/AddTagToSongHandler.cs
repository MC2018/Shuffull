using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Tags;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Tags.AddTagToSong;

public class AddTagToSongHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddTagToSongCommand, Result<AddTagToSongResponse>>
{
    public async Task<Result<AddTagToSongResponse>> Handle(AddTagToSongCommand request, CancellationToken cancellationToken)
    {
        var tagResult = await unitOfWork.Repository<Tag>().GetByIdAsync(request.TagId, cancellationToken);
        if (tagResult.IsError)
        {
            return Result.Error<AddTagToSongResponse>("Tag not found.");
        }

        var songResult = await unitOfWork.Repository<Song>().GetByIdAsync(request.SongId, cancellationToken);
        if (songResult.IsError)
        {
            return Result.Error<AddTagToSongResponse>("Song not found.");
        }

        var songTagRepository = unitOfWork.Repository<SongTag>();
        var existsResult = await songTagRepository.AnyAsync(
            new SongTagSpec(request.SongId, request.TagId).AsNoTracking(), cancellationToken);
        if (existsResult.IsError)
        {
            return Result.Error<AddTagToSongResponse>(existsResult.GetError().Message);
        }

        // Already tagged: treat as an idempotent no-op success.
        if (existsResult.Get())
        {
            return Result.Ok(new AddTagToSongResponse(false));
        }

        var addResult = await songTagRepository.AddAsync(new SongTag
        {
            SongTagId = IdGenerator.Generate(),
            SongId = request.SongId,
            TagId = request.TagId,
        }, cancellationToken);
        if (addResult.IsError)
        {
            return Result.Error<AddTagToSongResponse>(addResult.GetError().Message);
        }

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<AddTagToSongResponse>(saveResult.GetError().Message);
        }

        return Result.Ok(new AddTagToSongResponse(true));
    }
}
