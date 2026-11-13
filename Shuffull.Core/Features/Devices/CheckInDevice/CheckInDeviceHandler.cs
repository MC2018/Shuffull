using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Devices.CheckInDevice;

/// <summary>
/// Upserts the caller's device row in one statement: <c>INSERT ... ON CONFLICT ("UserId", "DeviceId") DO UPDATE</c>.
/// Two check-ins from one install at once (launch and login together) can't fail the key: the second waits for the
/// first, then updates the row it made. <c>CreatedAt</c> is set once; the name, platform and <c>LastSeenAt</c> are
/// the latest check-in's.
/// </summary>
public class CheckInDeviceHandler(ShuffullContext context, TimeProvider timeProvider) : IRequestHandler<CheckInDeviceCommand, Result<UserDeviceDto>>
{
    public async Task<Result<UserDeviceDto>> Handle(CheckInDeviceCommand request, CancellationToken cancellationToken)
    {
        var deviceId = request.DeviceId;
        var name = request.Name.Trim();
        var platform = string.IsNullOrWhiteSpace(request.Platform) ? null : request.Platform;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "UserDevices" ("UserId", "DeviceId", "Name", "Platform", "CreatedAt", "LastSeenAt")
                VALUES ({request.UserId}, {deviceId}, {name}, {platform}, {now}, {now})
                ON CONFLICT ("UserId", "DeviceId") DO UPDATE
                SET "Name" = excluded."Name", "Platform" = excluded."Platform", "LastSeenAt" = excluded."LastSeenAt"
                """,
                cancellationToken);

            var device = await context.UserDevices
                .AsNoTracking()
                .SingleAsync(d => d.UserId == request.UserId && d.DeviceId == deviceId, cancellationToken);
            return UserDeviceDto.Create(device);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Error<UserDeviceDto>(ResultErrors.FromException(ex, $"Could not check in this device. {ex.Message}"));
        }
    }
}
