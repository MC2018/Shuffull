using Shuffull.Core.Features.Songs.RetagSongs;

namespace Shuffull.Api.Commands.Songs.RetagSongs;

/// <summary>Body for the batched re-tag: per-song items, each naming its engine tier ("weak" | "strong";
/// null = strong) — so one call can flush a mixed offline backlog of Keeps and like-promotions.</summary>
public record RetagSongsRequest(SongRetagItem[]? Items);
