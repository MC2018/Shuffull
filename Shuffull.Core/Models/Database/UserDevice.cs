using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// One app install of one user: a stable id the app makes on first launch, a name the user can change, and when the
/// app was last open (Shuffull#132). Written by the app's check-in, <c>PUT /api/v1/devices/{deviceId}</c>. Keyed by
/// (<see cref="UserId"/>, <see cref="DeviceId"/>), so an install signed into two accounts is two rows, and no user can
/// write another's.
///
/// <para>The id is a label, never a credential: knowing it grants nothing. Rows are never deleted, because playback
/// sessions (Shuffull#6) point at them; a listing hides the ones that have gone quiet instead.</para>
/// </summary>
public class UserDevice
{
    public const int MaxDeviceIdLength = 64;
    public const int MaxNameLength = 64;

    [Required]
    public string UserId { get; set; }

    [Required, MaxLength(MaxDeviceIdLength)]
    public string DeviceId { get; set; }

    /// <summary>What the user calls it ("Pixel 8", "Desktop"). The latest check-in's name wins.</summary>
    [Required, MaxLength(MaxNameLength)]
    public string Name { get; set; }

    /// <summary>The app's platform as it reports it, the same free text as <see cref="Listen.Platform"/>.</summary>
    [MaxLength(Listen.MaxPlatformLength)]
    public string? Platform { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    /// <summary>When the app last checked in: at launch, after login, after a rename, or on coming to the foreground.</summary>
    [Required]
    public DateTime LastSeenAt { get; set; }

    public User User { get; set; }
}
