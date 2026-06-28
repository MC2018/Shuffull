using Newtonsoft.Json;
using Shuffull.Api.Tools.SongParsing;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Covers the import-precedence rules extracted from SongImportIntakeService (name) and
/// SongImportService.ParseArtistsAsync (artists). Producer-supplied values win over the audio file's ID3
/// tags; the ID3 tags are only a fallback.
/// </summary>
public class SongImportMetadataResolverTest
{
    // ---- ResolveSongName ----

    [Fact]
    public void ResolveSongName_PrefersProducerNameWhenPresent()
    {
        var name = SongImportMetadataResolver.ResolveSongName("Producer Title", "ID3 Title", "ext-123");

        Assert.Equal("Producer Title", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSongName_FallsBackToId3TitleWhenProducerNameBlank(string? producerName)
    {
        var name = SongImportMetadataResolver.ResolveSongName(producerName, "ID3 Title", "ext-123");

        Assert.Equal("ID3 Title", name);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData("   ", "  ")]
    public void ResolveSongName_FallsBackToExternalIdWhenNameAndId3Blank(string? producerName, string? id3Title)
    {
        var name = SongImportMetadataResolver.ResolveSongName(producerName, id3Title, "ext-123");

        Assert.Equal("ext-123", name);
    }

    [Fact]
    public void ResolveSongName_ProducerNameWinsEvenOverNonBlankId3Title()
    {
        // The producer title is authoritative; a populated ID3 title must not override it.
        var name = SongImportMetadataResolver.ResolveSongName("Vetted Name", "Stale MusicBrainz Name", "ext-9");

        Assert.Equal("Vetted Name", name);
    }

    // ---- ResolveArtistNames ----

    [Fact]
    public void ResolveArtistNames_PrefersProducerArtistsWhenJsonPresent()
    {
        var json = JsonConvert.SerializeObject(new List<string> { "Artist A", "Artist B" });

        var artists = SongImportMetadataResolver.ResolveArtistNames(json, new[] { "ID3 Performer" });

        Assert.Equal(new[] { "Artist A", "Artist B" }, artists);
    }

    [Fact]
    public void ResolveArtistNames_PreservesProducerArtistOrder()
    {
        var json = JsonConvert.SerializeObject(new List<string> { "First", "Second", "Third" });

        var artists = SongImportMetadataResolver.ResolveArtistNames(json, Array.Empty<string>());

        Assert.Equal(new[] { "First", "Second", "Third" }, artists);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveArtistNames_FallsBackToId3PerformersWhenJsonBlank(string? artistsJson)
    {
        var performers = new[] { "Performer 1", "Performer 2" };

        var artists = SongImportMetadataResolver.ResolveArtistNames(artistsJson, performers);

        Assert.Equal(performers, artists);
    }

    [Fact]
    public void ResolveArtistNames_DropsBlankProducerEntries()
    {
        var json = JsonConvert.SerializeObject(new List<string> { "Real", "", "   ", "Also Real" });

        var artists = SongImportMetadataResolver.ResolveArtistNames(json, new[] { "ID3" });

        Assert.Equal(new[] { "Real", "Also Real" }, artists);
    }

    [Fact]
    public void ResolveArtistNames_ProducerJsonOfAllBlanksDoesNotFallBackToId3()
    {
        // Behavior-preserving: a present-but-empty producer list still wins (the original code only fell back
        // to ID3 when ArtistsJson itself was blank), so the result is an empty list, not the ID3 performers.
        var json = JsonConvert.SerializeObject(new List<string> { "", "  " });

        var artists = SongImportMetadataResolver.ResolveArtistNames(json, new[] { "ID3 Performer" });

        Assert.Empty(artists);
    }

    [Fact]
    public void ResolveArtistNames_EmptyJsonArrayDoesNotFallBackToId3()
    {
        var artists = SongImportMetadataResolver.ResolveArtistNames("[]", new[] { "ID3 Performer" });

        Assert.Empty(artists);
    }

    [Fact]
    public void ResolveArtistNames_NullId3PerformersTreatedAsEmpty()
    {
        var artists = SongImportMetadataResolver.ResolveArtistNames(null, null!);

        Assert.Empty(artists);
    }

    [Fact]
    public void ResolveArtistNames_DropsBlankId3PerformerEntries()
    {
        // The ID3 fallback is filtered too, so a blank performer never becomes an empty-named artist.
        var artists = SongImportMetadataResolver.ResolveArtistNames(null, new[] { "Real", "", "   ", "Also Real" });

        Assert.Equal(new[] { "Real", "Also Real" }, artists);
    }
}
