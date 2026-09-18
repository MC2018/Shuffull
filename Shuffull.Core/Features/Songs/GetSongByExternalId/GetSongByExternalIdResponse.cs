namespace Shuffull.Core.Features.Songs.GetSongByExternalId;

public record GetSongByExternalIdResponse(
    string SongId,
    string Name,
    List<string> Artists,
    string? ExternalSongId,
    bool MetadataLocked,
    bool Exploratory);
