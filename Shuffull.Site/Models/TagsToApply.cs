using Shuffull.Site.Models.Database;

namespace Shuffull.Site.Models
{
    public class TagsToApply
    {
        public List<Tag> ExistingTags { get; set; } = new();
        public List<Tag> NewTags { get; set; } = new();
    }
}
