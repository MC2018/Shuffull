using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// A request to re-source a song whose audio quality is poor. Created when a user flags the song in the app;
/// the funnel pulls Pending rows, a human supplies a better link, and the resulting import replaces the song
/// in place (same SongId, all user associations preserved). Global per song — at most one open request per
/// song at a time.
/// </summary>
[Index(nameof(SongId)), Index(nameof(Status))]
public class SongReplacement
{
    [Key]
    public string SongReplacementId { get; set; }
    [Required]
    public string SongId { get; set; }
    [Required]
    public SongReplacementStatus Status { get; set; } = SongReplacementStatus.Pending;

    /// <summary>Optional note from the flagging user (e.g. "muffled / low bitrate").</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Snapshot of the song's ExternalSongId at flag time, so the funnel can supersede the bad source even
    /// after a replacement overwrites the song's ExternalSongId.
    /// </summary>
    public string? OriginalExternalSongId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Song Song { get; set; }
}
