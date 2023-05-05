using AutoMapper;
using Shuffull.Site.Database.Models;

namespace Shuffull.Site.Tools
{
    public class ClassMapper
    {
        public static readonly IMapper Mapper;

        static ClassMapper()
        {
            var config = new MapperConfiguration(x =>
            {
                x.CreateMap<Song, Shared.Networking.Models.Song>();
                x.CreateMap<PlaylistSong, Shared.Networking.Models.PlaylistSong>();
                x.CreateMap<Playlist, Shared.Networking.Models.Playlist>();
            });

            Mapper = config.CreateMapper();
        }
    }
}
