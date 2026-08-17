namespace DotNet.Debugging.CorApi;

public readonly record struct CustomAttributeToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly CustomAttributeToken Nil = new CustomAttributeToken(201326592u);

    public CustomAttributeToken(uint value) {
        Value = value;
    }

    public static implicit operator CustomAttributeToken(int value) {
        return new CustomAttributeToken((uint)value);
    }

    public static implicit operator int(CustomAttributeToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(CustomAttributeToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(CustomAttributeToken value) {
        return new MetadataToken(value.Value);
    }
}