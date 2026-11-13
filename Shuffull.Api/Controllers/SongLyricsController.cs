using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Songs.UpdateSongLyrics;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Songs.UpdateSongLyrics;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Producer-facing lyrics delivery for anything the producer resolves after a song's import (see
/// <see cref="UpdateSongLyricsHandler"/>). Shared-secret guarded like the other producer endpoints.
/// </summary>
[ApiController]
[ProducerKey]
[Route("api/v1/song-lyrics")]
public class SongLyricsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SongLyricsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Attaches lyrics to the song imported from a given YouTube video id. Bumps <c>Song.Version</c> so every
    /// client picks them up on its next incremental sync — no app change needed.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateLyrics([FromBody] UpdateSongLyricsRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new UpdateSongLyricsCommand(request.ExternalSongId,
            request.SyncedLyrics, request.PlainLyrics, request.Instrumental, request.Source), cancellationToken));
}
