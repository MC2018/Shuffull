namespace Shuffull.Core.Errors;

/// <summary>A <c>Result</c> error that carries its <see cref="ErrorKind"/>. The message is what the API returns.</summary>
public sealed class KindedError(ErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ErrorKind Kind { get; } = kind;
}
