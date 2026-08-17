namespace DotNet.Debugging.CorApi;

public readonly record struct MethodSpecToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly MethodSpecToken Nil = new MethodSpecToken(721420288u);

    public MethodSpecToken(uint value) {
        Value = value;
    }

    public static implicit operator MethodSpecToken(int value) {
        return new MethodSpecToken((uint)value);
    }

    public static implicit operator int(MethodSpecToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(MethodSpecToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(MethodSpecToken value) {
        return new MetadataToken(value.Value);
    }
}