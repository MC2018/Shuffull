using Nut.Results;
using Shuffull.Site.Models.AI;
using Shuffull.Site.Models.Database;

namespace Shuffull.Site.Services.AI
{
    public interface IAIService
    {
        public Task<Result<GenerateMainGenresResponse>> GenerateMainGenresAsync(GenerateMainGenresRequest request, CancellationToken cancellationToken = default!);
        public Task<Result<GenerateSubGenresResponse>> GenerateSubGenresAsync(GenerateSubGenresRequest request, CancellationToken cancellationToken = default!);
        public Task<Result<GenerateOtherSongDetailsResponse>> GenerateOtherSongDetailsAsync(GenerateOtherSongDetailsRequest request, CancellationToken cancellationToken = default!);
    }
}
