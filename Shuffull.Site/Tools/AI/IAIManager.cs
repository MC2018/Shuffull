using Shuffull.Site.Models;
using Shuffull.Site.Models.Database;

namespace Shuffull.Site.Tools.AI
{
    public interface IAIManager
    {
        public Task<GenerateTagsResponse> GenerateTagsAsync(Song song, List<Artist> artists, List<Tag> allGenreTags);
    }
}
