using Nut.Results;
using Shuffull.Metadata.Models.AI;

namespace Shuffull.Metadata.Services.AI;

public interface IAIService
{
    public Task<Result<GenerateMainGenresResponse>> GenerateMainGenresAsync(GenerateMainGenresRequest request, CancellationToken cancellationToken = default!);
    public Task<Result<GenerateSubGenresResponse>> GenerateSubGenresAsync(GenerateSubGenresRequest request, CancellationToken cancellationToken = default!);
    public Task<Result<GenerateOtherSongDetailsResponse>> GenerateOtherSongDetailsAsync(GenerateOtherSongDetailsRequest request, CancellationToken cancellationToken = default!);
}
