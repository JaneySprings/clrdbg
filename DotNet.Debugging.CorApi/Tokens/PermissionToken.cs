namespace DotNet.Debugging.CorApi;

public readonly record struct PermissionToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly PermissionToken Nil = new PermissionToken(234881024u);

    public PermissionToken(uint value) {
        Value = value;
    }

    public static implicit operator PermissionToken(int value) {
        return new PermissionToken((uint)value);
    }

    public static implicit operator int(PermissionToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(PermissionToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(PermissionToken value) {
        return new MetadataToken(value.Value);
    }
}