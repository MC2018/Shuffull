namespace Shuffull.Api.Commands.Devices.CheckInDevice;

/// <summary>Body for a device check-in: the install's current name and the app's platform (Shuffull#132).</summary>
public record CheckInDeviceRequest(string? Name, string? Platform);
