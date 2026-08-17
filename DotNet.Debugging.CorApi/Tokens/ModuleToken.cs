namespace DotNet.Debugging.CorApi;

public readonly record struct ModuleToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly ModuleToken Nil = new ModuleToken(0u);

    public ModuleToken(uint value) {
        Value = value;
    }

    public static implicit operator ModuleToken(int value) {
        return new ModuleToken((uint)value);
    }

    public static implicit operator int(ModuleToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(ModuleToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(ModuleToken value) {
        return new MetadataToken(value.Value);
    }
}