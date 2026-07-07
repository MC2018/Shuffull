using Nut.Results;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="Playlist"/>. Built through the static <see cref="Create"/>
/// factory (mirrors the Sociallite DTO pattern) so mapping/validation lives in one place and can
/// surface failures as a <see cref="Result"/> rather than throwing.
/// </summary>
public record PlaylistDto(
    string PlaylistId,
    string UserId,
    string Name,
    string? CurrentSongId,
    decimal PercentUntilReplayable,
    DateTime Version,
    IReadOnlyList<string> SongIds,
    // Audition playlist: imported from an exploratory source; deleting it purges the songs the user never kept.
    bool IsExploratory = false)
{
    public static Result<PlaylistDto> Create(Playlist playlist)
    {
        if (playlist is null)
        {
            return Result.Error<PlaylistDto>("Playlist cannot be null.");
        }

        // PlaylistSongs is only populated when the query Includes it; treat a missing collection as
        // "none" rather than throwing so the DTO works for lighter-weight queries (e.g. create) too.
        var songIds = playlist.PlaylistSongs?
            .Select(playlistSong => playlistSong.SongId)
            .ToList() ?? new List<string>();

        return Result.Ok(new PlaylistDto(
            PlaylistId: playlist.PlaylistId,
            UserId: playlist.UserId,
            Name: playlist.Name,
            CurrentSongId: playlist.CurrentSongId,
            PercentUntilReplayable: playlist.PercentUntilReplayable,
            Version: playlist.Version,
            SongIds: songIds,
            IsExploratory: playlist.IsExploratory));
    }
}
