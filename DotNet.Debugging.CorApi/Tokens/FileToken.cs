namespace DotNet.Debugging.CorApi;

public readonly record struct FileToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly FileToken Nil = new FileToken(637534208u);

    public FileToken(uint value) {
        Value = value;
    }

    public static implicit operator FileToken(int value) {
        return new FileToken((uint)value);
    }

    public static implicit operator int(FileToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(FileToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(FileToken value) {
        return new MetadataToken(value.Value);
    }
}