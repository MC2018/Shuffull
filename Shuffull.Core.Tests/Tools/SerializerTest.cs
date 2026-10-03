using Shuffull.Api.Tools;

namespace Shuffull.Core.Tests.Tools;

public class SerializerTest
{
    private record Sample(string SongId, string Name);

    [Fact]
    public void Serialize_UsesLowerCamelCasePropertyNames()
    {
        var json = Serializer.Serialize(new Sample("abc", "Hello"));

        Assert.Contains("\"songId\":\"abc\"", json);
        Assert.Contains("\"name\":\"Hello\"", json);
    }

    [Fact]
    public void Serialize_DoesNotThrowOnReferenceCycles()
    {
        // ReferenceHandler.IgnoreCycles is configured, so a self-referential object serializes without
        // throwing a cycle exception.
        var node = new CycleNode { Name = "root" };
        node.Self = node;

        var exception = Record.Exception(() => Serializer.Serialize(node));

        Assert.Null(exception);
    }

    [Fact]
    public void Serialize_NamingPolicyIsLowerCamelCasePolicyInstance()
    {
        Assert.IsType<LowerCamelCaseNamingPolicy>(Serializer.NamingPolicy);
    }

    private class CycleNode
    {
        public string Name { get; set; } = string.Empty;
        public CycleNode? Self { get; set; }
    }
}
