namespace DotNet.Debugging.CorApi;

public readonly record struct AssemblyRefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly AssemblyRefToken Nil = new AssemblyRefToken(587202560u);

    public AssemblyRefToken(uint value) {
        Value = value;
    }

    public static implicit operator AssemblyRefToken(int value) {
        return new AssemblyRefToken((uint)value);
    }

    public static implicit operator int(AssemblyRefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(AssemblyRefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(AssemblyRefToken value) {
        return new MetadataToken(value.Value);
    }
}