namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Wraps a single value in an <c>object[]</c> for use with xUnit's <c>[MemberData]</c>, keeping
/// validator test data tables readable.
/// </summary>
public static class ObjectArrayExtensions
{
    public static object[] ToObjectArray<T>(this T value) => new object[] { value! };
}
