using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Features.Users.AuthenticateUser;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Tests.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Users.AuthenticateUser;

public class AuthenticateUserHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly FakeAuthTokenGenerator _tokenGenerator;
    private readonly AuthenticateUserHandler _handler;

    public AuthenticateUserHandlerTest()
    {
        _database = new DatabaseFixture();
        _tokenGenerator = new FakeAuthTokenGenerator();
        _handler = new AuthenticateUserHandler(_database.UnitOfWork, _tokenGenerator, NullLogger<AuthenticateUserHandler>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(string username, string clientHash)
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = username,
            Version = DateTime.UtcNow,
        };
        ServerPasswordHash.Set(user, clientHash);
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    /// <summary>An account made before per-user salts: hashed with the shared salt, no salt of its own.</summary>
    private async Task<User> SeedLegacyUserAsync(string username, string clientHash)
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = username,
            Version = DateTime.UtcNow,
            ServerHash = Hasher.Argon2Hash(clientHash),
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<User> ReloadAsync(string userId)
        => await _database.CreateContext().Users.AsNoTracking().SingleAsync(u => u.UserId == userId);

    [Fact]
    public async Task Handle_WithCorrectCredentials_ReturnsUserAndToken()
    {
        // Arrange
        var user = await SeedUserAsync("alice", "client-hash");

        // Act
        var result = await _handler.Handle(new AuthenticateUserCommand("alice", "client-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.Equal(user.UserId, response.User.UserId);
        Assert.Equal("alice", response.User.Username);
        Assert.Equal($"token-for-{user.UserId}", response.Token);
        Assert.Equal(FakeAuthTokenGenerator.FixedExpiration, response.Expiration);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ReturnsError()
    {
        // Arrange
        await SeedUserAsync("alice", "client-hash");

        // Act
        var result = await _handler.Handle(new AuthenticateUserCommand("alice", "wrong-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("incorrect", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithUnknownUsername_ReturnsError()
    {
        // Act
        var result = await _handler.Handle(new AuthenticateUserCommand("nobody", "client-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("incorrect", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_LegacyAccount_LogsInAndMovesToItsOwnSalt()
    {
        // Arrange
        var user = await SeedLegacyUserAsync("alice", "client-hash");
        var legacyHash = user.ServerHash;

        // Act
        var result = await _handler.Handle(new AuthenticateUserCommand("alice", "client-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var saved = await ReloadAsync(user.UserId);
        Assert.NotNull(saved.ServerHashSalt);
        Assert.NotEqual(legacyHash, saved.ServerHash);
        Assert.True(ServerPasswordHash.Verify(saved, "client-hash"));

        // The re-hashed account still logs in, and still refuses a wrong password.
        Assert.True((await _handler.Handle(new AuthenticateUserCommand("alice", "client-hash"), CancellationToken.None)).IsOk);
        Assert.True((await _handler.Handle(new AuthenticateUserCommand("alice", "wrong-hash"), CancellationToken.None)).IsError);
    }

    [Fact]
    public async Task Handle_LegacyAccountWithWrongPassword_IsNotRehashed()
    {
        // Arrange
        var user = await SeedLegacyUserAsync("alice", "client-hash");

        // Act
        var result = await _handler.Handle(new AuthenticateUserCommand("alice", "wrong-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        var saved = await ReloadAsync(user.UserId);
        Assert.Null(saved.ServerHashSalt);
        Assert.Equal(user.ServerHash, saved.ServerHash);
    }

    [Fact]
    public async Task Handle_SameUsernameTwice_LogsIntoTheAccountWhosePasswordMatches()
    {
        // Arrange — usernames aren't unique in the schema, so login must try each account with that name.
        await SeedUserAsync("alice", "first-hash");
        var second = await SeedUserAsync("alice", "second-hash");

        // Act
        var result = await _handler.Handle(new AuthenticateUserCommand("alice", "second-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(second.UserId, result.Get().User.UserId);
    }
}
