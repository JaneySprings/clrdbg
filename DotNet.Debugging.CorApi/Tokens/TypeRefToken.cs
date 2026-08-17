namespace DotNet.Debugging.CorApi;

public readonly record struct TypeRefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly TypeRefToken Nil = new TypeRefToken(16777216u);

    public TypeRefToken(uint value) {
        Value = value;
    }

    public static implicit operator TypeRefToken(int value) {
        return new TypeRefToken((uint)value);
    }

    public static implicit operator int(TypeRefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(TypeRefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(TypeRefToken value) {
        return new MetadataToken(value.Value);
    }
}