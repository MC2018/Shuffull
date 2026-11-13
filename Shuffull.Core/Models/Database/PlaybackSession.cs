using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// What one device is playing right now (Shuffull#6, epic shuffull-workspace#134): one row per
/// <see cref="UserDevice"/>, keyed the same way and overwritten in place, never a log. It is the truth a handoff reads:
/// the device taking over loads the song, position, scope, Previous history and play-next list from here. The live
/// channel (<c>/hubs/playback</c>) only says "this row changed"; clients re-read it.
///
/// <para>Who writes what:</para>
/// <list type="bullet">
/// <item>The device itself, with <c>PUT /api/v1/playback/sessions/{deviceId}</c>: song, position, playing, scope,
/// history and queue. Last write wins. That PUT never touches the takeover or command fields, so a stale PUT that
/// lands after a takeover can't erase it; the PUT's answer carries the takeover back to the device instead.</item>
/// <item>Another of the user's devices, with <c>.../takeover</c>: <see cref="TakenOverBy"/> and
/// <see cref="TakenOverAt"/>, and the row is paused at its estimated position. Conditional on <see cref="UpdatedAt"/>.
/// <see cref="TakenOverAt"/> doubles as the takeover's id: a device acts on a takeover it hasn't seen before.</item>
/// <item>Any of the user's devices, with <c>.../command</c>: a pending remote command for this device, which it clears
/// with <c>.../command/{commandId}/ack</c>. A newer command replaces an unacked one.</item>
/// </list>
///
/// <para>Rows not updated in <see cref="RetentionPeriod"/> are pruned; the <see cref="UserDevice"/> row stays.</para>
/// </summary>
public class PlaybackSession
{
    /// <summary>The Previous history a session carries: the last songs played, newest last.</summary>
    public const int MaxHistory = 25;

    /// <summary>The play-next songs a session carries.</summary>
    public const int MaxQueuedNext = 100;

    /// <summary>The app's song filters, as compact JSON.</summary>
    public const int MaxScopeLength = 8192;

    public const int MaxCommandIdLength = 64;
    public const int MaxCommandLength = 16;

    /// <summary>A row not updated for this long is deleted, and a device inactive this long is left out of the listing.</summary>
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    [Required]
    public string UserId { get; set; }

    [Required, MaxLength(UserDevice.MaxDeviceIdLength)]
    public string DeviceId { get; set; }

    /// <summary>The current song, or null when nothing is loaded. No FK, like <see cref="Listen.SongId"/>: a purged
    /// song must not fail a device's state write.</summary>
    [MaxLength(Listen.MaxIdLength)]
    public string? SongId { get; set; }

    /// <summary>Where the song was at <see cref="PositionAt"/>.</summary>
    public double PositionSeconds { get; set; }

    /// <summary>When <see cref="PositionSeconds"/> was measured: the server's clock when the state arrived, never the
    /// device's, so an estimate never depends on two clocks agreeing.</summary>
    public DateTime PositionAt { get; set; }

    public bool IsPlaying { get; set; }

    /// <summary>The app's song filters (which songs it shuffles from), as JSON the server stores and returns verbatim.</summary>
    [MaxLength(MaxScopeLength)]
    public string? Scope { get; set; }

    /// <summary>The last song ids played, newest last, at most <see cref="MaxHistory"/>: what Previous walks back through.</summary>
    public List<string> History { get; set; } = [];

    /// <summary>The play-next song ids, in order, at most <see cref="MaxQueuedNext"/>.</summary>
    public List<string> QueuedNext { get; set; } = [];

    /// <summary>When the device's state or a takeover last changed this row. A takeover must name the value it read.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>The device that took playback over from this one, or null.</summary>
    [MaxLength(UserDevice.MaxDeviceIdLength)]
    public string? TakenOverBy { get; set; }

    public DateTime? TakenOverAt { get; set; }

    /// <summary>A pending remote command (<see cref="PlaybackCommandKinds"/>), or null.</summary>
    [MaxLength(MaxCommandLength)]
    public string? Command { get; set; }

    /// <summary>The sender's id for <see cref="Command"/>. The target clears the command by naming it, so an ack can
    /// never clear a newer command it hasn't seen.</summary>
    [MaxLength(MaxCommandIdLength)]
    public string? CommandId { get; set; }

    /// <summary>Where to seek to, for a <see cref="PlaybackCommandKinds.Seek"/>.</summary>
    public double? CommandSeekSeconds { get; set; }

    public DateTime? CommandAt { get; set; }

    public UserDevice Device { get; set; }

    /// <summary>Where the song is now: the stored position, plus the time since it was measured while playing.</summary>
    public double EstimatePosition(DateTime now)
        => IsPlaying ? PositionSeconds + Math.Max(0, (now - PositionAt).TotalSeconds) : PositionSeconds;
}

/// <summary>The remote commands a device can send another (Shuffull#6).</summary>
public static class PlaybackCommandKinds
{
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Seek = "seek";

    public static readonly IReadOnlySet<string> All = new HashSet<string>([Play, Pause, Next, Previous, Seek]);
}
