using AutoMapper;
using Shuffull.Database.Models;
using Results = Shuffull.Shared.Networking.Models.Results;

namespace Shuffull.Site.Tools
{
    public class ClassMapper
    {
        public static readonly IMapper Mapper;

        static ClassMapper()
        {
            var config = new MapperConfiguration(x =>
            {
                x.CreateMap<Song, Results.Song>();
                x.CreateMap<PlaylistSong, Results.PlaylistSong>();
                x.CreateMap<Playlist, Results.Playlist>();
            });

            Mapper = config.CreateMapper();
        }
    }
}
