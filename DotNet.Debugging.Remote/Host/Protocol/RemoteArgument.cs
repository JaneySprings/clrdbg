namespace DotNet.Debugging.Remote.Protocol;

// One argument of a remote call: a value or an object handle passed in, or storage for what the callee writes, which
// comes back in the response in argument order (zeroed when the call failed). A handle is passed with the interface
// the callee expects it as. An object coming back is asked, in the same request, which of the probe interfaces it
// has, so the host picks its proxy class without another round trip. The names' ULONG32 cch / ULONG32* pcch / WCHAR
// buffer pattern is one 'OutText' argument ('OutTextBuffer' for the metadata API's buffer, capacity, length order);
// a pointer the callee hands out into its own memory plus a count is one 'OutBlob'; an array of records holding
// interface pointers is one 'OutRecords', whose pointers come back as handles
public class RemoteArgument {
    private readonly RemoteArgumentKind kind;
    private readonly ulong value;
    private readonly Guid iid;
    private readonly uint minimum;
    private readonly uint recordSize;
    private readonly uint[] handles;
    private readonly uint[] offsets;
    private readonly byte[] bytes;
    private readonly string text;
    private readonly Guid[] probes;
    private uint count;

    public RemoteArgumentKind Kind => kind;
    // OutText, OutTextBuffer: the buffer the callee gets, in characters
    public uint Capacity => count;
    public bool IsText => kind == RemoteArgumentKind.OutText || kind == RemoteArgumentKind.OutTextBuffer;

    public uint UInt32Result { get; private set; }
    public ulong UInt64Result { get; private set; }
    public uint HandleResult { get; private set; }
    // OutObject: which of the probes the object has, in their order (empty without probes or for handle 0)
    public bool[] FlagsResult { get; private set; }
    public uint[] HandlesResult { get; private set; }
    public bool[][] FlagsResults { get; private set; }
    // OutText: the length the callee reported, terminator included, and what fit into the buffer
    public uint LengthResult { get; private set; }
    public string TextResult { get; private set; }
    public byte[] BytesResult { get; private set; }
    public Guid GuidResult { get; private set; }

    private RemoteArgument(RemoteArgumentKind kind, ulong value = 0, Guid iid = default, uint count = 0, uint[]? handles = null, byte[]? bytes = null, string? text = null, uint minimum = 0, Guid[]? probes = null, uint recordSize = 0, uint[]? offsets = null) {
        this.kind = kind;
        this.value = value;
        this.iid = iid;
        this.count = count;
        this.minimum = minimum;
        this.recordSize = recordSize;
        this.handles = handles ?? [];
        this.bytes = bytes ?? [];
        this.text = text ?? string.Empty;
        this.probes = probes ?? [];
        this.offsets = offsets ?? [];
        FlagsResult = [];
        HandlesResult = [];
        FlagsResults = [];
        TextResult = string.Empty;
        BytesResult = [];
    }

    public static RemoteArgument UInt32(uint value) {
        return new RemoteArgument(RemoteArgumentKind.UInt32, value);
    }
    public static RemoteArgument UInt64(ulong value) {
        return new RemoteArgument(RemoteArgumentKind.UInt64, value);
    }
    public static RemoteArgument Bool(bool value) {
        return UInt32(value ? 1u : 0u);
    }
    public static RemoteArgument Object(uint handle, Guid iid) {
        return new RemoteArgument(RemoteArgumentKind.Object, handle, iid);
    }
    public static RemoteArgument Objects(uint[] handles, Guid iid) {
        return new RemoteArgument(RemoteArgumentKind.Objects, 0, iid, (uint)handles.Length, handles);
    }
    public static RemoteArgument Bytes(byte[] data) {
        return new RemoteArgument(RemoteArgumentKind.Bytes, 0, default, (uint)data.Length, null, data);
    }
    public static RemoteArgument Text(string value) {
        return new RemoteArgument(RemoteArgumentKind.Text, 0, default, (uint)value.Length, null, null, value);
    }
    public static RemoteArgument Iid(Guid value) {
        return new RemoteArgument(RemoteArgumentKind.Guid, 0, value);
    }
    public static RemoteArgument OutUInt32() {
        return new RemoteArgument(RemoteArgumentKind.OutUInt32);
    }
    public static RemoteArgument OutUInt64() {
        return new RemoteArgument(RemoteArgumentKind.OutUInt64);
    }
    // 'probes' are the interfaces to ask the returned object for, null when its proxy class is known from the type
    public static RemoteArgument OutObject(Guid[]? probes = null) {
        return new RemoteArgument(RemoteArgumentKind.OutObject, probes: probes);
    }
    public static RemoteArgument OutObjects(uint capacity, Guid[]? probes = null) {
        return new RemoteArgument(RemoteArgumentKind.OutObjects, 0, default, capacity, probes: probes);
    }
    public static RemoteArgument OutText(uint capacity) {
        return new RemoteArgument(RemoteArgumentKind.OutText, 0, default, capacity);
    }
    public static RemoteArgument OutBytes(uint length) {
        return new RemoteArgument(RemoteArgumentKind.OutBytes, 0, default, length);
    }
    public static RemoteArgument OutTextBuffer(uint capacity) {
        return new RemoteArgument(RemoteArgumentKind.OutTextBuffer, 0, default, capacity);
    }
    public static RemoteArgument RefUInt64(ulong value) {
        return new RemoteArgument(RemoteArgumentKind.RefUInt64, value);
    }
    // 'unit' is the byte size of what the callee counts (1 for a byte count, 2 for a character count), 'minimum' the
    // least to copy when the pointer is set, for a constant whose size the count does not tell
    public static RemoteArgument OutBlob(uint unit, uint minimum) {
        return new RemoteArgument(RemoteArgumentKind.OutBlob, 0, default, unit, null, null, null, minimum);
    }
    public static RemoteArgument OutGuid() {
        return new RemoteArgument(RemoteArgumentKind.OutGuid);
    }
    // 'count' records of 'recordSize' bytes, with an interface pointer at each of the 'pointerOffsets' in a record;
    // the bytes come back with each pointer replaced by its handle, as a 64-bit value
    public static RemoteArgument OutRecords(uint count, uint recordSize, uint[] pointerOffsets) {
        return new RemoteArgument(RemoteArgumentKind.OutRecords, 0, default, count, recordSize: recordSize, offsets: pointerOffsets);
    }

