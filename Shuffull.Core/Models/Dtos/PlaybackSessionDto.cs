using System.Text.Json;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="PlaybackSession"/> (Shuffull#6). <see cref="EstimatedPositionSeconds"/> is
/// where the song is at the moment the server answered: the stored position plus the time since, while playing.
/// <see cref="Scope"/> is the app's own JSON, returned as it was stored.
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
        session.PositionAt,
        session.IsPlaying,
        session.EstimatePosition(now),
        session.Scope is null ? null : JsonDocument.Parse(session.Scope).RootElement.Clone(),
        session.History,
        session.QueuedNext,
        session.UpdatedAt,
        session.TakenOverBy,
        session.TakenOverAt,
        session is { Command: not null, CommandId: not null, CommandAt: not null }
            ? new PlaybackCommandDto(session.CommandId, session.Command, session.CommandSeekSeconds, session.CommandAt.Value)
            : null);
}

/// <summary>A remote command waiting for its device to carry it out and ack it.</summary>
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
