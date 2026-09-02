namespace DotNet.Debugging.CorApi;

public readonly record struct CordbAddress(ulong Value) {
    public static explicit operator int(CordbAddress value) {
        return (int)value.Value;
    }
}