using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Playback;

/// <summary>
/// The doorbell for playback sessions (Shuffull#6): after a session row changes, tells the user's other connected
/// devices which device's row it was, so they re-read it. It carries no state. The row is the truth, and a device whose
/// connection is down catches up on its next sync, so a lost signal costs latency, never data.
/// Implemented over SignalR in Shuffull.Api.
/// </summary>
public interface IPlaybackNotifier
{
    /// <summary>
    /// Sends "session changed: <paramref name="deviceId"/>" to every connection of <paramref name="userId"/> except
    /// those of <paramref name="writerDeviceId"/>, the device that made the change (null = nobody is left out).
    /// Never throws: a failed signal is logged, and the write it follows has already committed.
    /// </summary>
    Task SessionChangedAsync(string userId, string deviceId, string? writerDeviceId);
}

/// <summary>Which of a user's devices have a live connection to the playback hub right now. Held in memory.</summary>
public interface IPlaybackPresence
{
    IReadOnlySet<string> OnlineDeviceIds(string userId);
}

/// <summary>Shared rules for the playback slices.</summary>
public static class PlaybackRules
{
    /// <summary>
    /// The server's clock, cut to whole microseconds. Postgres keeps microseconds, so a timestamp handed to a client
    /// is exactly what is stored, and a takeover can send it back for an equality check.
    /// </summary>
    public static DateTime Now(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    /// <summary>
    /// A timestamp a client sent back, as UTC. One with an offset is converted; one without is taken as UTC, since every
    /// timestamp the server hands out is UTC. Npgsql refuses a non-UTC value for a <c>timestamptz</c> column.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };

    /// <summary>Postgres text can't hold a NUL, and no other control character belongs in an id.</summary>
    public static bool NoControlCharacters(string? value) => value is null || !value.Any(char.IsControl);

    /// <summary>The rules for a device id in a playback route or body, the same as a device check-in's.</summary>
    public static IRuleBuilderOptions<T, string?> ValidDeviceId<T>(this IRuleBuilder<T, string?> rule, string what = "device id")
        => rule
            .NotEmpty().WithMessage($"A {what} is required.")
            .MaximumLength(UserDevice.MaxDeviceIdLength).WithMessage($"The {what} is longer than {UserDevice.MaxDeviceIdLength} characters.")
            .Must(NoControlCharacters).WithMessage($"The {what} can't contain control characters.");
}
