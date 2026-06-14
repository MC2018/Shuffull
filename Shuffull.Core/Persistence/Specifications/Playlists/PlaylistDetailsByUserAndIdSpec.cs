using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Playlists;

/// <summary>
/// Loads a single playlist (scoped to its owning user) together with full details for each of its
/// songs — artists and tags included — so the result can be projected to a
/// <see cref="Models.Dtos.PlaylistDetailsDto"/> without lazy loading. Unlike the legacy
/// PlaylistController.Get, this is scoped to the user so one user cannot read another's playlist.
/// </summary>
public class PlaylistDetailsByUserAndIdSpec : BaseSpecification<Playlist>
{
    private readonly string _userId;
    private readonly string _playlistId;

    public PlaylistDetailsByUserAndIdSpec(string userId, string playlistId)
    {
        _userId = userId;
        _playlistId = playlistId;
    }

    protected override IQueryable<Playlist> BuildQuery(IQueryable<Playlist> query)
        => query
            .Where(playlist => playlist.UserId == _userId && playlist.PlaylistId == _playlistId)
            .Include(playlist => playlist.PlaylistSongs)
                .ThenInclude(playlistSong => playlistSong.Song)
                    .ThenInclude(song => song.SongArtists)
                        .ThenInclude(songArtist => songArtist.Artist)
            .Include(playlist => playlist.PlaylistSongs)
                .ThenInclude(playlistSong => playlistSong.Song)
                    .ThenInclude(song => song.SongTags)
                        .ThenInclude(songTag => songTag.Tag);
}
