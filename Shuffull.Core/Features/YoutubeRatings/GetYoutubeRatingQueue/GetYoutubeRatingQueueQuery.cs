using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.YoutubeRatings.GetYoutubeRatingQueue;

/// <summary>Producer: the like-parity rows at <paramref name="Status"/> (the funnel asks for Pending), least recently updated first.</summary>
public record GetYoutubeRatingQueueQuery(YoutubeRatingStatus Status) : IRequest<Result<IReadOnlyList<YoutubeRatingDto>>>;
