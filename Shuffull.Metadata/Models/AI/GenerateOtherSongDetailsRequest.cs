namespace Shuffull.Metadata.Models.AI;

[Serializable]
public record GenerateOtherSongDetailsRequest(string SongName, List<string> ArtistNames, string? OtherDetailsContext = null);
