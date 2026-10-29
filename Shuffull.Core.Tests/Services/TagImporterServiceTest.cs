using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Models;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The startup genre import adds the genres.json names the database lacks and relates them. A genre the tagging
/// paths created on their own has no relation yet, and the import once treated it as missing and created a second
/// tag of the same name, which songs then got linked to twice (Shuffull#43; four such pairs in prod).
/// </summary>
public class TagImporterServiceTest : IDisposable
{
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private TagImporterService Importer(GenresFile genres)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.DownloadSerializableObjectAsync<GenresFile>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(genres));
        var services = new ServiceCollection()
            .AddScoped(_ => _database.CreateContext())
            .AddSingleton(storage.Object)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Shuffull:Files:GenresFile"] = "genres.json",
        }).Build();
        return new TagImporterService(configuration, services);
    }

    private static GenresFile Genres(string main, params string[] subs) =>
        new() { MainGenres = [new GenresFile.MainGenre { Name = main, SubGenreNames = [.. subs] }] };

    [Fact]
    public async Task Import_ReusesAGenreThatHasNoRelationYet()
    {
        var bigBand = new Genre { TagId = IdGenerator.Generate(), Name = "Big Band" };
        _database.Context.Tags.Add(bigBand);
        await _database.Context.SaveChangesAsync();

        await Importer(Genres("Jazz", "Big Band")).ImportGenreList(CancellationToken.None);

        var check = _database.CreateContext();
        var names = await check.Tags.AsNoTracking().Select(t => t.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(["Big Band", "Jazz"], names);
        var relation = Assert.Single(await check.GenreRelations.AsNoTracking().ToListAsync());
        Assert.Equal(bigBand.TagId, relation.SubGenreId);
    }

    [Fact]
    public async Task Import_SkipsANameHeldByAnotherTagType()
    {
        _database.Context.Tags.Add(new Mood { TagId = IdGenerator.Generate(), Name = "Chill" });
        await _database.Context.SaveChangesAsync();

        await Importer(Genres("Electronic", "Chill", "House")).ImportGenreList(CancellationToken.None);

        var check = _database.CreateContext();
        Assert.Single(await check.Tags.AsNoTracking().Where(t => t.Name == "Chill").ToListAsync());
        var relation = Assert.Single(await check.GenreRelations.AsNoTracking().Include(r => r.SubGenre).ToListAsync());
        Assert.Equal("House", relation.SubGenre.Name);
    }

    [Fact]
    public async Task Import_ASubGenreUnderTwoMains_IsOneTag()
    {
        var genres = new GenresFile
        {
            MainGenres =
            [
                new GenresFile.MainGenre { Name = "Jazz", SubGenreNames = ["Swing"] },
                new GenresFile.MainGenre { Name = "Pop", SubGenreNames = ["Swing"] },
            ],
        };

        await Importer(genres).ImportGenreList(CancellationToken.None);

        var check = _database.CreateContext();
        Assert.Single(await check.Tags.AsNoTracking().Where(t => t.Name == "Swing").ToListAsync());
        Assert.Equal(2, await check.GenreRelations.CountAsync());
    }
}
