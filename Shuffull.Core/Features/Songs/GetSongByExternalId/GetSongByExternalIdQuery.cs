using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSongByExternalId;

/// <summary>
/// Producer lookup: which Shuffull song was imported from a given external (YouTube video) id. The funnel only
/// knows songs by their source video id, but replace-in-place (<c>SongImportDetails.ReplacesSongId</c>) needs
/// our <c>SongId</c> — this is the bridge, so the funnel can promote a duplicate candidate into a replacement
/// of the song it matched.
/// </summary>
public record GetSongByExternalIdQuery(string ExternalSongId) : IRequest<Result<GetSongByExternalIdResponse>>;
