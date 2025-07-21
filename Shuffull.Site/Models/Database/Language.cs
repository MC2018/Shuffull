using Shuffull.Shared.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Site.Models.Database;

public class Language : Tag
{
    public Language()
    {
        Type = TagType.Language;
    }
}
