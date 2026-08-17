namespace DotNet.Debugging.CorApi;

public readonly record struct StringToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly StringToken Nil = new StringToken(1879048192u);

    public StringToken(uint value) {
        Value = value;
    }

    public static implicit operator StringToken(int value) {
        return new StringToken((uint)value);
    }

    public static implicit operator int(StringToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(StringToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(StringToken value) {
        return new MetadataToken(value.Value);
    }
}