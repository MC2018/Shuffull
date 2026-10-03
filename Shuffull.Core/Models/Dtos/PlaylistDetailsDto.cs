using Nut.Results;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="Playlist"/> with full details for each song (artists and
/// tags), as opposed to <see cref="PlaylistDto"/> which carries only song ids. Built through the
/// static <see cref="Create"/> factory so each contained <see cref="SongDto"/> is mapped uniformly.
/// </summary>
public record PlaylistDetailsDto(
    string PlaylistId,
    string UserId,
    string Name,
    string? CurrentSongId,
    decimal PercentUntilReplayable,
    DateTime Version,
    IReadOnlyList<SongDto> Songs,
    // Audition playlist: imported from an exploratory source; deleting it purges the songs the user never kept.
    bool IsExploratory = false)
{
    public static Result<PlaylistDetailsDto> Create(Playlist playlist)
    {
        if (playlist is null)
        {
            return Result.Error<PlaylistDetailsDto>("Playlist cannot be null.");
        }

        var songs = new List<SongDto>();
        // PlaylistSongs is only populated when the query Includes it; treat a missing collection as
        // "none" rather than throwing.
        foreach (var playlistSong in playlist.PlaylistSongs ?? Enumerable.Empty<PlaylistSong>())
        {
            if (playlistSong.Song is null)
            {
                continue;
            }

            var songResult = SongDto.Create(playlistSong.Song);
            if (songResult.IsError)
            {
                return Result.Error<PlaylistDetailsDto>(songResult.GetError().Message);
            }

            songs.Add(songResult.Get());
        }

        return Result.Ok(new PlaylistDetailsDto(
            PlaylistId: playlist.PlaylistId,
            UserId: playlist.UserId,
            Name: playlist.Name,
            CurrentSongId: playlist.CurrentSongId,
            PercentUntilReplayable: playlist.PercentUntilReplayable,
            Version: playlist.Version,
            Songs: songs,
            IsExploratory: playlist.IsExploratory));
    }
}
