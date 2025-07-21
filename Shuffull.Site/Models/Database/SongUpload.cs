using Shuffull.Site.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Site.Models.Database;

public class SongUpload
{
    [Key]
    public string SongUploadId { get; set; }
    [Required]
    public string Name { get; set; }
    [Required]
    public string UploadFolder { get; set; }
    [Required]
    public string FileType { get; set; }
    [Required]
    public string UserId { get; set; }
    [Required]
    public SongUploadState State { get; private set; } = SongUploadState.ReadyForImporting;
    [Required]
    public DateTime LastUpdatedAt { get; set; }
    public string? PlaylistId { get; set; }

    public string FileName => $"{SongUploadId}{FileType}";
    public string GetFilePath(string songImportDirectory) => Path.Combine(songImportDirectory, UploadFolder, FileName);
    public void SetState(SongUploadState state)
    {
        State = state;
        LastUpdatedAt = DateTime.UtcNow;
    }
}
