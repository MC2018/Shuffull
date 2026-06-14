using TagLib;
using System.IO;

namespace Shuffull.Api.Tools.SongParsing;

public class ByteArrayAudioFileAbstraction(string name, byte[] data) : TagLib.File.IFileAbstraction
{
    private readonly MemoryStream _stream = new(data);

    public string Name { get; } = name;
    public Stream ReadStream => _stream;
    public Stream WriteStream => _stream;
    public void CloseStream(Stream stream) => stream.Dispose();
}
