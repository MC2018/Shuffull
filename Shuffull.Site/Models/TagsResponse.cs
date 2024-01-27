using Shuffull.Site.Database.Models;

namespace Shuffull.Site.Models
{
    [Serializable]
    public class TagsResponse
    {
        public string[] Genres {  get; set; }
        public string[] GenresNotProvided { get; set; }
        public string[] Languages { get; set; }
        public string TimePeriod { get; set; }

        public List<Tag> ToTagList()
        {
            var result = new List<Tag>();

            result.AddRange(Genres.Select(x => new Tag() { Name = x, Type = Enums.TagType.Genre }));
            result.AddRange(Languages.Select(x => new Tag() { Name = x, Type = Enums.TagType.Language }));
            result.Add(new Tag() { Name = TimePeriod, Type = Enums.TagType.TimePeriod });

            return result;
        }
    }
}
