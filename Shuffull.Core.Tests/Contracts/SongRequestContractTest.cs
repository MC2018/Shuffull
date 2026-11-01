using System.Text.Json;
using System.Text.Json.Nodes;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace Shuffull.Core.Tests.Contracts;

/// <summary>
/// The song request types in Shuffull.Metadata (MC2018/Shuffull.Metadata#1) survive every way the two sides write and
/// read JSON. The API writes camelCase and binds case-insensitively; the funnel's HTTP clients write with no naming
/// policy (PascalCase) through System.Text.Json, and its drops go through Newtonsoft. A value written by any of them
/// and read by any other must come back unchanged. The endpoints themselves (Shuffull#57, #58) pin their wire shapes
/// in the golden files once they exist.
/// </summary>
public class SongRequestContractTest
{
    private static readonly JsonSerializerOptions FunnelOptions = new() { PropertyNameCaseInsensitive = true };

    public static TheoryData<string> Values => new(Cases.Keys);

    private static readonly Dictionary<string, object> Cases = new()
    {
        ["pending"] = new PendingSongRequestDto(
            SongRequestId: "song-request-1",
            UserId: "user-1",
            ExternalSongId: "dQw4w9WgXcQ",
            Title: "Everybody Wants You",
            Artists: ["Billy Squier", "Beyoncé"],
            DurationSeconds: 236,
            ThumbnailUrl: "https://example.com/thumb.jpg",
            TargetPlaylistName: "Requests · week of 2026-11-02"),
        ["pending-sparse"] = new PendingSongRequestDto("song-request-2", "user-1", "9bZkp7q19f0", "Untitled", [], null, null,
            "Requests · week of 2026-11-02"),
        ["ack"] = new SongRequestAckDto("song-request-1", SongRequestAckStatus.Retrying, "timeout"),
        ["ack-no-reason"] = new SongRequestAckDto("song-request-1", SongRequestAckStatus.Accepted),
        ["ack-response"] = new SongRequestAckResponse(["song-request-1"], ["song-request-2"]),
        ["search"] = new SourceSearchResultDto("dQw4w9WgXcQ", "Everybody Wants You", ["Billy Squier"], "Don't Say No", 236,
            "https://example.com/thumb.jpg", IsExplicit: true),
        ["search-sparse"] = new SourceSearchResultDto("9bZkp7q19f0", "Untitled", [], null, null, null, IsExplicit: false),
        ["import"] = new SongImportDetails("dQw4w9WgXcQ", ".mp3", "", "user-1", null, "Requests · week of 2026-11-02",
            ExternalSource.Youtube, Exploratory: true, SongRequestId: "song-request-1"),
    };

    private static readonly Dictionary<string, Func<object, string>> Writers = new()
    {
        ["api"] = value => JsonSerializer.Serialize(value, value.GetType(), ApiJsonOptions()),
        ["funnel"] = value => JsonSerializer.Serialize(value, value.GetType(), FunnelOptions),
        ["newtonsoft"] = JsonConvert.SerializeObject,
    };

    private static readonly Dictionary<string, Func<string, Type, object?>> Readers = new()
    {
        ["api"] = (json, type) => JsonSerializer.Deserialize(json, type, ApiJsonOptions()),
        ["funnel"] = (json, type) => JsonSerializer.Deserialize(json, type, FunnelOptions),
        ["newtonsoft"] = JsonConvert.DeserializeObject,
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void EveryWriter_RoundTripsThroughEveryReader(string name)
    {
        var value = Cases[name];
        var expected = Canonical(value);

        foreach (var (writerName, write) in Writers)
        {
            var json = write(value);
            foreach (var (readerName, read) in Readers)
            {
                var actual = Canonical(read(json, value.GetType())!);
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{name}: written by {writerName}, read by {readerName}, got {actual}");
            }
        }
    }

    [Fact]
    public void AckStatus_GoesOverTheWireAsItsInt()
    {
        var json = JsonNode.Parse(Writers["api"](new SongRequestAckDto("song-request-1", SongRequestAckStatus.Failed, "unavailable")))!;

        Assert.Equal(4, json["status"]!.GetValue<int>());
    }

    [Fact]
    public void AnAckWithNoStatus_ReadsAsUnknown()
    {
        // So the API can refuse it instead of taking it as the first real status.
        var ack = JsonSerializer.Deserialize<SongRequestAckDto>("""{"songRequestId":"song-request-1"}""", ApiJsonOptions())!;

        Assert.Equal(SongRequestAckStatus.Unknown, ack.Status);
    }

    [Fact]
    public void AnOlderImportWithNoRequestId_StillReads()
    {
        var details = JsonConvert.DeserializeObject<SongImportDetails>(
            """{"ExternalSongId":"dQw4w9WgXcQ","FileExtension":".mp3","ExternalPlaylistId":"LL","TargetUserId":"user-1","ExternalSource":1}""")!;

        Assert.Null(details.SongRequestId);
    }

    // Records compare their lists by reference, so compare what they serialize to instead.
    private static JsonNode Canonical(object value) =>
        JsonNode.Parse(JsonSerializer.Serialize(value, value.GetType(), ApiJsonOptions()))!;

    // Built once: the golden test's helper builds a service provider on every call.
    private static readonly Lazy<JsonSerializerOptions> Api = new(ApiContractGoldenFilesTest.ApiJsonOptions);

    private static JsonSerializerOptions ApiJsonOptions() => Api.Value;
}
