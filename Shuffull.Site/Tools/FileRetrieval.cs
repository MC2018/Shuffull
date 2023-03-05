using Shuffull.Database;
using System.Data.Common;

namespace Shuffull.Site.Tools
{
    public class FileRetrieval
    {
        public static string RootDirectory { get; set; } = string.Empty;

        public static string GetDirectory(long songId, ShuffullContext context)
        {
            var song = context.Songs.Where(x => x.SongId == songId).First();

            return Path.Combine(RootDirectory, song.Directory);
        }

        public static string GetUrl(long songId, ShuffullContext context, HttpRequest request)
        {
            var song = context.Songs.Where(x => x.SongId == songId).First();

            return Path.Combine(request.Scheme + "://", request.Host.ToString(), "music", song.Directory);
        }
    }
}
