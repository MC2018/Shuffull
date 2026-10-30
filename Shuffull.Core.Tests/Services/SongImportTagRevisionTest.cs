using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using Shuffull.Metadata.Models;
using Shuffull.Metadata.Services.AI;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The prompt revision travels from the producer's hand-off to the Song. A revision that's lost on the way reads
/// as 0, and the song is re-scored for nothing; one stamped too high skips a song that still needs it.
/// </summary>
public class SongImportTagRevisionTest : IDisposable
{
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SongImportDetails Details(GeneratedSongTags? tags, int tagRevision) => new(
        ExternalSongId: "ext-1", FileExtension: ".mp3", ExternalPlaylistId: "pl-1", TargetUserId: "user-1",
        TargetPlaylistId: null, TargetPlaylistName: null, ExternalSource: ExternalSource.Youtube,
        GeneratedTags: tags, Name: "Song Name", Artists: ["Artist"], TagRevision: tagRevision);

    private SongImportIntakeService Intake() => SongImportHarness.Intake(_database);

    [Fact]
    public async Task Intake_CarriesTheProducersRevision()
    {
        var tags = new GeneratedSongTags(["Rock"], [], ["English"], "2010s", null, 6, null);

        var result = await Intake().CreateImportAsync(Details(tags, TagRevision.Current), [1, 2, 3]);

        Assert.True(result.IsOk);
        var row = await _database.Context.SongImports.AsNoTracking().SingleAsync(i => i.SongImportId == result.Get());
        Assert.Equal(TagRevision.Current, row.TagRevision);
    }

    [Fact]
    public void ImportedRevision_IsTheProducersWhenItSentTags()
    {
        var import = new SongImport { GeneratedTagsJson = "{}", TagRevision = TagRevision.Current };

        Assert.Equal(TagRevision.Current, SongImportService.ImportedTagRevision(import));
    }

    [Fact]
    public void ImportedRevision_IsZeroForTagsGeneratedAtImport()
    {
        // Self-generated import tags carry no heaviness, so the queue must still offer the song once.
        var import = new SongImport { GeneratedTagsJson = null, TagRevision = TagRevision.Current };

        Assert.Equal(0, SongImportService.ImportedTagRevision(import));
    }
}
