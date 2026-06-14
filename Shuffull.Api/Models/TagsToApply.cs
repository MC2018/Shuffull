using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Models
{
    public class TagsToApply
    {
        public List<Tag> ExistingTags { get; set; } = new();
        public List<Tag> NewTags { get; set; } = new();
    }
}
