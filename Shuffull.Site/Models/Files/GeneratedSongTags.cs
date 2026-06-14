using Shuffull.Metadata.Models;
using Shuffull.Shared.Tools;
using Shuffull.Core.Models.Database;

namespace Shuffull.Site.Models.Files;

/// <summary>
/// Maps the engine-produced <see cref="GeneratedSongTags"/> (from Shuffull.Metadata) onto Shuffull's
/// EF tag entities. Lives in Shuffull.Site because it depends on the database model and id generation —
/// the metadata library intentionally stays persistence-agnostic.
/// </summary>
public static class GeneratedSongTagsExtensions
{
    public static List<Tag> ToTagList(this GeneratedSongTags tags)
    {
        var result = new List<Tag>();

        result.AddRange(tags.MainGenres.Select(x => new Genre() { TagId = IdGenerator.Generate(), Name = x }));
        result.AddRange(tags.SubGenres.Select(x => new Genre() { TagId = IdGenerator.Generate(), Name = x }));
        result.AddRange(tags.Languages.Select(x => new Language() { TagId = IdGenerator.Generate(), Name = x }));
        result.Add(new TimePeriod() { TagId = IdGenerator.Generate(), Name = tags.TimePeriod });

        return result;
    }
}
