using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// A curator merged two copies of one song (Shuffull#142): <see cref="LoserSongId"/> was folded into
/// <see cref="SurvivorSongId"/> and deleted. Written in the merge's transaction, and never changed afterwards.
/// <para>
/// It does two jobs. Devices page these rows (<c>GET /api/v1/song-merges</c>) to swap the loser's id for the
/// survivor's in their local copy and their outbox. And the importer looks the loser's source up here
/// (<see cref="LoserExternalSongId"/>, <see cref="LoserFileHash"/>), so a re-import of the losing upload lands on the
/// survivor instead of bringing the duplicate back.
/// </para>
/// <para>
/// The survivor can itself be merged away later. The rows are left as they were, so a reader follows the chain
/// (<c>SongMergeAliases</c>) to the song that is live now.
/// </para>
/// </summary>
[Index(nameof(Version), nameof(LoserSongId))]
[Index(nameof(SurvivorSongId))]
[Index(nameof(LoserExternalSongId))]
[Index(nameof(LoserFileHash))]
public class SongMerge
{
    [Key, MaxLength(SongDuplicateReport.MaxIdLength)]
    public string LoserSongId { get; set; }

    /// <summary>The song the loser became. A plain column: the survivor can be deleted later, and this row stays.</summary>
    [Required, MaxLength(SongDuplicateReport.MaxIdLength)]
    public string SurvivorSongId { get; set; }

    /// <summary>The loser's source id (a YouTube video id), when it had one.</summary>
    public string? LoserExternalSongId { get; set; }

    /// <summary>The loser's audio hash. A manual upload has no source id, so this is how its re-upload is caught.</summary>
    [Required]
    public string LoserFileHash { get; set; } = string.Empty;

    /// <summary>The curator who merged them.</summary>
    [Required]
    public string MergedByUserId { get; set; }

    [Required]
    public DateTime MergedAt { get; set; }

    /// <summary>Paging cursor for the feed, with <see cref="LoserSongId"/> as the tie-breaker.</summary>
    [Required]
    public DateTime Version { get; set; }
}
