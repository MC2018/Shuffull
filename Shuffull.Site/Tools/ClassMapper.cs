using AutoMapper;
using Shuffull.Database.Models;
using Shuffull.Shared.Networking.Models;

namespace Shuffull.Site.Tools
{
    public class ClassMapper
    {
        public static readonly IMapper Mapper;

        static ClassMapper()
        {
            var config = new MapperConfiguration(x =>
            {
                x.CreateMap<Song, SongResult>();
                x.CreateMap<Playlist, PlaylistResult>();
            });

            Mapper = config.CreateMapper();
        }
    }
}
