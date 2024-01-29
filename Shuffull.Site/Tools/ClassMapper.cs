using AutoMapper;
using Shuffull.Site.Database.Models;
using Shuffull.Site.Migrations;
using System.Text.Json.Serialization;
using System.Text.Json;
using Artist = Shuffull.Site.Database.Models.Artist;
using Playlist = Shuffull.Site.Database.Models.Playlist;
using PlaylistSong = Shuffull.Site.Database.Models.PlaylistSong;
using Song = Shuffull.Site.Database.Models.Song;
using SongArtist = Shuffull.Site.Database.Models.SongArtist;
using SongTag = Shuffull.Site.Database.Models.SongTag;
using Tag = Shuffull.Site.Database.Models.Tag;
using User = Shuffull.Site.Database.Models.User;
using UserSong = Shuffull.Site.Database.Models.UserSong;

namespace Shuffull.Site.Tools
{
    /// <summary>
    /// Maps classes between the site and shared projects
    /// </summary>
    public class ClassMapper
    {
        public static readonly IMapper Mapper;

        /// <summary>
        /// Constructor, sets up the map
        /// </summary>
        static ClassMapper()
        {
            var config = new MapperConfiguration(x =>
            {
                x.CreateMap<Artist, Shared.Models.Server.Artist>();
                x.CreateMap<Playlist, Shared.Models.Server.Playlist>();
                x.CreateMap<PlaylistSong, Shared.Models.Server.PlaylistSong>();
                x.CreateMap<Song, Shared.Models.Server.Song>();
                x.CreateMap<SongArtist, Shared.Models.Server.SongArtist>();
                x.CreateMap<SongTag, Shared.Models.Server.SongTag>();
                x.CreateMap<Tag, Shared.Models.Server.Tag>();
                x.CreateMap<User, Shared.Models.Server.User>();
                x.CreateMap<UserSong, Shared.Models.Server.UserSong>();
            });

            Mapper = config.CreateMapper();
        }

        /// <summary>
        /// Maps an item and serializes the result
        /// </summary>
        /// <typeparam name="T">Class of the variable wanting to be mapped</typeparam>
        /// <param name="item">Item to be mapped</param>
        /// <exception cref="NotSupportedException">Thrown if something in the item is not mapped or serializable</exception>
        /// <returns>Serialized JSON of the mapped item</returns>
        public static string MapAndSerialize<T>(T item)
        {
            var result = Mapper.Map<T>(item);
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };

            return JsonSerializer.Serialize(result, options);
        }
    }
}
