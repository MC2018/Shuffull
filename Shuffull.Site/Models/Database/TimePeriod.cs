using Shuffull.Shared.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Site.Models.Database;

public class TimePeriod : Tag
{
    public TimePeriod()
    {
        Type = TagType.TimePeriod;
    }
}
