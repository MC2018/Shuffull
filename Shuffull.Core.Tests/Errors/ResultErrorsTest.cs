using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Errors;

namespace Shuffull.Core.Tests.Errors;

/// <summary>
/// What kind a thrown exception becomes, and so what status the app's outbox sees (Shuffull#41). A 5xx makes the app
/// retry, so only a fault that can clear up by itself may become <see cref="ErrorKind.ServerFault"/>.
/// </summary>
public class ResultErrorsTest
{
    /// <summary>Stands in for NpgsqlException/PostgresException, which report the SQLSTATE the same way.</summary>
    private sealed class FakeDbException(string? sqlState) : DbException("db said no")
    {
        public override string? SqlState { get; } = sqlState;
    }

    private static DbUpdateException SaveFailure(Exception inner) => new("An error occurred while saving.", inner);

    [Theory]
    [InlineData("23505", ErrorKind.Conflict)]    // unique violation
    [InlineData("23503", ErrorKind.Conflict)]    // foreign key violation
    [InlineData("22001", ErrorKind.Invalid)]     // value too long
    [InlineData("54000", ErrorKind.Invalid)]     // index row too big: the request's value, every replay
    [InlineData("42703", ErrorKind.Invalid)]     // undefined column: fails every replay too
    [InlineData("08006", ErrorKind.ServerFault)] // connection failure
    [InlineData("40P01", ErrorKind.ServerFault)] // deadlock
    [InlineData("40001", ErrorKind.ServerFault)] // serialization failure
    [InlineData("53300", ErrorKind.ServerFault)] // too many connections
    [InlineData("55P03", ErrorKind.ServerFault)] // lock wait timed out
    [InlineData("57P01", ErrorKind.ServerFault)] // admin shutdown
    [InlineData("57014", ErrorKind.ServerFault)] // statement timeout
    [InlineData(null, ErrorKind.ServerFault)]    // a driver error with no SQLSTATE
    public void DatabaseError_IsClassifiedBySqlState(string? sqlState, ErrorKind expected)
    {
        var error = ResultErrors.FromException(SaveFailure(new FakeDbException(sqlState)), "Error saving changes");

        Assert.Equal(expected, ResultErrors.KindOf(error));
        Assert.Equal("Error saving changes", error.Message);
    }

    [Fact]
    public void DatabaseError_ThrownDirectly_IsClassifiedToo()
    {
        Assert.Equal(ErrorKind.ServerFault, ResultErrors.KindOf(ResultErrors.FromException(new FakeDbException("08001"), "m")));
    }

    [Fact]
    public void Timeout_IsAServerFault()
    {
        var error = ResultErrors.FromException(new InvalidOperationException("wrapped", new TimeoutException()), "m");

        Assert.Equal(ErrorKind.ServerFault, ResultErrors.KindOf(error));
    }

    [Fact]
    public void SaveFailureWithoutADriverError_TakesTheCallersDefault()
    {
        // e.g. the driver refusing a non-UTC timestamp: every retry fails the same way, so it must not be a 5xx.
        var error = ResultErrors.FromException(
            new DbUpdateException("An error occurred while saving.", new ArgumentException("Cannot write DateTime with Kind=Local")), "m");

        Assert.Equal(ErrorKind.Invalid, ResultErrors.KindOf(error));
    }

    [Fact]
    public void ConcurrencyFailure_IsAConflict()
    {
        var error = ResultErrors.FromException(new DbUpdateConcurrencyException("row changed"), "m");

        Assert.Equal(ErrorKind.Conflict, ResultErrors.KindOf(error));
    }

    [Fact]
    public void NonDatabaseException_TakesTheCallersDefault()
    {
        var thrown = new InvalidOperationException("bad state");

        Assert.Equal(ErrorKind.Invalid, ResultErrors.KindOf(ResultErrors.FromException(thrown, "m")));
        Assert.Equal(ErrorKind.ServerFault, ResultErrors.KindOf(ResultErrors.FromException(thrown, "m", ErrorKind.ServerFault)));
    }

    [Fact]
    public void KindedError_KeepsItsKind()
    {
        var error = ResultErrors.FromException(ResultErrors.NotFound("gone"), "Error retrieving entity");

        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(error));
    }

    [Fact]
    public void PlainError_IsInvalid_AsEveryErrorWasBefore()
    {
        Assert.Equal(ErrorKind.Invalid, ResultErrors.KindOf(new Exception("anything")));
    }

    [Fact]
    public void WithMessage_KeepsTheKind_AndWrapsTheOriginal()
    {
        var original = ResultErrors.FromException(new FakeDbException("08006"), "connection lost");

        var renamed = original.WithMessage("Could not record the keep: connection lost");

        Assert.Equal(ErrorKind.ServerFault, ResultErrors.KindOf(renamed));
        Assert.Equal("Could not record the keep: connection lost", renamed.Message);
        Assert.Same(original, renamed.InnerException);
    }

    [Fact]
    public void NotFoundAs_RenamesOnlyANotFound()
    {
        var missing = ResultErrors.NotFound("Entity with ID x not found.").NotFoundAs("Playlist not found.");
        var fault = ResultErrors.FromException(new FakeDbException("08006"), "Error retrieving entity: db said no");

        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(missing));
        Assert.Equal("Playlist not found.", missing.Message);
        // A database fault must not be reported as "not found": it stays the fault, with its own message.
        Assert.Same(fault, fault.NotFoundAs("Playlist not found."));
    }
}
