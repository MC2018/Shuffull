using FluentValidation;

namespace Shuffull.Core.Features.Songs.GetSongByExternalId;

public class GetSongByExternalIdValidator : AbstractValidator<GetSongByExternalIdQuery>
{
    public GetSongByExternalIdValidator()
    {
        RuleFor(x => x.ExternalSongId)
            .NotEmpty().WithMessage("ExternalSongId is required.");
    }
}
