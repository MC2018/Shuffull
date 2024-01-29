using System;
using Shuffull.Shared.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shuffull.Shared.Models.Server;

namespace Shuffull.Shared.Models.Requests
{
    public class CreateUserSongRequest : Request
    {
        public long UserId { get; set; }
        public long SongId { get; set; }

        public CreateUserSongRequest()
        {
            RequestType = RequestType.CreateUserSong;
            RequestName = RequestType.CreateUserSong.ToString();
            ProcessingMethod = ProcessingMethod.Batch;
        }


        public override void UpdateLocalDb(ShuffullContext context)
        {
            var userSong = new UserSong()
            {
                UserId = UserId,
                SongId = SongId
            };

            context.UserSongs.Add(userSong);
        }
    }
}
