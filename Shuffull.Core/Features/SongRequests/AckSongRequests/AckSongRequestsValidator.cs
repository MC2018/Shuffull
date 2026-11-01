using FluentValidation;

namespace Shuffull.Core.Features.SongRequests.AckSongRequests;

public class AckSongRequestsValidator : AbstractValidator<AckSongRequestsCommand>
{
    public AckSongRequestsValidator()
    {
        RuleFor(x => x.Acks)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Acks are required.")
            .Must(acks => acks.Count <= AckSongRequestsHandler.MaxBatch)
            .WithMessage($"At most {AckSongRequestsHandler.MaxBatch} acks per call.");
    }
}
