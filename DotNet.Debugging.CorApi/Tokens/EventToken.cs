namespace DotNet.Debugging.CorApi;

public readonly record struct EventToken {
    public int Rid => (int)(Value & 0xFFFFFF);

    public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

    public uint Value { get; }

    public bool IsNil => Rid == 0;

    public static readonly EventToken Nil = new EventToken(0x14000000u);

    public EventToken(uint value) {
        Value = value;
    }

    public static implicit operator EventToken(int value) {
        return new EventToken((uint)value);
    }

    public static implicit operator int(EventToken value) {
        return (int)value.Value;
    }

    public static implicit operator uint(EventToken value) {
        return value.Value;
    }

    public static implicit operator MetadataToken(EventToken value) {
        return new MetadataToken(value.Value);
    }
}