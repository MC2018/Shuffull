using Shuffull.Shared.Enums;

namespace Shuffull.Core.Models.Database;

public class Mood : Tag
{
    public Mood()
    {
        Type = TagType.Mood;
    }
}
