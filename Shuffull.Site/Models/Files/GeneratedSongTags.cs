using Shuffull.Shared.Tools;
using Shuffull.Site.Models.Database;

namespace Shuffull.Site.Models.Files;

[Serializable]
public record GeneratedSongTags(List<string> MainGenres, List<string> SubGenres, List<string> Languages, string TimePeriod)
{
    public List<Tag> ToTagList()
    {
        var result = new List<Tag>();

        result.AddRange(MainGenres.Select(x => new Genre() { TagId = IdGenerator.Generate(), Name = x }));
        result.AddRange(SubGenres.Select(x => new Genre() { TagId = IdGenerator.Generate(), Name = x }));
        // rewrite a bit for new formatting. sub/main genres
        result.AddRange(Languages.Select(x => new Language() { TagId = IdGenerator.Generate(), Name = x }));
        result.Add(new TimePeriod() { TagId = IdGenerator.Generate(), Name = TimePeriod });

        return result;
    }
}
