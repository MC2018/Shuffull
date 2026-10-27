using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Songs.UploadSongs;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Multipart upload endpoint that drops user-supplied audio files into the SongImport pipeline. This
/// replaces the old server-rendered MVC upload page (there is no UI anymore); a dedicated website will
/// consume this endpoint later.
/// </summary>
[ApiController]
[Route("api/v1/song-uploads")]
public class SongUploadsController : ControllerBase
{
    private readonly IServiceProvider _services;

    public SongUploadsController(IServiceProvider services)
    {
        _services = services;
    }

    // The uploader is whoever the token says, never a name in the form: the tunnel exposes this path. The
    // limit bounds what one request can write to the box's disk (Cloudflare stops a single request at 100 MB
    // anyway; the headroom is for uploads made on the local network).
    [HttpPost]
    [Authorize]
    [RequestSizeLimit(1_000_000_000)]
    public async Task<IActionResult> Upload([FromForm] string? playlistName, [FromForm] IEnumerable<IFormFile> files)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        var result = await new UploadSongHandler(_services).Handle(new UploadSongRequest(user.UserId, playlistName, files));
        return this.ToActionResult(result);
    }
}
