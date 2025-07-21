namespace Shuffull.Site.Models.AI;

[Serializable]
public record GenerateOtherSongDetailsRequest(string SongName, List<string> ArtistNames);
