using Nut.Results;
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
        _handler = new AuthenticateUserHandler(_database.UnitOfWork, _tokenGenerator);
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
            // Stored hash mirrors what the handler computes from the client-supplied hash.
            ServerHash = Hasher.Argon2Hash(clientHash),
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

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
}
