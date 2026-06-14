namespace Shuffull.Metadata.Models;

[Serializable]
public record GeneratedSongTags(List<string> MainGenres, List<string> SubGenres, List<string> Languages, string TimePeriod);
