using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Devices.CheckInDevice;

public class CheckInDeviceValidator : AbstractValidator<CheckInDeviceCommand>
{
    public CheckInDeviceValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        // Postgres text can't hold a NUL, and no other control character belongs in an id or a name.
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("A device id is required.")
            .MaximumLength(UserDevice.MaxDeviceIdLength).WithMessage($"The device id is longer than {UserDevice.MaxDeviceIdLength} characters.")
            .Must(NoControlCharacters).WithMessage("The device id can't contain control characters.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A device name is required.")
            .Must(name => name is null || name.Trim().Length <= UserDevice.MaxNameLength)
            .WithMessage($"The device name is longer than {UserDevice.MaxNameLength} characters.")
            .Must(NoControlCharacters).WithMessage("The device name can't contain control characters.");

        RuleFor(x => x.Platform)
            .MaximumLength(Listen.MaxPlatformLength).WithMessage($"The platform is longer than {Listen.MaxPlatformLength} characters.")
            .Must(NoControlCharacters).WithMessage("The platform can't contain control characters.");
    }

    private static bool NoControlCharacters(string? value) => value is null || !value.Any(char.IsControl);
}
