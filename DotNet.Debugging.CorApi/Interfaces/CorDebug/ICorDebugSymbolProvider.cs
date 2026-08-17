using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugsymbolprovider-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3948A999-FD8A-4C38-A708-8A71E9B04DBB")]
public partial interface ICorDebugSymbolProvider {
    [PreserveSig]
    int TryGetStaticFieldSymbols(uint cbSignature, [In][MarshalUsing(CountElementName = "cbSignature")] byte[] typeSig, uint cRequestedSymbols, out uint pcFetchedSymbols, [Out][MarshalUsing(CountElementName = "cRequestedSymbols")] ICorDebugStaticFieldSymbol[] pSymbols);

    [PreserveSig]
    int TryGetInstanceFieldSymbols(uint cbSignature, [In][MarshalUsing(CountElementName = "cbSignature")] byte[] typeSig, uint cRequestedSymbols, out uint pcFetchedSymbols, [Out][MarshalUsing(CountElementName = "cRequestedSymbols")] ICorDebugInstanceFieldSymbol[] pSymbols);

    [PreserveSig]
    int TryGetMethodLocalSymbols(uint nativeRVA, uint cRequestedSymbols, out uint pcFetchedSymbols, [Out][MarshalUsing(CountElementName = "cRequestedSymbols")] ICorDebugVariableSymbol[] pSymbols);

    [PreserveSig]
    int TryGetMethodParameterSymbols(uint nativeRVA, uint cRequestedSymbols, out uint pcFetchedSymbols, [Out][MarshalUsing(CountElementName = "cRequestedSymbols")] ICorDebugVariableSymbol[] pSymbols);

    [PreserveSig]
    int TryGetMergedAssemblyRecords(uint cRequestedRecords, out uint pcFetchedRecords, [Out][MarshalUsing(CountElementName = "cRequestedRecords")] ICorDebugMergedAssemblyRecord[] pRecords);

    [PreserveSig]
    int TryGetMethodProps(uint codeRva, out MetadataToken pMethodToken, out uint pcGenericParams, uint cbSignature, out uint pcbSignature, [Out][MarshalUsing(CountElementName = "cbSignature")] byte[] signature);

    [PreserveSig]
    int TryGetTypeProps(uint vtableRva, uint cbSignature, out uint pcbSignature, [Out][MarshalUsing(CountElementName = "cbSignature")] byte[] signature);

    [PreserveSig]
    int TryGetCodeRange(uint codeRva, out uint pCodeStartAddress, out uint pCodeSize);

    [PreserveSig]
    int TryGetAssemblyImageBytes(CordbAddress rva, uint length, out ICorDebugMemoryBuffer ppMemoryBuffer);

    [PreserveSig]
    int TryGetObjectSize(uint cbSignature, [In][MarshalUsing(CountElementName = "cbSignature")] byte[] typeSig, out uint pObjectSize);

    [PreserveSig]
    int TryGetAssemblyImageMetadata(out ICorDebugMemoryBuffer ppMemoryBuffer);
}