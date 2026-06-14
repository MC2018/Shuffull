namespace Shuffull.Metadata.Models;

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
