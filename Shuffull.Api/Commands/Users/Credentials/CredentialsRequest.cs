namespace Shuffull.Api.Commands.Users.Credentials;

/// <summary>The body of login and sign-up: a username and the client's hash of "username;password".</summary>
public record CredentialsRequest(string? Username, string? UserHash);
