using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Tags.AddTagToSong;

public record AddTagToSongCommand(string SongId, string TagId) : IRequest<Result<AddTagToSongResponse>>;
