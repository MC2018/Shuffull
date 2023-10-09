using Microsoft.EntityFrameworkCore;
using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class UpdateSongLastPlayedRequest : Request
    {
        public long SongId { get; set; }
        public DateTime LastPlayed { get; set; }

        public UpdateSongLastPlayedRequest()
        { 
            RequestType = RequestType.UpdateSongLastPlayed;
            RequestName = RequestType.UpdateSongLastPlayed.ToString();
        }

        public override void UpdateLocalDb(ShuffullContext context)
        {
            var song = context.Songs
                .Where(x => x.SongId == SongId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Playlist)
                .First();

            foreach (var playlistSong in song.PlaylistSongs)
            {
                playlistSong.LastPlayed = LastPlayed;
                playlistSong.InQueue = false;
            }

            context.UpdateQueue(SongId);
        }
    }
}
