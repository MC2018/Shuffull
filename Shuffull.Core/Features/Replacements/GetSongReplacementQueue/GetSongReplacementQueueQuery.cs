using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.Replacements.GetSongReplacementQueue;

/// <summary>Producer: the replacement requests at <paramref name="Status"/>, oldest first.</summary>
public record GetSongReplacementQueueQuery(SongReplacementStatus Status) : IRequest<Result<IReadOnlyList<SongReplacementDto>>>;
