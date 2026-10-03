using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;

/// <summary>A single requested last-played update for one of the user's songs.</summary>
public record SongLastPlayed(string SongId, DateTime LastPlayed);

public record UpdateSongsLastPlayedCommand(string UserId, IReadOnlyList<SongLastPlayed> Updates)
    : IRequest<Result<UpdateSongsLastPlayedResponse>>;
