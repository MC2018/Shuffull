using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// A pending "mirror this rating onto YouTube" request — the like-parity analog of <see cref="SongReplacement"/>.
/// Created/updated whenever a user's <see cref="LikeStatus"/> toward a YouTube-sourced song changes (the song
/// carries the video id in <see cref="Song.ExternalSongId"/>). The producer (funnel) polls the Pending rows with
/// its shared secret, calls the YouTube Data API <c>videos.rate</c>, then acks them Completed.
///
/// <para>At most one open (Pending) row per <see cref="VideoId"/>: a later rating change updates the open row in
/// place (latest-wins), so a rapid like → un-like → like collapses to a single call.</para>
/// </summary>
[Index(nameof(VideoId)), Index(nameof(Status))]
public class YoutubeRatingRequest
{
    [Key]
    public string YoutubeRatingRequestId { get; set; }

    [Required]
    public string SongId { get; set; }

    /// <summary>The YouTube video id to rate (snapshot of the song's <see cref="Song.ExternalSongId"/>).</summary>
    [Required]
    public string VideoId { get; set; }

    /// <summary>The rating to apply on YouTube (mirrors the user's current sentiment).</summary>
    [Required]
    public YoutubeRating Rating { get; set; }

    [Required]
    public YoutubeRatingStatus Status { get; set; } = YoutubeRatingStatus.Pending;

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Song Song { get; set; }
}
