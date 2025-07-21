namespace Shuffull.Site.Models.Files;

[Serializable]
public class GenresFile
{
    public List<MainGenre> MainGenres { get; set; } = [];

    [Serializable]
    public class MainGenre
    {
        public string Name { get; set; } = string.Empty;
        public List<string> SubGenreNames { get; set; } = [];
    }
}
