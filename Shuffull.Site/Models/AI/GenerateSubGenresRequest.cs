namespace Shuffull.Site.Models.AI;

[Serializable]
public record GenerateSubGenresRequest(string SongName, List<string> ArtistNames, List<string> SubGenres, string? SubGenresContext = null);
