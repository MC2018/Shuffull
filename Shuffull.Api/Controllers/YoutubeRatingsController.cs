using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Like-parity queue. When a user's sentiment toward a YouTube-sourced song changes, the site records a pending
/// <see cref="Core.Models.Database.YoutubeRatingRequest"/> (see SetSongLikeStatusHandler). The producer (funnel)
/// pulls the pending rows with the shared secret, applies each via the YouTube Data API <c>videos.rate</c>, then
/// acks them. Producer-only (shared secret) — the app never calls this; it just sets like status.
/// </summary>
[ApiController]
[Route("api/v1/youtube-ratings")]
public class YoutubeRatingsController : ControllerBase
{
    private readonly ShuffullContext _context;
    private readonly IConfiguration _configuration;

    public YoutubeRatingsController(ShuffullContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    private bool IsAuthorized(string? importKey)
    {
        var expectedKey = _configuration["Shuffull:Import:Key"];
        return !string.IsNullOrEmpty(expectedKey) && importKey == expectedKey;
    }

    /// <summary>Producer: pull the rating queue at a given status (defaults to Pending). Shared-secret guarded.</summary>
    [HttpGet]
    public async Task<IActionResult> GetQueue(
        [FromQuery] YoutubeRatingStatus status,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(importKey))
        {
            return Unauthorized();
        }

        var rows = await _context.YoutubeRatingRequests
            .Where(r => r.Status == status)
            .OrderBy(r => r.UpdatedAt)
            .Select(r => new { r.YoutubeRatingRequestId, r.SongId, r.VideoId, r.Rating })
            .ToListAsync(cancellationToken);

        var dtos = rows
            .Select(r => new YoutubeRatingDto(r.YoutubeRatingRequestId, r.SongId, r.VideoId, ToApiRating(r.Rating)))
            .ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Producer: report what happened to each rating. Each item echoes the rating the producer actually pushed;
    /// a row is only resolved if it is still Pending with that exact rating. If the user re-rated during the
    /// producer's poll→apply window (the row is now Pending with a newer value), it is left for the next cycle
    /// rather than being silently resolved with the stale value.
    ///
    /// <para><see cref="YoutubeRatingAckItem.Outcome"/> decides which terminal state: <c>applied</c> (or null,
    /// for older producers) completes the row; <c>unratable</c> marks it Skipped because YouTube will never
    /// accept it — the owner has disabled ratings. ONLY a positively-identified permanent refusal may be sent
    /// as unratable. A transient failure (out of quota, network, expired token) must not be acked at all, so
    /// the row stays Pending and is retried after the daily reset.</para>
    /// </summary>
    [HttpPost("ack")]
    public async Task<IActionResult> Ack(
        [FromBody] YoutubeRatingAckItem[] items,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(importKey))
        {
            return Unauthorized();
        }

        if (items is null || items.Length == 0)
        {
            return Ok();
        }

        var ids = items.Select(i => i.RatingRequestId).ToList();
        var rows = await _context.YoutubeRatingRequests
            .Where(r => ids.Contains(r.YoutubeRatingRequestId) && r.Status == YoutubeRatingStatus.Pending)
            .ToListAsync(cancellationToken);

        var ackById = items
            .GroupBy(i => i.RatingRequestId)
            .ToDictionary(g => g.Key, g => g.Last());

        var now = DateTime.UtcNow;
        var completed = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            if (!ackById.TryGetValue(row.YoutubeRatingRequestId, out var ack) || FromApiRating(ack.Rating) != row.Rating)
            {
                continue;
            }

            // Unknown outcome strings deliberately fall through to Completed rather than erroring: an older
            // producer sends no outcome at all, and treating that as "applied" preserves the original contract.
            // Only the explicit unratable marker is terminal-without-success.
            if (string.Equals(ack.Outcome, Outcomes.Unratable, StringComparison.OrdinalIgnoreCase))
            {
                row.Status = YoutubeRatingStatus.Skipped;
                skipped++;
            }
            else
            {
                row.Status = YoutubeRatingStatus.Completed;
                completed++;
            }

            row.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new YoutubeRatingAckResponse(completed, skipped));
    }

    // The YouTube Data API videos.rate values, so the producer can pass them straight through.
    internal static string ToApiRating(YoutubeRating rating) => rating switch
    {
        YoutubeRating.Like => "like",
        YoutubeRating.Dislike => "dislike",
        _ => "none",
    };

    internal static YoutubeRating FromApiRating(string? rating) => rating?.Trim().ToLowerInvariant() switch
    {
        "like" => YoutubeRating.Like,
        "dislike" => YoutubeRating.Dislike,
        _ => YoutubeRating.None,
    };
}

public record YoutubeRatingDto(string RatingRequestId, string SongId, string VideoId, string Rating);

/// <summary>One producer report. <see cref="Outcome"/> is one of <see cref="Outcomes"/>; null means applied.</summary>
public record YoutubeRatingAckItem(string RatingRequestId, string Rating, string? Outcome = null);

public record YoutubeRatingAckResponse(int Completed, int Skipped = 0);

/// <summary>The stable ack outcome strings on the wire.</summary>
public static class Outcomes
{
    /// <summary>The rating was pushed to YouTube successfully.</summary>
    public const string Applied = "applied";
    /// <summary>YouTube will never accept it (ratings disabled on the video). Terminal — stop retrying.</summary>
    public const string Unratable = "unratable";
}
