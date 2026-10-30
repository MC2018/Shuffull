using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Tests.Features.Listens.GetRecap;

/// <summary>
/// Writes songs, artists, tags and listens for the recap tests. Each listen is placed at an exact UTC instant, so a
/// test can put one a minute either side of a local midnight.
/// </summary>
internal sealed class RecapFixture(ShuffullContext context, string userId)
{
    private int _count;

    public static User NewUser() => new()
    {
        UserId = Guid.NewGuid().ToString(),
        Username = $"user-{Guid.NewGuid():N}",
        Version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ServerHash = "server-hash",
    };

    public static DateTime Utc(int year, int month, int day, int hour = 12, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    public Song Song(string songId, string name, bool exploratory = false, string[]? artists = null, Tag[]? tags = null)
    {
        var song = new Song
        {
            SongId = songId,
            Name = name,
            FileHash = $"hash-{songId}",
            FileExtension = "mp3",
            Exploratory = exploratory,
            Version = Utc(2026, 1, 1),
        };
        context.Songs.Add(song);
        foreach (var artist in artists ?? [])
        {
            var artistId = $"artist-{artist}";
            if (context.Artists.Local.All(a => a.ArtistId != artistId))
            {
                context.Artists.Add(new Artist { ArtistId = artistId, Name = artist });
            }
            context.SongArtists.Add(new SongArtist { SongArtistId = $"{songId}-{artistId}", SongId = songId, ArtistId = artistId });
        }
        foreach (var tag in tags ?? [])
        {
            context.SongTags.Add(new SongTag { SongTagId = $"{songId}-{tag.TagId}", SongId = songId, TagId = tag.TagId });
        }
        return song;
    }

    public Genre Genre(string name)
    {
        var genre = new Genre { TagId = $"genre-{name}", Name = name };
        context.Tags.Add(genre);
        return genre;
    }

    public Mood Mood(string name)
    {
        var mood = new Mood { TagId = $"mood-{name}", Name = name };
        context.Tags.Add(mood);
        return mood;
    }

    public void Owns(string songId, LikeStatus likeStatus = LikeStatus.Neutral, string? owner = null)
        => context.UserSongs.Add(new UserSong
        {
            UserId = owner ?? userId,
            SongId = songId,
            LikeStatus = likeStatus,
            LastPlayed = Utc(2026, 1, 1),
            Version = Utc(2026, 1, 1),
        });

    /// <summary>A listen starting at <paramref name="startedAt"/> (UTC) that plays <paramref name="minutes"/> unpaused.</summary>
    public void Listen(string songId, DateTime startedAt, double minutes = 1,
        ListenEndReason endReason = ListenEndReason.Finished, ListenContext listenContext = ListenContext.Playlist)
        => context.Listens.Add(new Listen
        {
            ListenId = $"{userId}-{_count++:D5}",
            UserId = userId,
            SongId = songId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(minutes),
            SecondsListened = minutes * 60,
            SongDurationSeconds = 240,
            EndReason = endReason,
            Context = listenContext,
            RecordedAt = startedAt,
        });
}
