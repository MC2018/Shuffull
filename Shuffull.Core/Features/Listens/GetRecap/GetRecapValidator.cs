using FluentValidation;

namespace Shuffull.Core.Features.Listens.GetRecap;

public class GetRecapValidator : AbstractValidator<GetRecapQuery>
{
    public const int MinYear = 2000;
    public const int MaxYear = 2999;

    public GetRecapValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Year)
            .InclusiveBetween(MinYear, MaxYear).WithMessage($"Year must be between {MinYear} and {MaxYear}.");

        RuleFor(x => x.TimeZone)
            .Must(tz => RecapTimeZone.TryParse(tz, out _))
            .WithMessage("Unknown time zone. Send an IANA id such as Europe/Berlin, or an offset such as +02:00.");
    }
}
