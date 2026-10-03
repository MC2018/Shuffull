using FluentValidation;

namespace Shuffull.Core.Features.Songs.GetSongsChanged;

public class GetSongsChangedValidator : AbstractValidator<GetSongsChangedQuery>
{
    public GetSongsChangedValidator()
    {
        // afterDate is an open cursor (any timestamp, including the year-0001 sentinel for a first sync),
        // so there is nothing to constrain beyond the type itself.
    }
}