    // The argument for a log line: its kind and what it carries in
    public override string ToString() {
        switch (kind) {
            case RemoteArgumentKind.UInt32:
            case RemoteArgumentKind.UInt64:
            case RemoteArgumentKind.RefUInt64:
                return $"{kind}:{value}";
            case RemoteArgumentKind.Object:
                return $"Object:{value}";
            case RemoteArgumentKind.Objects:
                return $"Objects:[{string.Join(",", handles)}]";
            case RemoteArgumentKind.Bytes:
                return $"Bytes:{Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32))}";
            case RemoteArgumentKind.Text:
                return $"Text:{text}";
            case RemoteArgumentKind.Guid:
                return $"Guid:{iid}";
            default:
                return count != 0 ? $"{kind}({count})" : kind.ToString();
        }
    }
    // A wider buffer than the caller asked for, so the text comes along with its length in one round trip
    internal void Widen(uint capacity) {
        if (IsText && count < capacity)
            count = capacity;
    }
    internal void Write(ByteWriter writer) {
        writer.WriteByte((byte)kind);
        switch (kind) {
            case RemoteArgumentKind.UInt32:
                writer.WriteUInt32((uint)value);
                break;
            case RemoteArgumentKind.UInt64:
            case RemoteArgumentKind.RefUInt64:
                writer.WriteUInt64(value);
                break;
            case RemoteArgumentKind.Object:
                writer.WriteUInt32((uint)value);
                writer.WriteGuid(iid);
                break;
            case RemoteArgumentKind.Objects:
                writer.WriteUInt32(count);
                writer.WriteGuid(iid);
                foreach (var handle in handles)
                    writer.WriteUInt32(handle);
                break;
            case RemoteArgumentKind.Bytes:
                writer.WriteUInt32(count);
                writer.WriteBytes(bytes);
                break;
            case RemoteArgumentKind.Text:
                writer.WriteWide(text);
                break;
            case RemoteArgumentKind.Guid:
                writer.WriteGuid(iid);
                break;
            case RemoteArgumentKind.OutObject:
                WriteProbes(writer);
                break;
            case RemoteArgumentKind.OutObjects:
                writer.WriteUInt32(count);
                WriteProbes(writer);
                break;
            case RemoteArgumentKind.OutText:
            case RemoteArgumentKind.OutBytes:
            case RemoteArgumentKind.OutTextBuffer:
                writer.WriteUInt32(count);
                break;
            case RemoteArgumentKind.OutBlob:
                writer.WriteUInt32(count);
                writer.WriteUInt32(minimum);
                break;
            case RemoteArgumentKind.OutRecords:
                writer.WriteUInt32(count);
                writer.WriteUInt32(recordSize);
                writer.WriteByte(checked((byte)offsets.Length));
                foreach (var offset in offsets)
                    writer.WriteUInt32(offset);
                break;
        }
    }
    // What tells one call from another: everything the request carries but the size of a text buffer
    internal void WriteKey(ByteWriter writer) {
        if (IsText)
            writer.WriteByte((byte)kind);
        else
            Write(writer);
    }
    internal void Read(ByteReader reader) {
        switch (kind) {
            case RemoteArgumentKind.OutUInt32:
                UInt32Result = reader.ReadUInt32();
                break;
            case RemoteArgumentKind.OutUInt64:
            case RemoteArgumentKind.RefUInt64:
                UInt64Result = reader.ReadUInt64();
                break;
            case RemoteArgumentKind.OutObject:
                HandleResult = reader.ReadUInt32();
                FlagsResult = ReadFlags(reader);
                break;
            case RemoteArgumentKind.OutText:
            case RemoteArgumentKind.OutTextBuffer:
                LengthResult = reader.ReadUInt32();
                TextResult = reader.ReadWide();
                break;
            case RemoteArgumentKind.OutBlob:
                UInt32Result = reader.ReadUInt32();
                BytesResult = reader.ReadBytes(checked((int)reader.ReadUInt32()));
                break;
            case RemoteArgumentKind.OutGuid:
                GuidResult = new Guid(reader.ReadBytes(16));
                break;
            case RemoteArgumentKind.OutBytes:
            case RemoteArgumentKind.OutRecords:
                BytesResult = reader.ReadBytes(checked((int)reader.ReadUInt32()));
                break;
            case RemoteArgumentKind.OutObjects: {
                var fetched = new uint[reader.ReadUInt32()];
                var flags = new bool[fetched.Length][];
                for (var i = 0; i < fetched.Length; i++) {
                    fetched[i] = reader.ReadUInt32();
                    flags[i] = ReadFlags(reader);
                }
                HandlesResult = fetched;
                FlagsResults = flags;
                break;
            }
        }
    }

    private void WriteProbes(ByteWriter writer) {
        writer.WriteByte(checked((byte)probes.Length));
        foreach (var probe in probes)
            writer.WriteGuid(probe);
    }
    private bool[] ReadFlags(ByteReader reader) {
        var flags = new bool[probes.Length];
        for (var i = 0; i < flags.Length; i++)
            flags[i] = reader.ReadByte() != 0;
        return flags;
    }
}
