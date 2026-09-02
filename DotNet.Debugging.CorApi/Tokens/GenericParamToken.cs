namespace DotNet.Debugging.CorApi;

public readonly record struct GenericParamToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly GenericParamToken Nil = new GenericParamToken(0x2a000000u);

    public GenericParamToken(uint value) {
        Value = value;
    }

    public static implicit operator GenericParamToken(int value) {
        return new GenericParamToken((uint)value);
    }

    public static implicit operator int(GenericParamToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(GenericParamToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(GenericParamToken value) {
        return new MetadataToken(value.Value);
    }
}