using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Tools;

/// <summary>
/// Gets or creates tags by name, safe against another writer creating the same name at the same time (Shuffull#82).
/// </summary>
public static class TagStore
{
    /// <summary>
    /// Returns the stored tag for each of <paramref name="wanted"/>'s names, inserting any that don't exist yet. Call it
    /// inside the write transaction: each name is inserted with <c>ON CONFLICT DO NOTHING</c>, then every id is read back
    /// by name. A name another writer committed first is reused, whatever its id or type, instead of failing the unique
    /// index on <c>Tags.Name</c>. One still being written by an open transaction makes the insert wait for it.
    /// </summary>
    /// <param name="wanted">Untracked tags. Only their id (used if the name is new), name and type are read.</param>
    public static async Task<List<Tag>> GetOrCreateAsync(ShuffullContext context, IEnumerable<Tag> wanted, CancellationToken cancellationToken)
    {
        var tags = wanted.DistinctBy(t => t.Name).ToList();
        if (tags.Count == 0)
        {
            return [];
        }

        foreach (var tag in tags)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Tags" ("TagId", "Name", "Type") VALUES ({tag.TagId}, {tag.Name}, {(int)tag.Type}) ON CONFLICT DO NOTHING""",
                cancellationToken);
        }

        var names = tags.Select(t => t.Name).ToList();
        return await context.Tags.Where(t => names.Contains(t.Name)).ToListAsync(cancellationToken);
    }
}
