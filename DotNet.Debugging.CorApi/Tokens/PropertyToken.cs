namespace DotNet.Debugging.CorApi;

public readonly record struct PropertyToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly PropertyToken Nil = new PropertyToken(0x17000000u);

    public PropertyToken(uint value) {
        Value = value;
    }

    public static implicit operator PropertyToken(int value) {
        return new PropertyToken((uint)value);
    }

    public static implicit operator int(PropertyToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(PropertyToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(PropertyToken value) {
        return new MetadataToken(value.Value);
    }
}