using Shuffull.Shared.Enums;
using Shuffull.Site.Models.Database;

namespace Shuffull.Site.Models
{
    [Serializable]
    public class GenerateTagsResponse
    {
        public string[] Genres {  get; set; }
        public string[] GenresNotProvided { get; set; }
        public string[] Languages { get; set; }
        public string TimePeriod { get; set; }

        public List<Tag> ToTagList()
        {
            var result = new List<Tag>();

            result.AddRange(Genres.Select(x => new Tag() { TagId = Ulid.NewUlid().ToString(), Name = x, Type = TagType.Genre }));
            result.AddRange(Languages.Select(x => new Tag() { TagId = Ulid.NewUlid().ToString(), Name = x, Type = TagType.Language }));
            result.Add(new Tag() { TagId = Ulid.NewUlid().ToString(), Name = TimePeriod, Type = TagType.TimePeriod });

            return result;
        }
    }
}
