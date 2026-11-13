using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.UpdateSongLyrics;

/// <summary>Producer: lyrics for the song imported from <paramref name="ExternalSongId"/>. See <see cref="UpdateSongLyricsHandler"/>.</summary>
public record UpdateSongLyricsCommand(
    string ExternalSongId,
    string? SyncedLyrics,
    string? PlainLyrics,
    bool Instrumental,
    string? Source) : IRequest<Result<UpdateSongLyricsResponse>>;
