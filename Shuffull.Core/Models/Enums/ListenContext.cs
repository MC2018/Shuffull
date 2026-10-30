namespace Shuffull.Core.Models.Enums;

/// <summary>What the user was playing from when a <see cref="Database.Listen"/> happened. On the wire it is the
/// lower-camel name ("playlist", "audition", "genreJam", "other").</summary>
public enum ListenContext
{
    Playlist = 0,
    /// <summary>An exploratory (audition) playlist. Counts towards the recap's minutes spent auditioning.</summary>
    Audition = 1,
    GenreJam = 2,
    Other = 3,
}
