using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// A merge as the API sends it: <see cref="LoserSongId"/> is gone and became <see cref="SurvivorSongId"/>. The loser's
/// source id and the curator stay server-side.
/// </summary>
public record SongMergeDto(string LoserSongId, string SurvivorSongId, DateTime MergedAt, DateTime Version)
{
    public static SongMergeDto Create(SongMerge merge) => new(
        merge.LoserSongId,
        merge.SurvivorSongId,
        Utc(merge.MergedAt),
        Utc(merge.Version));

    // SQLite hands timestamps back with no kind; Postgres as UTC. Either way they were written as UTC.
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
