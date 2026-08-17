namespace DotNet.Debugging.CorApi;

public readonly record struct AssemblyToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly AssemblyToken Nil = new AssemblyToken(536870912u);

    public AssemblyToken(uint value) {
        Value = value;
    }

    public static implicit operator AssemblyToken(int value) {
        return new AssemblyToken((uint)value);
    }

    public static implicit operator int(AssemblyToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(AssemblyToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(AssemblyToken value) {
        return new MetadataToken(value.Value);
    }
}