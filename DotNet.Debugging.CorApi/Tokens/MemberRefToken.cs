namespace DotNet.Debugging.CorApi;

public readonly record struct MemberRefToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly MemberRefToken Nil = new MemberRefToken(0x0a000000u);

    public MemberRefToken(uint value) {
        Value = value;
    }

    public static implicit operator MemberRefToken(int value) {
        return new MemberRefToken((uint)value);
    }

    public static implicit operator int(MemberRefToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(MemberRefToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(MemberRefToken value) {
        return new MetadataToken(value.Value);
    }
}