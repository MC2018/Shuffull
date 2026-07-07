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
    /// Producer: mark ratings applied. Each item echoes the rating the producer actually pushed; a row is only
    /// completed if it is still Pending with that exact rating. If the user re-rated during the producer's
    /// poll→apply window (the row is now Pending with a newer value), it is left for the next cycle rather than
    /// being silently completed with the stale value.
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

        var appliedById = items
            .GroupBy(i => i.RatingRequestId)
            .ToDictionary(g => g.Key, g => FromApiRating(g.Last().Rating));

        var now = DateTime.UtcNow;
        var completed = 0;
        foreach (var row in rows)
        {
            if (appliedById.TryGetValue(row.YoutubeRatingRequestId, out var applied) && applied == row.Rating)
            {
                row.Status = YoutubeRatingStatus.Completed;
                row.UpdatedAt = now;
                completed++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new YoutubeRatingAckResponse(completed));
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
public record YoutubeRatingAckItem(string RatingRequestId, string Rating);
public record YoutubeRatingAckResponse(int Completed);
