using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Users.ChangePassword;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Users.ChangePassword;

public class ChangePasswordHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly FakeAuthTokenGenerator _tokenGenerator;
    private readonly ChangePasswordHandler _handler;

    public ChangePasswordHandlerTest()
    {
        _database = new DatabaseFixture();
        _tokenGenerator = new FakeAuthTokenGenerator();
        _handler = new ChangePasswordHandler(_database.UnitOfWork, _tokenGenerator);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(string clientHash)
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "alice",
            Version = DateTime.UtcNow,
        };
        ServerPasswordHash.Set(user, clientHash);
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<User> ReloadAsync(string userId)
        => await _database.CreateContext().Users.AsNoTracking().SingleAsync(u => u.UserId == userId);

    [Fact]
    public async Task Handle_WithTheCurrentPassword_StoresTheNewOneAndANewStamp()
    {
        // Arrange
        var user = await SeedUserAsync("old-hash");

        // Act
        var result = await _handler.Handle(new ChangePasswordCommand(user.UserId, "old-hash", "new-hash"), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var saved = await ReloadAsync(user.UserId);
        Assert.True(ServerPasswordHash.Verify(saved, "new-hash"));
        Assert.False(ServerPasswordHash.Verify(saved, "old-hash"));
        Assert.NotNull(saved.SecurityStamp);

        // The fresh session is issued after the stamp moved, so it carries the new one.
        Assert.Equal($"token-for-{user.UserId}", result.Get().Token);
        Assert.Equal(saved.SecurityStamp, _tokenGenerator.LastUser!.SecurityStamp);
    }

    [Fact]
    public async Task Handle_EachChange_GetsADifferentStamp()
    {
        // Arrange
        var user = await SeedUserAsync("first-hash");
        await _handler.Handle(new ChangePasswordCommand(user.UserId, "first-hash", "second-hash"), CancellationToken.None);
        var firstStamp = (await ReloadAsync(user.UserId)).SecurityStamp;

        // Act
        var result = await _handler.Handle(new ChangePasswordCommand(user.UserId, "second-hash", "third-hash"), CancellationToken.None);

        // Assert — so a token from between the two changes stops working too.
        Assert.True(result.IsOk);
        Assert.NotEqual(firstStamp, (await ReloadAsync(user.UserId)).SecurityStamp);
    }

    [Fact]
    public async Task Handle_WithAWrongCurrentPassword_ChangesNothing()
    {
        // Arrange
        var user = await SeedUserAsync("old-hash");

        // Act
        var result = await _handler.Handle(new ChangePasswordCommand(user.UserId, "wrong-hash", "new-hash"), CancellationToken.None);

        // Assert — an Invalid (400), never a 401 the app would log out on.
        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.Invalid, ResultErrors.KindOf(result.GetError()));
        Assert.Contains("incorrect", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
        var saved = await ReloadAsync(user.UserId);
        Assert.True(ServerPasswordHash.Verify(saved, "old-hash"));
        Assert.Null(saved.SecurityStamp);
        Assert.Null(_tokenGenerator.LastUser);
    }

    [Fact]
    public async Task Handle_UnknownUser_IsNotFound()
    {
        var result = await _handler.Handle(new ChangePasswordCommand("no-such-user", "old-hash", "new-hash"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(result.GetError()));
    }
}
