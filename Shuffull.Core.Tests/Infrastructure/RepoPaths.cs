namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>Paths in the source tree, for tests that read checked-in files rather than build output.</summary>
public static class RepoPaths
{
    /// <summary>The folder holding Shuffull.sln, found by walking up from the test binaries.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shuffull.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
