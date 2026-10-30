using Newtonsoft.Json;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The hook start point rides the producer's hand-off, which both sides serialize with Newtonsoft's defaults. A
/// payload from a producer that doesn't send it has to read as unknown, not as 0% (the very start of the song).
/// </summary>
public class SongImportHookStartTest
{
    private static SongImportDetails Details(double? hookStartPercent) => new(
        ExternalSongId: "ext-1", FileExtension: ".mp3", ExternalPlaylistId: "pl-1", TargetUserId: "user-1",
        TargetPlaylistId: null, TargetPlaylistName: null, ExternalSource: ExternalSource.Youtube,
        HookStartPercent: hookStartPercent);

    [Fact]
    public void HookStartPercent_SurvivesTheRoundTrip()
    {
        var json = JsonConvert.SerializeObject(Details(62.5));

        var read = JsonConvert.DeserializeObject<SongImportDetails>(json);

        Assert.NotNull(read);
        Assert.Equal(62.5, read.HookStartPercent);
    }

    [Fact]
    public void HookStartPercent_LeftUnset_RoundTripsAsNull()
    {
        var json = JsonConvert.SerializeObject(Details(null));

        var read = JsonConvert.DeserializeObject<SongImportDetails>(json);

        Assert.NotNull(read);
        Assert.Null(read.HookStartPercent);
    }

    [Fact]
    public void OlderPayload_WithoutTheField_ReadsAsUnknown()
    {
        const string json = """
            {"ExternalSongId":"ext-1","FileExtension":".mp3","ExternalPlaylistId":"pl-1","TargetUserId":"user-1",
             "TargetPlaylistId":null,"TargetPlaylistName":null,"ExternalSource":1,"TagRevision":2}
            """;

        var read = JsonConvert.DeserializeObject<SongImportDetails>(json);

        Assert.NotNull(read);
        Assert.Equal("ext-1", read.ExternalSongId);
        Assert.Null(read.HookStartPercent);
    }
}
