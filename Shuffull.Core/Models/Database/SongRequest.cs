using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// A user asking for a song the library doesn't have (Shuffull#57). The row is the user's intent, written in its own
/// transaction by SongRequestsController.Create. The producer polls the <see cref="SongRequestStatus.Pending"/> rows with its
/// shared secret, acks its progress conditionally, and completes the request by importing the song with
/// <c>SongImportDetails.SongRequestId</c> set: the same durable row + poll + ack shape as <see cref="YoutubeRatingRequest"/>.
///
/// <para>At most one open request per user and song: a unique index on (<see cref="UserId"/>,
/// <see cref="ExternalSongId"/>) filtered to the open statuses (<see cref="ShuffullContext"/>). Completed and cancelled
/// rows stay as history, so the same song can be asked for again later.</para>
///
/// <para><see cref="SongId"/> deliberately has no foreign key: deleting the requests playlist purges the songs nobody
/// kept, and a finished request should survive that as a record rather than block it or vanish with it.</para>
/// </summary>
[Index(nameof(Status), nameof(CreatedAt))]
[Index(nameof(UserId), nameof(CreatedAt))]
public class SongRequest
{
    public const int MaxExternalSongIdLength = 64;
    public const int MaxTitleLength = 300;
    public const int MaxThumbnailUrlLength = 2048;
    public const int MaxFailureReasonLength = 1000;

    [Key]
    public string SongRequestId { get; set; }

    [Required]
    public string UserId { get; set; }

    /// <summary>The source's id for the song; what the import will carry as <see cref="Song.ExternalSongId"/>.</summary>
    [Required, MaxLength(MaxExternalSongIdLength)]
    public string ExternalSongId { get; set; }

    /// <summary>What the user picked from the search result, so the producer can name the song without a lookup.</summary>
    [Required, MaxLength(MaxTitleLength)]
    public string Title { get; set; }

    /// <summary>JSON array of artist names, in credit order.</summary>
    [Required]
    public string ArtistsJson { get; set; } = "[]";

    [MaxLength(MaxThumbnailUrlLength)]
    public string? ThumbnailUrl { get; set; }

    public int? DurationSeconds { get; set; }

    /// <summary>
    /// The user's requests playlist for the week the request was made in (<c>SongRequestRules.PlaylistName</c>). Fixed
    /// at creation and handed to the producer, so the instant path and the import put the song in the same playlist
    /// even if the import lands in a later week.
    /// </summary>
    [Required]
    public string TargetPlaylistName { get; set; }

    [Required]
    public SongRequestStatus Status { get; set; } = SongRequestStatus.Pending;

    /// <summary>The producer's free-text reason, sent with a Retrying or Failed ack. Cleared when it moves on.</summary>
    [MaxLength(MaxFailureReasonLength)]
    public string? FailureReason { get; set; }

    /// <summary>The song that fulfilled the request, once <see cref="SongRequestStatus.Completed"/>.</summary>
    public string? SongId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }
}
