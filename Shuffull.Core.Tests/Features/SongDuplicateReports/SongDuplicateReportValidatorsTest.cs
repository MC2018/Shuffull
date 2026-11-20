using Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;
using Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;

namespace Shuffull.Core.Tests.Features.SongDuplicateReports;

/// <summary>The request-level checks. Each report item is checked in its handler, so one bad item doesn't fail the batch.</summary>
public class SongDuplicateReportValidatorsTest
{
    private static readonly SongDuplicateReportItem Report = new("r1", "song-a", "song-b", "duplicate", null, null);

    [Fact]
    public void Report_Valid_Passes()
        => Assert.True(new ReportSongDuplicatesValidator().Validate(new ReportSongDuplicatesCommand("user", [Report])).IsValid);

    [Fact]
    public void Report_EmptyList_Passes() // the handler treats it as a no-op
        => Assert.True(new ReportSongDuplicatesValidator().Validate(new ReportSongDuplicatesCommand("user", [])).IsValid);

    [Fact]
    public void Report_NullList_Fails()
        => Assert.False(new ReportSongDuplicatesValidator().Validate(new ReportSongDuplicatesCommand("user", null!)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Report_MissingUser_Fails(string? userId)
        => Assert.False(new ReportSongDuplicatesValidator().Validate(new ReportSongDuplicatesCommand(userId!, [Report])).IsValid);

    [Theory]
    [InlineData(ReportSongDuplicatesHandler.MaxBatch, true)]
    [InlineData(ReportSongDuplicatesHandler.MaxBatch + 1, false)]
    public void Report_BatchCap(int count, bool valid)
    {
        var items = Enumerable.Range(0, count).Select(i => Report with { ReportId = $"r{i}" }).ToList();

        Assert.Equal(valid, new ReportSongDuplicatesValidator().Validate(new ReportSongDuplicatesCommand("user", items)).IsValid);
    }

    [Theory]
    [InlineData("r1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has\0nul", false)]
    public void Withdraw_ReportId(string? reportId, bool valid)
        => Assert.Equal(valid, new WithdrawSongDuplicateReportValidator().Validate(new WithdrawSongDuplicateReportCommand("user", reportId!)).IsValid);

    [Fact]
    public void Withdraw_ReportIdTooLong_Fails()
        => Assert.False(new WithdrawSongDuplicateReportValidator()
            .Validate(new WithdrawSongDuplicateReportCommand("user", new string('x', 65))).IsValid);

    [Theory]
    [InlineData("notDuplicate", "a", "b", true)]
    [InlineData("KEPTBOTH", "a", "b", true)]
    [InlineData("merged", "a", "b", false)]
    [InlineData("pending", "a", "b", false)]
    [InlineData("1", "a", "b", false)]
    [InlineData("keptBoth", "a", "a", false)]
    [InlineData("keptBoth", "", "b", false)]
    [InlineData("keptBoth", "a", null, false)]
    public void Resolve(string? resolution, string? songId, string? otherSongId, bool valid)
        => Assert.Equal(valid, new ResolveSongDuplicateReportsValidator()
            .Validate(new ResolveSongDuplicateReportsCommand("curator", songId, otherSongId, resolution)).IsValid);

    [Fact]
    public void Mine_Valid_Passes()
        => Assert.True(new GetMySongDuplicateReportsValidator().Validate(new GetMySongDuplicateReportsQuery("user", DateTime.MinValue)).IsValid);
}
