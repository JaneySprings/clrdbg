using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class MetaDataTables2Extensions {
    public static (nint ppvMd, int pcbMd) GetMetaDataStorage(this IMetaDataTables2 instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetMetaDataStorage(out var ppvMd, out var pcbMd));
        return (ppvMd: ppvMd, pcbMd: checked((int)pcbMd));
    }
}