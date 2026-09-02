namespace DotNet.Debugging.CorApi;

public readonly record struct SignatureToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly SignatureToken Nil = new SignatureToken(0x11000000u);

    public SignatureToken(uint value) {
        Value = value;
    }

    public static implicit operator SignatureToken(int value) {
        return new SignatureToken((uint)value);
    }

    public static implicit operator int(SignatureToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(SignatureToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(SignatureToken value) {
        return new MetadataToken(value.Value);
    }
}