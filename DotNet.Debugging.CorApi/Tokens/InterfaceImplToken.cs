namespace DotNet.Debugging.CorApi;

public readonly record struct InterfaceImplToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly InterfaceImplToken Nil = new InterfaceImplToken(150994944u);

    public InterfaceImplToken(uint value) {
        Value = value;
    }

    public static implicit operator InterfaceImplToken(int value) {
        return new InterfaceImplToken((uint)value);
    }

    public static implicit operator int(InterfaceImplToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(InterfaceImplToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(InterfaceImplToken value) {
        return new MetadataToken(value.Value);
    }
}