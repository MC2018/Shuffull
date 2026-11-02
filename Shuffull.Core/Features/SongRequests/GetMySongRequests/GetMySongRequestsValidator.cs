using FluentValidation;

namespace Shuffull.Core.Features.SongRequests.GetMySongRequests;

public class GetMySongRequestsValidator : AbstractValidator<GetMySongRequestsQuery>
{
    public GetMySongRequestsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");
    }
}
