using Nut.Results;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="User"/>. Deliberately omits <see cref="User.ServerHash"/>
/// so the password hash never leaves the server. Built through the static <see cref="Create"/>
/// factory like the other DTOs.
/// </summary>
public record UserDto(string UserId, string Username, DateTime Version, bool IsCurator)
{
    public static Result<UserDto> Create(User user)
    {
        if (user is null)
        {
            return Result.Error<UserDto>("User cannot be null.");
        }

        return Result.Ok(new UserDto(
            UserId: user.UserId,
            Username: user.Username,
            Version: user.Version,
            // Lets the app show curator-only UI (e.g. the song-edit affordance) to curators only.
            IsCurator: user.IsCurator));
    }
}
