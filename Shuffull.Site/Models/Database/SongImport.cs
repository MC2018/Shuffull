using Nut.Results;
using Shuffull.Site.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Site.Models.Database;

public class SongImport
{
    [Key]
    public string SongImportId { get; set; }
    [Required]
    public string Name { get; set; }
    [Required]
    public string ImportFolder { get; set; }
    [Required]
    public string FileType { get; set; }
    [Required]
    public string UserId { get; set; }
    [Required]
    public SongImportState State { get; private set; } = SongImportState.ReadyForImporting;
    [Required]
    public ExternalSource ExternalSource { get; set; }
    public string? PlaylistId { get; set; }
    public string? ExternalSongId { get; set; }
    public string? ExternalPlaylistId { get; set; }
    public string? SongId { get; set; }
    [Required]
    public DateTime LastUpdatedAt { get; set; }

    public Song? Song { get; set; }

    public string FileName => $"{SongImportId}{FileType}";
    public string GetImportFolderPath(string songImportDirectory) => Path.Combine(songImportDirectory, ImportFolder);
    public string GetFilePath(string songImportDirectory) => Path.Combine(songImportDirectory, ImportFolder, FileName);

    public Result SetState(SongImportState state)
    {
        if (State != SongImportState.ReadyForImporting)
        {
            return Result.Error("Cannot set state from current state.");
        }

        State = state;
        LastUpdatedAt = DateTime.UtcNow;
        return Result.Ok();
    }
}
