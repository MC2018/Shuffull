using FluentValidation;

namespace Shuffull.Core.Features.Songs.UpdateSong;

public class UpdateSongValidator : AbstractValidator<UpdateSongCommand>
{
    public UpdateSongValidator()
    {
        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("Song ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Song name is required.");

        // Optional fields: only constrained when supplied. BPM is a positive tempo; Energy is the 1-10 scale.
        RuleFor(x => x.Bpm)
            .GreaterThan(0).WithMessage("BPM must be greater than 0.")
            .LessThanOrEqualTo(1000).WithMessage("BPM is unreasonably high.")
            .When(x => x.Bpm.HasValue);

        RuleFor(x => x.Energy)
            .InclusiveBetween(1, 10).WithMessage("Energy must be between 1 and 10.")
            .When(x => x.Energy.HasValue);

        RuleFor(x => x.Artists)
            .NotNull().WithMessage("Artists list is required (may be empty).");

        // A song may have no artists, but any supplied entry must be a real name.
        RuleForEach(x => x.Artists)
            .NotEmpty().WithMessage("Artist names cannot be empty.");

        RuleFor(x => x.Tags)
            .NotNull().WithMessage("Tags list is required (may be empty).");

        RuleForEach(x => x.Tags).ChildRules(tag =>
        {
            tag.RuleFor(t => t.Name)
                .NotEmpty().WithMessage("Tag names cannot be empty.");
            tag.RuleFor(t => t.Type)
                .IsInEnum().WithMessage("Invalid tag type.");
        });
    }
}
