namespace DotNet.Debugging.CorApi;

public readonly record struct MethodDefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly MethodDefToken Nil = new MethodDefToken(100663296u);

    public MethodDefToken(uint value) {
        Value = value;
    }

    public static implicit operator MethodDefToken(int value) {
        return new MethodDefToken((uint)value);
    }

    public static implicit operator int(MethodDefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(MethodDefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(MethodDefToken value) {
        return new MetadataToken(value.Value);
    }
}