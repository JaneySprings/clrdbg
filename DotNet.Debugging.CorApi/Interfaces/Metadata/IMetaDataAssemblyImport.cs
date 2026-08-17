using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataassemblyimport-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("EE62470B-E94B-424E-9B7C-2F00C9249F93")]
public partial interface IMetaDataAssemblyImport {
    [PreserveSig]
    int TryGetAssemblyProps(AssemblyToken mda, out nint ppbPublicKey, out uint pcbPublicKey, out uint pulHashAlgId, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] char[]? szName, uint cchName, out uint pchName, out AssemblyMetadata pMetaData, out CorAssemblyFlags pdwAssemblyFlags);

    [PreserveSig]
    int TryGetAssemblyRefProps(AssemblyRefToken mdar, out nint ppbPublicKeyOrToken, out uint pcbPublicKeyOrToken, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[]? szName, uint cchName, out uint pchName, out AssemblyMetadata pMetaData, out nint ppbHashValue, out uint pcbHashValue, out CorAssemblyFlags pdwAssemblyRefFlags);

    [PreserveSig]
    int TryGetFileProps(FileToken mdf, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[]? szName, uint cchName, out uint pchName, out nint ppbHashValue, out uint pcbHashValue, out CorFileFlags pdwFileFlags);

    [PreserveSig]
    int TryGetExportedTypeProps(ExportedTypeToken mdct, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[]? szName, uint cchName, out uint pchName, out MetadataToken ptkImplementation, out TypeDefToken ptkTypeDef, out CorTypeAttr pdwExportedTypeFlags);

    [PreserveSig]
    int TryGetManifestResourceProps(ManifestResourceToken mdmr, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[]? szName, uint cchName, out uint pchName, out MetadataToken ptkImplementation, out uint pdwOffset, out CorManifestResourceFlags pdwResourceFlags);

    [PreserveSig]
    int TryEnumAssemblyRefs(ref HCorEnum phEnum, out AssemblyRefToken rAssemblyRefs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumFiles(ref HCorEnum phEnum, out FileToken rFiles, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumExportedTypes(ref HCorEnum phEnum, out ExportedTypeToken rExportedTypes, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumManifestResources(ref HCorEnum phEnum, out ManifestResourceToken rManifestResources, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryGetAssemblyFromScope(out AssemblyToken ptkAssembly);

    [PreserveSig]
    int TryFindExportedTypeByName([MarshalAs(UnmanagedType.LPWStr)] string szName, MetadataToken mdtExportedType, out ExportedTypeToken ptkExportedType);

    [PreserveSig]
    int TryFindManifestResourceByName([MarshalAs(UnmanagedType.LPWStr)] string szName, out ManifestResourceToken ptkManifestResource);

    [PreserveSig]
    void TryCloseEnum(HCorEnum hEnum);

    [PreserveSig]
    int TryFindAssembliesByName([MarshalAs(UnmanagedType.LPWStr)] string szAppBase, [MarshalAs(UnmanagedType.LPWStr)] string szPrivateBin, [MarshalAs(UnmanagedType.LPWStr)] string szAssemblyName, out nint ppIUnk, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcAssemblies);
}