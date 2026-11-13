using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Playback;

/// <summary>Shared rules for the playback slices.</summary>
public static class PlaybackRules
{
    /// <summary>
    /// The server's clock, cut to whole milliseconds. A takeover sends back the <c>updatedAt</c> it read for an equality
    /// check, and a JavaScript <c>Date</c> holds only milliseconds, so a finer stored value could never be matched by an
    /// app that parses timestamps into Dates. Postgres keeps microseconds, so a millisecond value is stored exactly.
    /// </summary>
    public static DateTime Now(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
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
