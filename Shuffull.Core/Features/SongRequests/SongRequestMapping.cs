using System.Text.Json;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongRequests;

/// <summary>Turns a <see cref="SongRequest"/> row into what the endpoints send.</summary>
public static class SongRequestMapping
{
    public static List<string> ArtistsOf(SongRequest row) =>
        JsonSerializer.Deserialize<List<string>>(row.ArtistsJson) ?? [];

    public static SongRequestDto ToDto(SongRequest row) => new(
        row.SongRequestId, row.ExternalSongId, row.Title, ArtistsOf(row), row.DurationSeconds, row.ThumbnailUrl,
        row.Status, row.FailureReason, row.SongId, row.TargetPlaylistName, row.CreatedAt, row.UpdatedAt);
}
