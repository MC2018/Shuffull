using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Tests.Models;

public class SongImportTest
{
    private static SongImport NewSongImport() => new()
    {
        SongImportId = "import-1",
        Name = "Song",
        ImportFolder = "folder-1",
        FileType = ".mp3",
        UserId = "user-1",
        LastUpdatedAt = DateTime.UtcNow.AddDays(-1),
    };

    [Fact]
    public void NewSongImport_DefaultsToReadyForImporting()
    {
        Assert.Equal(SongImportState.ReadyForImporting, NewSongImport().State);
    }

    [Theory]
    [InlineData(SongImportState.Completed)]
    [InlineData(SongImportState.Failed)]
    public void SetState_FromReadyForImporting_TransitionsAndStampsLastUpdated(SongImportState target)
    {
        var import = NewSongImport();
        var before = import.LastUpdatedAt;

        var result = import.SetState(target);

        Assert.True(result.IsOk);
        Assert.Equal(target, import.State);
        Assert.True(import.LastUpdatedAt > before);
    }

    [Fact]
    public void SetState_WhenAlreadyTransitioned_ReturnsErrorAndKeepsState()
    {
        // The guard makes the transition one-way: once it leaves ReadyForImporting, further sets are rejected.
        var import = NewSongImport();
        Assert.True(import.SetState(SongImportState.Completed).IsOk);

        var result = import.SetState(SongImportState.Failed);

        Assert.True(result.IsError);
        Assert.Contains("Cannot set state", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SongImportState.Completed, import.State);
    }

    [Fact]
    public void FilePathHelpers_ComposeFromImportFolderAndFileName()
    {
        var import = NewSongImport();

        Assert.Equal("import-1.mp3", import.FileName);
        Assert.Equal(Path.Combine("root", "folder-1"), import.GetImportFolderPath("root"));
        Assert.Equal(Path.Combine("root", "folder-1", "import-1.mp3"), import.GetFilePath("root"));
    }
}
