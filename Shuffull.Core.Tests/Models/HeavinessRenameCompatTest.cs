using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Shuffull.Api.Commands.Songs.UpdateSong;
using Shuffull.Core.Models.Dtos;
using Shuffull.Metadata.Models;

namespace Shuffull.Core.Tests.Models;

/// <summary>
/// The heaviness score was called energy until shuffull-workspace#50, and JSON written under that name outlives the
/// rename: import rows, export files, AI response files, queued app edits, app builds not yet updated. Each of these
/// must still land as heaviness rather than silently as null.
/// </summary>
public class HeavinessRenameCompatTest
{
    // What ASP.NET Core model binding uses when nothing overrides it, as here.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void GeneratedSongTags_StoredUnderEnergy_ReadsAsHeaviness()
    {
        // SongImports.GeneratedTagsJson and the export files, as Newtonsoft wrote them before the rename.
        const string stored = """{"MainGenres":["Rock"],"SubGenres":[],"Languages":["English"],"TimePeriod":"1990s","Energy":8}""";

        Assert.Equal(8, JsonConvert.DeserializeObject<GeneratedSongTags>(stored)!.Heaviness);
    }

    [Fact]
    public void GeneratedSongTags_PostedUnderEnergy_ReadsAsHeaviness()
    {
        // POST /songs/{id}/tags from a funnel still on the old Shuffull.Metadata (PascalCase, System.Text.Json).
        const string posted = """{"MainGenres":["Rock"],"SubGenres":[],"Languages":["English"],"TimePeriod":"1990s","Energy":7}""";

        Assert.Equal(7, System.Text.Json.JsonSerializer.Deserialize<GeneratedSongTags>(posted, Web)!.Heaviness);
    }

    [Fact]
    public void GeneratedSongTags_IsWrittenOnlyAsHeaviness()
    {
        var tags = new GeneratedSongTags(["Rock"], [], ["English"], "1990s", Heaviness: 6);

        var newtonsoft = JsonConvert.SerializeObject(tags);
        var stj = System.Text.Json.JsonSerializer.Serialize(tags, Web);

        Assert.Contains("\"Heaviness\":6", newtonsoft);
        Assert.DoesNotContain("Energy", newtonsoft, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"heaviness\":6", stj);
        Assert.DoesNotContain("energy", stj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateSongRequest_SentUnderEnergy_KeepsTheScore()
    {
        // An edit queued by an app from before the rename. Read as null, it would clear the song's score.
        const string body = """{"name":"A Song","bpm":120,"energy":9,"artists":[],"tags":[]}""";

        Assert.Equal(9, System.Text.Json.JsonSerializer.Deserialize<UpdateSongRequest>(body, Web)!.Heaviness);
    }

    [Fact]
    public void UpdateSongRequest_SentUnderHeaviness_ReadsIt()
    {
        const string body = """{"name":"A Song","bpm":120,"heaviness":4,"artists":[],"tags":[]}""";

        Assert.Equal(4, System.Text.Json.JsonSerializer.Deserialize<UpdateSongRequest>(body, Web)!.Heaviness);
    }

    [Fact]
    public void SongDto_CarriesTheScoreUnderBothNames()
    {
        var dto = new SongDto("s1", "A Song", ".mp3", "hash", null, [], [], DateTime.UtcNow, Heaviness: 5);

        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(dto, Web))!;

        Assert.Equal(5, (int)json["heaviness"]!);
        Assert.Equal(5, (int)json["energy"]!);
    }
}
