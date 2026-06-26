using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Shuffull.Api.Extensions;
using Shuffull.Api.Services;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Producer ingestion endpoint — the HTTP equivalent of the external-import folder drop. The YoutubeFunnel
/// POSTs a <see cref="SongImportDetails"/> JSON part plus the audio file; on a 2xx it deletes its local temp
/// copy. Authenticated by a static shared secret (config <c>Shuffull:Import:Key</c>, overridable via the
/// <c>Shuffull__Import__Key</c> environment variable) rather than a user token, since the caller is a service.
/// The folder importer stays in place during the transition; both funnel into <see cref="SongImportIntakeService"/>.
/// </summary>
[ApiController]
[Route("api/v1/song-imports")]
public class SongImportsController : ControllerBase
{
    private readonly SongImportIntakeService _intake;
    private readonly IConfiguration _configuration;

    public SongImportsController(SongImportIntakeService intake, IConfiguration configuration)
    {
        _intake = intake;
        _configuration = configuration;
    }

    [HttpPost]
    [RequestSizeLimit(100_000_000_000)]
    public async Task<IActionResult> Ingest(
        [FromForm] string details,
        IFormFile audio,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        var expectedKey = _configuration["Shuffull:Import:Key"];
        if (string.IsNullOrEmpty(expectedKey) || importKey != expectedKey)
        {
            return Unauthorized();
        }

        if (audio is null || audio.Length == 0)
        {
            return BadRequest("Missing audio file.");
        }

        SongImportDetails importDetails;
        try
        {
            importDetails = JsonConvert.DeserializeObject<SongImportDetails>(details)
                ?? throw new Exception("details deserialized to null.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Invalid details payload: {ex.Message}");
        }

        byte[] audioBytes;
        using (var memoryStream = new MemoryStream())
        {
            await audio.CopyToAsync(memoryStream, cancellationToken);
            audioBytes = memoryStream.ToArray();
        }

        // Returns 200 with the new SongImportId; SongImportService processes the queued row asynchronously.
        return this.ToActionResult(await _intake.CreateImportAsync(importDetails, audioBytes, cancellationToken));
    }
}
