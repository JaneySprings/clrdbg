namespace DotNet.Debugging.CorApi;

public readonly record struct GenericParamConstraintToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly GenericParamConstraintToken Nil = new GenericParamConstraintToken(738197504u);

    public GenericParamConstraintToken(uint value) {
        Value = value;
    }

    public static implicit operator GenericParamConstraintToken(int value) {
        return new GenericParamConstraintToken((uint)value);
    }

    public static implicit operator int(GenericParamConstraintToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(GenericParamConstraintToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(GenericParamConstraintToken value) {
        return new MetadataToken(value.Value);
    }
}