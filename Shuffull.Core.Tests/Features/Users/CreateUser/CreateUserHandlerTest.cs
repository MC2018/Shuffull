using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Users.CreateUser;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Tests.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Users.CreateUser;

public class CreateUserHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly FakeAuthTokenGenerator _tokenGenerator;
    private readonly CreateUserHandler _handler;

    public CreateUserHandlerTest()
    {
        _database = new DatabaseFixture();
        _tokenGenerator = new FakeAuthTokenGenerator();
        _handler = new CreateUserHandler(_database.UnitOfWork, _tokenGenerator);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_WithNewUsername_CreatesUserAndReturnsToken()
    {
        // Act
        var result = await _handler.Handle(new CreateUserCommand("alice", "client-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.Equal("alice", response.User.Username);
        Assert.Equal($"token-for-{response.User.UserId}", response.Token);

        var saved = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.Username == "alice");
        Assert.Equal(response.User.UserId, saved.UserId);
        // The raw/client hash is never stored verbatim — only the server-side hash.
        Assert.Equal(Hasher.Argon2Hash("client-hash"), saved.ServerHash);
        Assert.NotEqual("client-hash", saved.ServerHash);
    }

    [Fact]
    public async Task Handle_WhenUsernameTaken_ReturnsError()
    {
        // Arrange
        await _database.Context.Users.AddAsync(new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "alice",
            Version = DateTime.UtcNow,
            ServerHash = "existing-hash",
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new CreateUserCommand("alice", "client-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("already exists", result.GetError().Message, StringComparison.OrdinalIgnoreCase);

        var count = await _database.Context.Users.AsNoTracking().CountAsync(u => u.Username == "alice");
        Assert.Equal(1, count);
    }
}
