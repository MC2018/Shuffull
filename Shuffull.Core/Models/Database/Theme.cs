using Shuffull.Shared.Enums;

namespace Shuffull.Core.Models.Database;

public class Theme : Tag
{
    public Theme()
    {
        Type = TagType.Theme;
    }
}
