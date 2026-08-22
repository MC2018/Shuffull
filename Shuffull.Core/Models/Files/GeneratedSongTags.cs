using Shuffull.Metadata.Models;
using Shuffull.Shared.Tools;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Files;

/// <summary>
/// Maps the engine-produced <see cref="GeneratedSongTags"/> (from Shuffull.Metadata) onto Shuffull's
/// EF tag entities. Lives in Shuffull.Core alongside the tag entities it builds — the metadata library
/// intentionally stays persistence-agnostic, and Core handlers (the producer tag write-back) need this too.
/// </summary>
public static class GeneratedSongTagsExtensions
{
    public static List<Tag> ToTagList(this GeneratedSongTags tags)
    {
        var result = new List<Tag>();

        result.AddRange(tags.MainGenres.Select(x => new Genre() { TagId = IdGenerator.Generate(), Name = x }));
        result.AddRange(tags.SubGenres.Select(x => new Genre() { TagId = IdGenerator.Generate(), Name = x }));
        result.AddRange(tags.Languages.Select(x => new Language() { TagId = IdGenerator.Generate(), Name = x }));
        // Defensive: only add a time-period tag when one is actually present. The producer now enforces a valid
        // era (regenerating otherwise), but a legacy/cached response could still carry an empty string, and an
        // empty-named tag must never be created.
        if (!string.IsNullOrWhiteSpace(tags.TimePeriod))
        {
            result.Add(new TimePeriod() { TagId = IdGenerator.Generate(), Name = tags.TimePeriod });
        }

        if (tags.Moods != null)
        {
            result.AddRange(tags.Moods.Select(x => new Mood() { TagId = IdGenerator.Generate(), Name = x }));
        }

        if (tags.Themes != null)
        {
            result.AddRange(tags.Themes.Select(x => new Theme() { TagId = IdGenerator.Generate(), Name = x }));
        }

        // Final guard: never emit an empty/whitespace-named tag of ANY type. The producer's sanitizers should
        // already prevent it, but this makes it impossible for a blank name (from a legacy/cached response or a
        // future field) to become a junk tag.
        return result.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList();
    }
}
