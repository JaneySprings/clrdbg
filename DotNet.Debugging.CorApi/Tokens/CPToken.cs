namespace DotNet.Debugging.CorApi;

public readonly record struct CPToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly CPToken Nil = new CPToken(0u);

    public CPToken(uint value) {
        Value = value;
    }

    public static implicit operator CPToken(int value) {
        return new CPToken((uint)value);
    }

    public static implicit operator int(CPToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(CPToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(CPToken value) {
        return new MetadataToken(value.Value);
    }
}