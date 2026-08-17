namespace DotNet.Debugging.CorApi;

public readonly record struct MetadataToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly MetadataToken Nil = new MetadataToken(0u);

    public MetadataToken(uint value) {
        Value = value;
    }

    public static implicit operator MetadataToken(int value) {
        return new MetadataToken((uint)value);
    }

    public static implicit operator int(MetadataToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(MetadataToken value) {
        return value.Value;
    }
}