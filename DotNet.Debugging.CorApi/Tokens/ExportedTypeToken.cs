namespace DotNet.Debugging.CorApi;

public readonly record struct ExportedTypeToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly ExportedTypeToken Nil = new ExportedTypeToken(0x27000000u);

    public ExportedTypeToken(uint value) {
        Value = value;
    }

    public static implicit operator ExportedTypeToken(int value) {
        return new ExportedTypeToken((uint)value);
    }

    public static implicit operator int(ExportedTypeToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(ExportedTypeToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(ExportedTypeToken value) {
        return new MetadataToken(value.Value);
    }
}