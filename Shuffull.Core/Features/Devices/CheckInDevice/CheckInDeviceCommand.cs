using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Devices.CheckInDevice;

/// <summary>
/// The app saying "this install is open": creates the caller's <c>UserDevice</c> row for <paramref name="DeviceId"/>
/// the first time, then updates its name and platform and moves <c>LastSeenAt</c> to now (Shuffull#132). Scoped to
/// <paramref name="UserId"/>, the signed-in user, so nobody can write another user's device.
/// </summary>
/// <param name="Name">What the user calls the device. Trimmed; the latest check-in's name wins. A body without one
/// arrives as empty and fails validation.</param>
/// <param name="Platform">Optional free text, as on a listen (android, ios, web, desktop).</param>
public record CheckInDeviceCommand(string UserId, string DeviceId, string Name, string? Platform)
    : IRequest<Result<UserDeviceDto>>;
