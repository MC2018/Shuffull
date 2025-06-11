using Microsoft.EntityFrameworkCore;
using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace Shuffull.Shared.Models.Requests
{
    [Serializable]
    public class UpdateSongLastPlayedRequest : Request
    {
        [Required]
        public string SongId { get; set; }
        [Required]
        public DateTime LastPlayed { get; set; }

        public UpdateSongLastPlayedRequest()
        {
            RequestType = RequestType.UpdateSongLastPlayed;
            RequestName = RequestType.UpdateSongLastPlayed.ToString();
            ProcessingMethod = ProcessingMethod.Batch;
        }

        public override void UpdateLocalDb(ShuffullContext context)
        {
            var localSessionData = context.LocalSessionData.First();
            var userSong = context.Songs
                .Where(x => x.SongId == SongId)
                .Select(x => x.UserSongs.Where(y => y.UserId == localSessionData.UserId).First())
                .First();

            userSong.LastPlayed = LastPlayed;
        }
    }
}
