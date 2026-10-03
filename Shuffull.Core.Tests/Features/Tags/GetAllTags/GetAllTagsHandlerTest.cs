using Nut.Results;
using Shuffull.Core.Features.Tags.GetAllTags;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Shared.Enums;

namespace Shuffull.Core.Tests.Features.Tags.GetAllTags;

public class GetAllTagsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetAllTagsHandler _handler;

    public GetAllTagsHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetAllTagsHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedTagAsync(Tag tag)
    {
        await _database.Context.Tags.AddAsync(tag);
        await _database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_WithNoTags_ReturnsEmptyList()
    {
        // Act
        var result = await _handler.Handle(new GetAllTagsQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Empty(result.Get().Tags);
    }

    [Fact]
    public async Task Handle_WithTagsAcrossHierarchy_ReturnsAllOrderedByTypeThenName()
    {
        // Arrange — mix concrete TPH types and unsorted names to exercise the spec's ordering.
        await SeedTagAsync(new Language { TagId = Guid.NewGuid().ToString(), Name = "English" });
        await SeedTagAsync(new Genre { TagId = Guid.NewGuid().ToString(), Name = "Rock" });
        await SeedTagAsync(new Genre { TagId = Guid.NewGuid().ToString(), Name = "Ambient" });

        // Act
        var result = await _handler.Handle(new GetAllTagsQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var tags = result.Get().Tags;
        Assert.Equal(3, tags.Count);

        // Genre (0) before Language (2); within Genre, "Ambient" before "Rock".
        Assert.Equal(TagType.Genre, tags[0].Type);
        Assert.Equal("Ambient", tags[0].Name);
        Assert.Equal(TagType.Genre, tags[1].Type);
        Assert.Equal("Rock", tags[1].Name);
        Assert.Equal(TagType.Language, tags[2].Type);
        Assert.Equal("English", tags[2].Name);
    }
}
