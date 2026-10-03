using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Songs.UploadSongs;
using Shuffull.Api.Extensions;

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

    // TODO: When the dedicated website is built, authenticate this endpoint and derive the user from
    // the bearer token instead of accepting a username in the form body.
    [HttpPost]
    [RequestSizeLimit(100_000_000_000)]
    public async Task<IActionResult> Upload([FromForm] string username, [FromForm] string? playlistName, [FromForm] IEnumerable<IFormFile> files)
    {
        var result = await new UploadSongHandler(_services).Handle(new UploadSongRequest(username, playlistName, files));
        return this.ToActionResult(result);
    }
}
