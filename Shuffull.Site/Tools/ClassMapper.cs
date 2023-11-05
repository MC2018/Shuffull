using AutoMapper;
using Shuffull.Shared.Networking.Models.Server;
using Shuffull.Site.Database.Models;
using Playlist = Shuffull.Site.Database.Models.Playlist;
using PlaylistSong = Shuffull.Site.Database.Models.PlaylistSong;
using Song = Shuffull.Site.Database.Models.Song;
using User = Shuffull.Site.Database.Models.User;

namespace Shuffull.Site.Tools
{
    public class ClassMapper
    {
        public static readonly IMapper Mapper;

        static ClassMapper()
        {
            var config = new MapperConfiguration(x =>
            {
                x.CreateMap<User, Shared.Networking.Models.Server.User>();
                x.CreateMap<Song, Shared.Networking.Models.Server.Song>();
                x.CreateMap<PlaylistSong, Shared.Networking.Models.Server.PlaylistSong>();
                x.CreateMap<Playlist, Shared.Networking.Models.Server.Playlist>();
            });

            Mapper = config.CreateMapper();
        }
    }
}
