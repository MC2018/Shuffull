namespace Shuffull.Metadata.Models.AI;

[Serializable]
public record GenerateMainGenresRequest(string SongName, List<string> ArtistNames, List<string> MainGenres, string? MainGenresContext = null);
