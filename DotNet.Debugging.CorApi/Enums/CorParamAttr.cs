namespace DotNet.Debugging.CorApi;

public enum CorParamAttr {
    pdIn = 1,
    pdOut = 2,
    pdOptional = 16,
    pdReservedMask = 61440,
    pdHasDefault = 4096,
    pdHasFieldMarshal = 8192,
    pdUnused = 53216
}