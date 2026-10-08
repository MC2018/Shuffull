using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// The record left behind when a song row is purged (today: an un-kept audition song, when its exploratory
/// playlist is deleted). Written in the same transaction as the delete, so a purge is never silent.
/// <para>
/// It does two jobs. First, it holds the media back: the purge does not delete the audio/album-art file. A sweep
/// deletes it only after <see cref="MediaGracePeriod"/>, and only when no live song shares
/// <see cref="FileHash"/>. That leaves time to recover a wrong purge from the file and these columns (on
/// 2026-08-22, 34 kept songs had their media hard-deleted on the spot). Second, it is the deletion signal for
/// clients, which otherwise only ever see songs being added.
/// </para>
/// </summary>
[Index(nameof(DeletedAt))]
[Index(nameof(FileHash))]
public class SongTombstone
{
    /// <summary>How long a purged song's media is kept before the sweep may delete it.</summary>
    public static readonly TimeSpan MediaGracePeriod = TimeSpan.FromDays(7);

    [Key]
    public string SongId { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? ExternalSongId { get; set; }
    [Required]
    public string FileHash { get; set; } = string.Empty;
    [Required]
    public string FileExtension { get; set; } = string.Empty;
    /// <summary>The user whose action caused the purge.</summary>
    [Required]
    public string DeletedByUserId { get; set; }
    /// <summary>The audition playlist whose deletion purged it.</summary>
    public string? PlaylistId { get; set; }
    [Required]
    public DateTime DeletedAt { get; set; }
    /// <summary>When the media sweep handled this row; null while it is still inside the grace window.</summary>
    public DateTime? MediaSweptAt { get; set; }
    /// <summary>
    /// Whether that sweep deleted the files. False after a sweep means the file was left in place: a live song or
    /// a newer tombstone shares the hash, or the delete kept failing and was abandoned.
    /// </summary>
    public bool MediaDeleted { get; set; }
    /// <summary>
    /// How many sweep passes failed to delete the media. Failed rows are retried behind fresh ones, and abandoned
    /// once this reaches the sweep's attempt cap.
    /// </summary>
    public int MediaSweepFailures { get; set; }
}
