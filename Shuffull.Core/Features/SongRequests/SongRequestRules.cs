using System.Globalization;
using Shuffull.Core.Models.Enums;
using Shuffull.Metadata.Enums;

namespace Shuffull.Core.Features.SongRequests;

/// <summary>
/// The song request state machine, kept in one place so the app endpoints, the producer ack and the import agree on it.
/// </summary>
public static class SongRequestRules
{
    /// <summary>
    /// Statuses that count as open: the user's request is still alive, so a second one for the same song is refused.
    /// The filtered unique index on SongRequests is built from this list (ShuffullContext), so the two can't drift.
    /// </summary>
    public static readonly SongRequestStatus[] Open =
    [
        SongRequestStatus.Pending,
        SongRequestStatus.Accepted,
        SongRequestStatus.Downloading,
        SongRequestStatus.Retrying,
        SongRequestStatus.Failed,
    ];

    /// <summary>
    /// The states a request the producer has claimed moves through before it completes or fails. Only an ack moves a
    /// request out of them (<see cref="AckMayFollow"/>), so the API watches them for a producer that lost one
    /// (<c>SongRequestBacklogCheck</c>).
    /// </summary>
    public static readonly SongRequestStatus[] Claimed =
    [
        SongRequestStatus.Accepted,
        SongRequestStatus.Downloading,
        SongRequestStatus.Retrying,
    ];

    /// <summary>
    /// The states an ack of <paramref name="ack"/> may move a request out of. A request already AT that status is
    /// included, so a re-sent ack applies. Empty for
    /// <see cref="SongRequestAckStatus.Unknown"/> and any undefined value, which are refused.
    /// <list type="bullet">
    /// <item><c>Accepted</c> follows only <c>Pending</c>: it is the producer's claim, and the one ack that finds out a
    /// request was cancelled.</item>
    /// <item><c>Downloading</c>, <c>Retrying</c> and <c>Failed</c> follow a claimed request, never a <c>Pending</c> one,
    /// so a producer can't skip past a cancel.</item>
    /// </list>
    /// No ack moves a request out of <c>Completed</c> or <c>Cancelled</c>.
    /// </summary>
    public static SongRequestStatus[] AckMayFollow(SongRequestAckStatus ack) => ack switch
    {
        SongRequestAckStatus.Accepted => [SongRequestStatus.Pending, SongRequestStatus.Accepted],
        SongRequestAckStatus.Downloading => [SongRequestStatus.Accepted, SongRequestStatus.Downloading, SongRequestStatus.Retrying],
        SongRequestAckStatus.Retrying => [SongRequestStatus.Accepted, SongRequestStatus.Downloading, SongRequestStatus.Retrying],
        SongRequestAckStatus.Failed => [SongRequestStatus.Accepted, SongRequestStatus.Downloading, SongRequestStatus.Retrying, SongRequestStatus.Failed],
        _ => [],
    };

    /// <summary>The request status an ack sets. The numbers are shared, so this is a cast once the ack is known to be defined.</summary>
    public static SongRequestStatus ToStatus(SongRequestAckStatus ack) => (SongRequestStatus)(int)ack;

    /// <summary>
    /// What every weekly requests playlist's name starts with. A filtered unique index on Playlists (ShuffullContext)
    /// allows one such playlist per user and name, so the instant path and an import racing to create the week's
    /// playlist can't make two: the loser fails, and its retry finds the winner's.
    /// </summary>
    public const string PlaylistNamePrefix = "Requests · week of ";

    /// <summary>
    /// The user's requests playlist for the week containing <paramref name="utcNow"/>: <c>"Requests · week of
    /// 2026-11-02"</c>, named for that week's Monday, in UTC. The API owns this name. The instant path and the
    /// producer's import (which passes it through from the request) both resolve the playlist by it, so they can't
    /// disagree.
    /// </summary>
    public static string PlaylistName(DateTime utcNow)
    {
        var date = utcNow.Date;
        var sinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-sinceMonday);
        return $"{PlaylistNamePrefix}{monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
    }
}
