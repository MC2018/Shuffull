using Nut.Results;
using Shuffull.Core.Features.Users.GetCurrentUser;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Users.GetCurrentUser;

public class GetCurrentUserHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetCurrentUserHandler _handler;

    public GetCurrentUserHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetCurrentUserHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = $"user-{Guid.NewGuid():N}",
            Version = DateTime.UtcNow,
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_WithExistingUser_ReturnsUserDto()
    {
        // Arrange
        var user = await SeedUserAsync();

        // Act
        var result = await _handler.Handle(new GetCurrentUserQuery(user.UserId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(user.UserId, result.Get().User.UserId);
        Assert.Equal(user.Username, result.Get().User.Username);
    }

    [Fact]
    public async Task Handle_WithMissingUser_ReturnsError()
    {
        // Act
        var result = await _handler.Handle(new GetCurrentUserQuery(Guid.NewGuid().ToString()), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("User not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }
}
