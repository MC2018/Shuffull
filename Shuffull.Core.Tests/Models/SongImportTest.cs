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

    [Fact]
    public void RecordTagFailure_BacksOffExponentiallyCappedAtAnHour()
    {
        var import = NewSongImport();
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var expectedMinutes = new[] { 1, 2, 4, 8, 16, 32, 60, 60 };

        foreach (var minutes in expectedMinutes)
        {
            Assert.True(import.RecordTagFailure(now).IsOk);
            Assert.Equal(now.AddMinutes(minutes), import.NextAttemptAt);
            Assert.Equal(now, import.LastUpdatedAt);
        }

        // A tag failure is a retry, not a terminal state: the row stays in the queue.
        Assert.Equal(SongImportState.ReadyForImporting, import.State);
        Assert.Equal(expectedMinutes.Length, import.FailedTagAttempts);
    }

    [Fact]
    public void IsLastTagAttempt_TurnsTrueOnTheFinalAllowedAttempt()
    {
        var import = NewSongImport();
        for (var i = 0; i < SongImport.MaxTagAttempts - 1; i++)
        {
            Assert.False(import.IsLastTagAttempt);
            import.RecordTagFailure(DateTime.UtcNow);
        }

        Assert.True(import.IsLastTagAttempt);
    }

    [Fact]
    public void RecordTagFailure_WhenAlreadyTransitioned_ReturnsError()
    {
        var import = NewSongImport();
        Assert.True(import.SetState(SongImportState.Completed).IsOk);

        Assert.True(import.RecordTagFailure(DateTime.UtcNow).IsError);
        Assert.Equal(0, import.FailedTagAttempts);
        Assert.Null(import.NextAttemptAt);
    }
}
