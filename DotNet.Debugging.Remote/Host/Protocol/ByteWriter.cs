using System.Buffers.Binary;
using System.Text;

namespace DotNet.Debugging.Remote.Protocol;

// Little-endian encoding of the wire types: fixed-size integers, a string as a 16-bit byte count plus UTF-8, and a
// wide string as a 32-bit character count plus UTF-16, the runtime's WCHAR
public class ByteWriter {
    private readonly MemoryStream stream;

    public ByteWriter() {
        stream = new MemoryStream();
    }

    public void WriteByte(byte value) {
        stream.WriteByte(value);
    }
    public void WriteUInt16(ushort value) {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }
    public void WriteUInt32(uint value) {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
    public void WriteUInt64(ulong value) {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }
    public void WriteGuid(Guid value) {
        Span<byte> buffer = stackalloc byte[16];
        value.TryWriteBytes(buffer);
        stream.Write(buffer);
    }
    public void WriteBytes(ReadOnlySpan<byte> value) {
        stream.Write(value);
    }
    public void WriteString(string value) {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt16(checked((ushort)bytes.Length));
        stream.Write(bytes);
    }
    public void WriteWide(string value) {
        WriteUInt32((uint)value.Length);
        stream.Write(Encoding.Unicode.GetBytes(value));
    }
    public byte[] ToArray() {
        return stream.ToArray();
    }
}
