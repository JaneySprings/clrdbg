namespace DotNet.Debugging.CorApi;

public readonly record struct SourceFileToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly SourceFileToken Nil = new SourceFileToken(0u);

    public SourceFileToken(uint value) {
        Value = value;
    }

    public static implicit operator SourceFileToken(int value) {
        return new SourceFileToken((uint)value);
    }

    public static implicit operator int(SourceFileToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(SourceFileToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(SourceFileToken value) {
        return new MetadataToken(value.Value);
    }
}