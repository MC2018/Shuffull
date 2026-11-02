namespace Shuffull.Core.Tools;

public static class IdGenerator
{
    /// <summary>A new lowercased ULID. Ids are stored and synced to the app, so the format must not change.</summary>
    public static string Generate()
    {
        return Ulid.NewUlid().ToString().ToLowerInvariant();
    }
}
