using Shuffull.Shared.Enums;

namespace Shuffull.Core.Models.Database;

public class Genre : Tag
{
    public List<GenreRelation> GenreRelationsAsMain { get; set; }
    public List<GenreRelation> GenreRelationsAsSub { get; set; }

    public bool IsMain => GenreRelationsAsMain.Count != 0;

    public Genre()
    {
        Type = TagType.Genre;
    }
}
