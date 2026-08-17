namespace DotNet.Debugging.CorApi;

public readonly record struct ModuleRefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly ModuleRefToken Nil = new ModuleRefToken(436207616u);

    public ModuleRefToken(uint value) {
        Value = value;
    }

    public static implicit operator ModuleRefToken(int value) {
        return new ModuleRefToken((uint)value);
    }

    public static implicit operator int(ModuleRefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(ModuleRefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(ModuleRefToken value) {
        return new MetadataToken(value.Value);
    }
}