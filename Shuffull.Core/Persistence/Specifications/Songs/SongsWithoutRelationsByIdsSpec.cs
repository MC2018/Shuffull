using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// The songs whose id is in the supplied set, with no artists or tags loaded. For callers that only need to know
/// which ids are real songs; <see cref="SongsByIdsSpec"/> is the one to project to a DTO.
/// </summary>
public class SongsWithoutRelationsByIdsSpec(IReadOnlyCollection<string> songIds) : BaseSpecification<Song>
{
    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => query.Where(song => songIds.Contains(song.SongId));
}
