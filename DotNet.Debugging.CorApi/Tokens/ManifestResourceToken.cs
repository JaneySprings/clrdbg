namespace DotNet.Debugging.CorApi;

public readonly record struct ManifestResourceToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly ManifestResourceToken Nil = new ManifestResourceToken(0x28000000u);

    public ManifestResourceToken(uint value) {
        Value = value;
    }

    public static implicit operator ManifestResourceToken(int value) {
        return new ManifestResourceToken((uint)value);
    }

    public static implicit operator int(ManifestResourceToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(ManifestResourceToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(ManifestResourceToken value) {
        return new MetadataToken(value.Value);
    }
}