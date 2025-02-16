using System.Text.Json.Serialization;

namespace Shuffull.Site.Configuration
{
    public class ShuffullFilesConfiguration
    {
        [JsonIgnore]
        public const string FilesConfigurationSection = "Shuffull:Files";
        public string MusicRootDirectory { get; set; } = string.Empty;
        public string SongImportDirectory { get; set; } = string.Empty;
        public string ManualSongImportDirectory { get; set; } = string.Empty;
        public string FailedImportDirectory { get; set; } = string.Empty;
        public string SavedAiResponsesDirectory { get; set; } = string.Empty;
        public string GenresFile { get; set; } = string.Empty;
        public string AlbumArtDirectory { get; set; } = string.Empty;
    }
}
