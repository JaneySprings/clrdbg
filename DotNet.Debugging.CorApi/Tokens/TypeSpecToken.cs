namespace DotNet.Debugging.CorApi;

public readonly record struct TypeSpecToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly TypeSpecToken Nil = new TypeSpecToken(452984832u);

    public TypeSpecToken(uint value) {
        Value = value;
    }

    public static implicit operator TypeSpecToken(int value) {
        return new TypeSpecToken((uint)value);
    }

    public static implicit operator int(TypeSpecToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(TypeSpecToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(TypeSpecToken value) {
        return new MetadataToken(value.Value);
    }
}