using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Replacements.FlagSongReplacement;

/// <summary>App: flag a poor-quality song so the producer re-sources it. See <see cref="FlagSongReplacementHandler"/>.</summary>
public record FlagSongReplacementCommand(string SongId, string? Note) : IRequest<Result<FlagSongReplacementResponse>>;
