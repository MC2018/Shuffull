using Nut.Results;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>API-facing projection of a <see cref="UserDevice"/>: one of the caller's app installs.</summary>
public record UserDeviceDto(string DeviceId, string Name, string? Platform, DateTime CreatedAt, DateTime LastSeenAt)
{
    public static Result<UserDeviceDto> Create(UserDevice device)
    {
        if (device is null)
        {
            return Result.Error<UserDeviceDto>("UserDevice cannot be null.");
        }

        return Result.Ok(new UserDeviceDto(device.DeviceId, device.Name, device.Platform, device.CreatedAt, device.LastSeenAt));
    }
}
