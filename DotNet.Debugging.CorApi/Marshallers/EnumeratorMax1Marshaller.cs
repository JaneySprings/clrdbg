using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[CustomMarshaller(typeof(uint), MarshalMode.Default, typeof(EnumeratorMax1Marshaller))]
internal static class EnumeratorMax1Marshaller {
    public static uint ConvertToUnmanaged(uint cMax) {
        return Validate(cMax);
    }

    public static uint ConvertToManaged(uint cMax) {
        return Validate(cMax);
    }

    private static uint Validate(uint cMax) {
        if (cMax != 1) {
            throw new ArgumentException("Because we decided to make the type of 'uint* rTypeDefs' be 'out Token rTypeDefs', cMax cannot be > 1", "cMax");
        }
        return cMax;
    }
}