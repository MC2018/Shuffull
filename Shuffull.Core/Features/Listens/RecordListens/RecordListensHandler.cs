using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Listens.RecordListens;

/// <summary>
/// Records a batch of the caller's listens in one transaction. Each valid listen is inserted with
/// <c>ON CONFLICT ("ListenId") DO NOTHING</c>, so a resend writes nothing, and so does a second copy of the batch
/// sent at the same time: its insert waits for the first copy's transaction, then finds the row and is reported
/// <see cref="ListenOutcomes.Duplicate"/> instead of failing the unique key. A <c>ListenId</c> already used by
/// another user is <see cref="ListenOutcomes.Rejected"/>, and so is a listen that names another user.
/// </summary>
public class RecordListensHandler(ShuffullContext context)
    : IRequestHandler<RecordListensCommand, Result<RecordListensResponse>>
{
    // At least the app outbox's OUTBOX_MAX_BATCH_SIZE, which is what it chunks to.
    public const int MaxBatch = 200;

    // How far ahead of the server's clock a listen may end. A phone's clock drifts a little; a listen dated days
    // ahead would sit in the wrong month of the recap.
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromDays(1);

    public async Task<Result<RecordListensResponse>> Handle(RecordListensCommand request, CancellationToken cancellationToken)
    {
        var items = request.Listens ?? [];
        if (items.Count == 0)
        {
            return Result.Ok(new RecordListensResponse([]));
        }
        if (items.Count > MaxBatch)
        {
            return Result.Error<RecordListensResponse>($"Too many listens in one request ({items.Count}); max {MaxBatch}.");
        }

        var now = DateTime.UtcNow;
        var checkedItems = items.Select(item => Check(item, request.UserId, now)).ToList();

        // In id order, so two overlapping batches can't each hold a row the other is waiting on.
        var toInsert = checkedItems
            .Where(c => c.Listen is not null)
            .Select(c => c.Listen!)
            .DistinctBy(l => l.ListenId)
            .OrderBy(l => l.ListenId, StringComparer.Ordinal)
            .ToList();

        var inserted = new HashSet<string>(StringComparer.Ordinal);
        var otherUsersIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            foreach (var l in toInsert)
            {
                var rows = await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO "Listens" ("ListenId", "UserId", "SongId", "StartedAt", "EndedAt", "SecondsListened",
                                           "SongDurationSeconds", "EndReason", "Context", "PlaylistId", "Platform", "RecordedAt")
                    VALUES ({l.ListenId}, {l.UserId}, {l.SongId}, {l.StartedAt}, {l.EndedAt}, {l.SecondsListened},
                            {l.SongDurationSeconds}, {(int)l.EndReason}, {(int)l.Context}, {l.PlaylistId}, {l.Platform}, {now})
                    ON CONFLICT ("ListenId") DO NOTHING
                    """,
                    cancellationToken);
                if (rows == 1)
                {
                    inserted.Add(l.ListenId);
                }
            }

            var conflicted = toInsert.Select(l => l.ListenId).Where(id => !inserted.Contains(id)).ToList();
            if (conflicted.Count > 0)
            {
                otherUsersIds = (await context.Listens
                    .AsNoTracking()
                    .Where(l => conflicted.Contains(l.ListenId) && l.UserId != request.UserId)
                    .Select(l => l.ListenId)
                    .ToListAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Disposing the transaction rolls back every insert, so the app's outbox can resend the batch whole.
            return Result.Error<RecordListensResponse>(ResultErrors.FromException(ex,
                $"Could not record this batch; no listens were recorded. {ex.Message}"));
        }

        // In request order. Only the first copy of an id in the batch can be the one that was recorded.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var results = checkedItems.Select(c =>
        {
            if (c.Listen is null)
            {
                return new ListenResult(c.Item.ListenId, ListenOutcomes.Rejected, c.Error);
            }

            var id = c.Listen.ListenId;
            if (otherUsersIds.Contains(id))
            {
                return new ListenResult(id, ListenOutcomes.Rejected, "This listen id is already used by another user.");
            }

            return reported.Add(id) && inserted.Contains(id)
                ? new ListenResult(id, ListenOutcomes.Recorded)
                : new ListenResult(id, ListenOutcomes.Duplicate);
        }).ToList();

        return Result.Ok(new RecordListensResponse(results));
    }

    private sealed record CheckedItem(ListenItem Item, Listen? Listen, string? Error);

    /// <summary>The listen to store for <paramref name="item"/>, or why it is rejected.</summary>
    private static CheckedItem Check(ListenItem item, string userId, DateTime now)
    {
        CheckedItem Reject(string error) => new(item, null, error);

        if (string.IsNullOrWhiteSpace(item.ListenId) || item.ListenId.Length > Listen.MaxIdLength)
        {
            return Reject($"A listen id is required, at most {Listen.MaxIdLength} characters.");
        }
        if (item.UserId is not null && item.UserId != userId)
        {
            return Reject("This listen belongs to another user.");
        }
        if (string.IsNullOrWhiteSpace(item.SongId) || item.SongId.Length > Listen.MaxIdLength)
        {
            return Reject($"A song id is required, at most {Listen.MaxIdLength} characters.");
        }
        if (item.StartedAt is not { } startedAt || item.EndedAt is not { } endedAt)
        {
            return Reject("Both startedAt and endedAt are required.");
        }

        startedAt = ToUtc(startedAt);
        endedAt = ToUtc(endedAt);
        if (endedAt < startedAt)
        {
            return Reject("endedAt is before startedAt.");
        }
        if (endedAt > now + MaxClockSkew)
        {
            return Reject("endedAt is in the future.");
        }
        if (item.SecondsListened is not { } secondsListened || !double.IsFinite(secondsListened) || secondsListened < 0)
        {
            return Reject("secondsListened is required and must be zero or more.");
        }
        if (item.SongDurationSeconds is { } duration && (!double.IsFinite(duration) || duration < 0))
        {
            return Reject("songDurationSeconds must be zero or more.");
        }
        if (ParseName<ListenEndReason>(item.EndReason) is not { } endReason)
        {
            return Reject($"Unknown endReason '{item.EndReason}'.");
        }
        if (ParseName<ListenContext>(item.Context) is not { } listenContext)
        {
            return Reject($"Unknown context '{item.Context}'.");
        }
        if (item.PlaylistId is { Length: > Listen.MaxIdLength })
        {
            return Reject($"playlistId is longer than {Listen.MaxIdLength} characters.");
        }
        if (item.Platform is { Length: > Listen.MaxPlatformLength })
        {
            return Reject($"platform is longer than {Listen.MaxPlatformLength} characters.");
        }

        return new CheckedItem(item, new Listen
        {
            ListenId = item.ListenId,
            UserId = userId,
            SongId = item.SongId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            SecondsListened = secondsListened,
            SongDurationSeconds = item.SongDurationSeconds,
            EndReason = endReason,
            Context = listenContext,
            PlaylistId = string.IsNullOrWhiteSpace(item.PlaylistId) ? null : item.PlaylistId,
            Platform = string.IsNullOrWhiteSpace(item.Platform) ? null : item.Platform,
        }, null);
    }

    /// <summary>A time with no offset is taken as UTC, which is what the app sends.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };

    /// <summary>
    /// Matches an enum member by name only, ignoring case. Unlike <c>Enum.TryParse</c> it refuses numbers, so "7"
    /// is rejected rather than stored as a value no member has.
    /// </summary>
    private static T? ParseName<T>(string? value) where T : struct, Enum
        => Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v => string.Equals(v.ToString(), value, StringComparison.OrdinalIgnoreCase));
}
