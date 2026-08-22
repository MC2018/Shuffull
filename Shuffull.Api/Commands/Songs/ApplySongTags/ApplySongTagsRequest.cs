using Shuffull.Metadata.Models;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Producer write-back body. <see cref="Tags"/> is the engine's own output shape (shared via Shuffull.Metadata,
/// so the funnel sends exactly what it already produces), and <see cref="TagModel"/> is the model that made
/// them — required, because without provenance the song stays "behind" and re-queues forever.
/// </summary>
public record ApplySongTagsRequest(GeneratedSongTags Tags, string TagModel);
