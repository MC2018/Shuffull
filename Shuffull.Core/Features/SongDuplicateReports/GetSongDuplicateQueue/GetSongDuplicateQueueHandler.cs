using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;

/// <summary>
/// Pending reports that name two songs, grouped by their unordered pair, with what a curator needs to judge them.
///
/// <list type="bullet">
/// <item>A report on one song alone ("different version, hide this one") is a personal hide with no pair to judge,
/// so it never enters the queue.</item>
/// <item>A pair where either song no longer exists is left out: there is nothing left to compare.</item>
/// </list>
/// </summary>
public class GetSongDuplicateQueueHandler(ShuffullContext context)
    : IRequestHandler<GetSongDuplicateQueueQuery, Result<GetSongDuplicateQueueResponse>>
{
    public const int MaxPairs = 200;

    public async Task<Result<GetSongDuplicateQueueResponse>> Handle(GetSongDuplicateQueueQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var open = await context.SongDuplicateReports
                .AsNoTracking()
                .Where(r => r.Status == SongDuplicateReportStatus.Pending && r.OtherSongId != null)
                .ToListAsync(cancellationToken);

            var songIds = open.SelectMany(r => new[] { r.SongId, r.OtherSongId! }).Distinct().ToList();
            var songs = await context.Songs
                .AsNoTracking()
                .Where(s => songIds.Contains(s.SongId))
                .Select(s => new
                {
                    s.SongId,
                    s.Name,
                    s.FileHash,
                    s.ExternalSongId,
                    Artists = s.SongArtists.Select(sa => sa.Artist.Name).ToList(),
                })
                .ToDictionaryAsync(s => s.SongId, cancellationToken);

            var pairs = open
                .Where(r => songs.ContainsKey(r.SongId) && songs.ContainsKey(r.OtherSongId!))
                .GroupBy(r => r.PairKey)
                .OrderBy(g => g.Min(r => r.CreatedAt))
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();
            var page = pairs.Take(MaxPairs).ToList();

            var pageSongIds = page
                .SelectMany(g => new[] { g.First().SongId, g.First().OtherSongId! })
                .Distinct()
                .ToList();
            var likes = await context.UserSongs
                .Where(us => pageSongIds.Contains(us.SongId)
                    && (us.LikeStatus == LikeStatus.Like || us.LikeStatus == LikeStatus.Love))
                .GroupBy(us => us.SongId)
                .Select(g => new { SongId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SongId, x => x.Count, cancellationToken);
            var durations = await context.Listens
                .Where(l => pageSongIds.Contains(l.SongId) && l.SongDurationSeconds != null)
                .GroupBy(l => l.SongId)
                .Select(g => new { SongId = g.Key, Seconds = g.Max(l => l.SongDurationSeconds) })
                .ToDictionaryAsync(x => x.SongId, x => x.Seconds, cancellationToken);

            SongDuplicateQueueSong Describe(string songId)
            {
                var song = songs[songId];
                return new SongDuplicateQueueSong(
                    song.SongId,
                    song.Name,
                    song.Artists,
                    durations.GetValueOrDefault(songId),
                    song.ExternalSongId,
                    likes.GetValueOrDefault(songId));
            }

            var result = page.Select(g =>
            {
                var sample = g.First();
                var (first, second) = string.CompareOrdinal(sample.SongId, sample.OtherSongId) <= 0
                    ? (sample.SongId, sample.OtherSongId!)
                    : (sample.OtherSongId!, sample.SongId);
                var reports = g
                    .OrderBy(r => r.CreatedAt)
                    .ThenBy(r => r.ReportId, StringComparer.Ordinal)
                    .Select(r => new SongDuplicateQueueReport(
                        r.ReportId,
                        r.ReporterUserId,
                        SongDuplicateReportWire.ToWire(r.Kind),
                        r.PreferredSongId,
                        r.Note,
                        DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)))
                    .ToList();
                var firstHash = songs[first].FileHash;
                return new SongDuplicateQueuePair(
                    Describe(first),
                    Describe(second),
                    !string.IsNullOrEmpty(firstHash) && firstHash == songs[second].FileHash,
                    reports.Count,
                    reports);
            }).ToList();

            return Result.Ok(new GetSongDuplicateQueueResponse(result, pairs.Count - page.Count));
        }
        catch (Exception ex)
        {
            return Result.Error<GetSongDuplicateQueueResponse>(
                ResultErrors.FromException(ex, $"Could not load the duplicate queue. {ex.Message}"));
        }
    }
}
