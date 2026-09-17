using System.Buffers.Binary;
using System.Text;

namespace DotNet.Debugging.Remote.Protocol;

public class ByteReader {
    private readonly byte[] bytes;
    private int position;

    public ByteReader(byte[] bytes, int position = 0) {
        this.bytes = bytes;
        this.position = position;
    }

    public byte ReadByte() {
        return bytes[position++];
    }
    public ushort ReadUInt16() {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position));
        position += 2;
        return value;
    }
    public uint ReadUInt32() {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position));
        position += 4;
        return value;
    }
    public ulong ReadUInt64() {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position));
        position += 8;
        return value;
    }
    public int ReadInt32() {
        return unchecked((int)ReadUInt32());
    }
    // Everything from the position on
    public byte[] ReadRemaining() {
        return ReadBytes(bytes.Length - position);
    }
    public byte[] ReadBytes(int count) {
        var value = new byte[count];
        Array.Copy(bytes, position, value, 0, count);
        position += count;
        return value;
    }
    // A 16-bit byte count and UTF-8
    public string ReadString() {
        var length = ReadUInt16();
        var value = Encoding.UTF8.GetString(bytes, position, length);
        position += length;
        return value;
    }
    // A 32-bit character count and UTF-16, the runtime's WCHAR
    public string ReadWide() {
        var count = checked((int)ReadUInt32());
        var value = Encoding.Unicode.GetString(bytes, position, count * 2);
        position += count * 2;
        return value;
    }
}
