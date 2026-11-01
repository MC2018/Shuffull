namespace Shuffull.Api.Commands.Users.Credentials;

/// <summary>The body of a password change: the client's hash of "username;password" for the current and new password.</summary>
public record ChangePasswordRequest(string? CurrentUserHash, string? NewUserHash);
