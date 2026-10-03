using Shuffull.Shared.Enums;

namespace Shuffull.Core.Features.Songs.UpdateSong;

/// <summary>A single tag a curator wants the song to carry, identified by its display name and type.</summary>
public record SongTagEdit(string Name, TagType Type);
