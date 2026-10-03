using Shuffull.Api.Services;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// Locks in the refresh-vs-drop decision for a colliding import (SongImportService.ShouldRefreshInPlace):
/// only "same source video, different audio content" refreshes the existing song in place; everything else
/// stays an idempotent drop, and a tag-less audition re-rip can never clobber a promoted song's tags.
/// </summary>
public class SongImportRefreshInPlaceTest
{
    private static Song ExistingSong(string? externalSongId = "yt-vid-1", string fileHash = "old-hash", bool exploratory = false) => new()
    {
        SongId = "song-1",
        Name = "Song",
        FileExtension = ".mp3",
        FileHash = fileHash,
        ExternalSongId = externalSongId,
        Exploratory = exploratory,
    };

    [Fact]
    public void SameVideo_NewAudio_Refreshes()
        => Assert.True(SongImportService.ShouldRefreshInPlace(
            ExistingSong(), importExternalSongId: "yt-vid-1", importFileHash: "new-hash", importExploratory: false));

    [Fact]
    public void SameVideo_SameAudio_DropsAsDuplicate()
        => Assert.False(SongImportService.ShouldRefreshInPlace(
            ExistingSong(fileHash: "same-hash"), importExternalSongId: "yt-vid-1", importFileHash: "same-hash", importExploratory: false));

    [Fact]
    public void HashOnlyCollision_DifferentVideo_DropsAsDuplicate()
        => Assert.False(SongImportService.ShouldRefreshInPlace(
            ExistingSong(externalSongId: "yt-vid-OTHER", fileHash: "same-hash"),
            importExternalSongId: "yt-vid-1", importFileHash: "same-hash", importExploratory: false));

    [Fact]
    public void HashOnlyCollision_ManualUploadWithoutExternalId_DropsAsDuplicate()
        => Assert.False(SongImportService.ShouldRefreshInPlace(
            ExistingSong(externalSongId: null, fileHash: "same-hash"),
            importExternalSongId: null, importFileHash: "same-hash", importExploratory: false));

    [Fact]
    public void ExploratoryReRip_OfPromotedSong_DropsInsteadOfWipingTags()
        => Assert.False(SongImportService.ShouldRefreshInPlace(
            ExistingSong(exploratory: false), importExternalSongId: "yt-vid-1", importFileHash: "new-hash", importExploratory: true));

    [Fact]
    public void ExploratoryReRip_OfStillExploratorySong_Refreshes()
        => Assert.True(SongImportService.ShouldRefreshInPlace(
            ExistingSong(exploratory: true), importExternalSongId: "yt-vid-1", importFileHash: "new-hash", importExploratory: true));
}
