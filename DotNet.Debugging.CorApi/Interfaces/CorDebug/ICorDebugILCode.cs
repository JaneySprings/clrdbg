using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilcode-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("598D46C2-C877-42A7-89D2-3D0C7F1C1264")]
public partial interface ICorDebugILCode {
    [PreserveSig]
    int TryGetEHClauses(uint cClauses, out uint pcClauses, [Out][MarshalUsing(CountElementName = "cClauses")] CorDebugEHClause[]? clauses);
}