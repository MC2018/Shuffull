using Shuffull.Shared.Enums;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// Creates the correct <see cref="Tag"/> subclass for a <see cref="TagType"/> so EF stores the right
/// table-per-hierarchy discriminator. Centralised here because both the import pipeline and curator edits
/// need to materialise new tag rows from a (name, type) pair.
/// </summary>
public static class TagFactory
{
    public static Tag Create(string tagId, string name, TagType type) => type switch
    {
        TagType.Genre => new Genre { TagId = tagId, Name = name },
        TagType.TimePeriod => new TimePeriod { TagId = tagId, Name = name },
        TagType.Language => new Language { TagId = tagId, Name = name },
        TagType.Mood => new Mood { TagId = tagId, Name = name },
        TagType.Theme => new Theme { TagId = tagId, Name = name },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown tag type."),
    };
}
