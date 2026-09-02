namespace DotNet.Debugging.CorApi;

public readonly record struct TypeDefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly TypeDefToken Nil = new TypeDefToken(0x02000000u);

    public TypeDefToken(uint value) {
        Value = value;
    }

    public static implicit operator TypeDefToken(int value) {
        return new TypeDefToken((uint)value);
    }

    public static implicit operator int(TypeDefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(TypeDefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(TypeDefToken value) {
        return new MetadataToken(value.Value);
    }
}