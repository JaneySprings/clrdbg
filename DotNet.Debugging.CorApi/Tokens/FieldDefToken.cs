namespace DotNet.Debugging.CorApi;

public readonly record struct FieldDefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly FieldDefToken Nil = new FieldDefToken(67108864u);

    public FieldDefToken(uint value) {
        Value = value;
    }

    public static implicit operator FieldDefToken(int value) {
        return new FieldDefToken((uint)value);
    }

    public static implicit operator int(FieldDefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(FieldDefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(FieldDefToken value) {
        return new MetadataToken(value.Value);
    }
}