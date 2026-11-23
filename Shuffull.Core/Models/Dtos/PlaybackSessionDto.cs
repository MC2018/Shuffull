using System.Text.Json;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="PlaybackSession"/> (Shuffull#6). <see cref="EstimatedPositionSeconds"/> is
/// where the song is at the moment the server answered: the stored position plus the time since, while playing.
/// <see cref="Scope"/> is the app's own JSON, returned as it was stored. <see cref="Command"/> is null once the
/// pending command is older than <see cref="PlaybackSession.CommandLifetime"/>.
/// </summary>
public record PlaybackSessionDto(
    string DeviceId,
    string? SongId,
    double PositionSeconds,
    DateTime PositionAt,
    bool IsPlaying,
    double EstimatedPositionSeconds,
    JsonElement? Scope,
    IReadOnlyList<string> History,
    IReadOnlyList<string> QueuedNext,
    DateTime UpdatedAt,
    string? TakenOverBy,
    DateTime? TakenOverAt,
    PlaybackCommandDto? Command)
{
    public static PlaybackSessionDto Create(PlaybackSession session, DateTime now) => new(
        session.DeviceId,
        session.SongId,
        session.PositionSeconds,
        Utc(session.PositionAt),
        session.IsPlaying,
        session.EstimatePosition(now),
        session.Scope is null ? null : JsonSerializer.Deserialize<JsonElement>(session.Scope),
        session.History,
        session.QueuedNext,
        Utc(session.UpdatedAt),
        session.TakenOverBy,
        session.TakenOverAt is { } takenOverAt ? Utc(takenOverAt) : null,
        session is { Command: not null, CommandId: not null, CommandAt: not null }
                && now - session.CommandAt.Value <= PlaybackSession.CommandLifetime
            ? new PlaybackCommandDto(session.CommandId, session.Command, session.CommandSeekSeconds, Utc(session.CommandAt.Value))
            : null);

    /// <summary>
    /// Every stored time is UTC, but SQLite reads one back with no kind, which would go out without its <c>Z</c> and be
    /// read as local time by a client. Npgsql already says UTC for a <c>timestamptz</c>; this makes the answer the same
    /// on both.
    /// </summary>
    internal static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

/// <summary>
/// A remote command waiting for its device to carry it out and ack it. Only shown for
/// <see cref="PlaybackSession.CommandLifetime"/> after it was sent: a press on a remote is only meant for now, so a device
/// that was asleep doesn't act on it when it wakes. The ack of an expired command still clears it.
/// </summary>
/// <param name="Kind">One of <see cref="PlaybackCommandKinds"/>.</param>
/// <param name="SeekSeconds">Only for a seek.</param>
public record PlaybackCommandDto(string CommandId, string Kind, double? SeekSeconds, DateTime At);

/// <summary>
/// One of the caller's devices in the playback listing: its name and platform from its <see cref="UserDevice"/> row,
/// when it was last active (its last check-in or session write, whichever is later), whether it has a live connection
/// to the playback hub, and its session, if it has one.
/// </summary>
public record PlaybackDeviceDto(
    string DeviceId,
    string Name,
    string? Platform,
    DateTime LastActiveAt,
    bool Online,
    PlaybackSessionDto? Session);
