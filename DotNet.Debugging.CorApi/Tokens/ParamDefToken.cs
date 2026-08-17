namespace DotNet.Debugging.CorApi;

public readonly record struct ParamDefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly ParamDefToken Nil = new ParamDefToken(134217728u);

    public ParamDefToken(uint value) {
        Value = value;
    }

    public static implicit operator ParamDefToken(int value) {
        return new ParamDefToken((uint)value);
    }

    public static implicit operator int(ParamDefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(ParamDefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(ParamDefToken value) {
        return new MetadataToken(value.Value);
    }
}