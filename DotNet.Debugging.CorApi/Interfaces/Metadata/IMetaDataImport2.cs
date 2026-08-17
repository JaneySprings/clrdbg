using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataimport2-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FCE5EFA0-8BBA-4F8E-A036-8F2022B08466")]
public unsafe partial interface IMetaDataImport2 : IMetaDataImport {
    [PreserveSig]
    int TryEnumGenericParams(ref HCorEnum phEnum, MetadataToken tk, out GenericParamToken rGenericParams, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcGenericParams);

    [PreserveSig]
    int TryGetGenericParamProps(GenericParamToken gp, out uint pulParamSeq, out CorGenericParamAttr pdwParamFlags, out MetadataToken ptOwner, out uint reserved, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 6)] char[]? wzname, uint cchName, out uint pchName);

    [PreserveSig]
    unsafe int TryGetMethodSpecProps(MethodSpecToken mi, out MetadataToken tkParent, out byte* ppvSigBlob, out uint pcbSigBlob);

    [PreserveSig]
    int TryEnumGenericParamConstraints(ref HCorEnum phEnum, GenericParamToken tk, out GenericParamConstraintToken rGenericParamConstraints, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcGenericParamConstraints);

    [PreserveSig]
    int TryGetGenericParamConstraintProps(GenericParamConstraintToken gpc, out GenericParamToken ptGenericParam, out MetadataToken ptkConstraintType);

    [PreserveSig]
    int TryGetPEKind(out uint pdwPEKind, out uint pdwMAchine);

    [PreserveSig]
    int TryGetVersionString([Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[]? pwzBuf, uint ccBufSize, out uint pccBufSize);

    [PreserveSig]
    int TryEnumMethodSpecs(ref HCorEnum phEnum, MetadataToken tk, out MethodSpecToken rMethodSpecs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcMethodSpecs);

}