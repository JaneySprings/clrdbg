using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataimport-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44")]
public partial interface IMetaDataImport {
    [PreserveSig]
    void TryCloseEnum(HCorEnum hEnum);

    [PreserveSig]
    int TryCountEnum(HCorEnum hEnum, out uint pulCount);

    [PreserveSig]
    int TryResetEnum(HCorEnum hEnum, uint ulPos);

    [PreserveSig]
    int TryEnumTypeDefs(ref HCorEnum phEnum, out TypeDefToken rTypeDefs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTypeDefs);

    [PreserveSig]
    int TryEnumInterfaceImpls(ref HCorEnum phEnum, TypeDefToken td, out InterfaceImplToken rImpls, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcImpls);

    [PreserveSig]
    int TryEnumTypeRefs(ref HCorEnum phEnum, out TypeRefToken rTypeRefs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTypeRefs);

    [PreserveSig]
    int TryFindTypeDefByName([MarshalAs(UnmanagedType.LPWStr)] string szTypeDef, MetadataToken tkEnclosingClass, out TypeDefToken ptd);

    [PreserveSig]
    int TryGetScopeProps([Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[]? szName, uint cchName, out uint pchName, out Guid pmvid);

    [PreserveSig]
    int TryGetModuleFromScope(out ModuleToken pmd);

    [PreserveSig]
    int TryGetTypeDefProps(TypeDefToken td, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[]? szTypeDef, uint cchTypeDef, out uint pchTypeDef, out CorTypeAttr pdwTypeDefFlags, out MetadataToken ptkExtends);

    [PreserveSig]
    int TryGetInterfaceImplProps(InterfaceImplToken iiImpl, out TypeDefToken pClass, out MetadataToken ptkIface);

    [PreserveSig]
    int TryGetTypeRefProps(TypeRefToken tr, out MetadataToken ptkResolutionScope, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szName, uint cchName, out uint pchName);

    [PreserveSig]
    int TryResolveTypeRef(TypeRefToken tr, ref Guid riid, out nint ppIScope, out TypeDefToken ptd);

    [PreserveSig]
    int TryEnumMembers(ref HCorEnum phEnum, TypeDefToken cl, out MetadataToken rMembers, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumMembersWithName(ref HCorEnum phEnum, TypeDefToken cl, [MarshalAs(UnmanagedType.LPWStr)] string szName, out MetadataToken rMembers, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumMethods(ref HCorEnum phEnum, TypeDefToken cl, out MethodDefToken rMethods, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumMethodsWithName(ref HCorEnum phEnum, TypeDefToken cl, [MarshalAs(UnmanagedType.LPWStr)] string szName, out MethodDefToken rMethods, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumFields(ref HCorEnum phEnum, TypeDefToken cl, out FieldDefToken rFields, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumFieldsWithName(ref HCorEnum phEnum, TypeDefToken cl, [MarshalAs(UnmanagedType.LPWStr)] string szName, out FieldDefToken rFields, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumParams(ref HCorEnum phEnum, MethodDefToken mb, out ParamDefToken rParams, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumMemberRefs(ref HCorEnum phEnum, MetadataToken tkParent, out MemberRefToken rMemberRefs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumMethodImpls(ref HCorEnum phEnum, TypeDefToken td, out MetadataToken rMethodBody, out MetadataToken rMethodDecl, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryEnumPermissionSets(ref HCorEnum phEnum, MetadataToken tk, uint dwActions, out PermissionToken rPermission, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    unsafe int TryFindMember(TypeDefToken td, [MarshalAs(UnmanagedType.LPWStr)] string szName, byte* pvSigBlob, uint cbSigBlob, out MetadataToken pmb);

    [PreserveSig]
    unsafe int TryFindMethod(TypeDefToken td, [MarshalAs(UnmanagedType.LPWStr)] string szName, byte* pvSigBlob, uint cbSigBlob, out MethodDefToken pmb);

    [PreserveSig]
    unsafe int TryFindField(TypeDefToken td, [MarshalAs(UnmanagedType.LPWStr)] string szName, byte* pvSigBlob, uint cbSigBlob, out FieldDefToken pmb);

    [PreserveSig]
    unsafe int TryFindMemberRef(TypeRefToken td, [MarshalAs(UnmanagedType.LPWStr)] string szName, byte* pvSigBlob, uint cbSigBlob, out MemberRefToken pmr);

    [PreserveSig]
    unsafe int TryGetMethodProps(MethodDefToken mb, out TypeDefToken pClass, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szMethod, uint cchMethod, out uint pchMethod, out CorMethodAttr pdwAttr, out byte* ppvSigBlob, out uint pcbSigBlob, out uint pulCodeRVA, out uint pdwImplFlags);

    [PreserveSig]
    unsafe int TryGetMemberRefProps(MemberRefToken mr, out MetadataToken ptk, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szMember, uint cchMember, out uint pchMember, out byte* ppvSigBlob, out uint pbSig);

    [PreserveSig]
    int TryEnumProperties(ref HCorEnum phEnum, TypeDefToken td, out PropertyToken rProperties, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcProperties);

    [PreserveSig]
    int TryEnumEvents(ref HCorEnum phEnum, TypeDefToken td, out EventToken rEvents, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcEvents);

    [PreserveSig]
    int TryGetEventProps(EventToken ev, out TypeDefToken pClass, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szEvent, uint cchEvent, out uint pchEvent, out CorEventAttr pdwEventFlags, out MetadataToken ptkEventType, out MethodDefToken pmdAddOn, out MethodDefToken pmdRemoveOn, out MethodDefToken pmdFire, out MethodDefToken rmdOtherMethod, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcOtherMethod);

    [PreserveSig]
    int TryEnumMethodSemantics(ref HCorEnum phEnum, MethodDefToken mb, out MetadataToken rEventProp, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcEventProp);

    [PreserveSig]
    int TryGetMethodSemantics(MethodDefToken mb, MetadataToken tkEventProp, out uint pdwSemanticsFlags);

    [PreserveSig]
    int TryGetClassLayout(TypeDefToken td, out uint pdwPackSize, out CorFieldOffset rFieldOffset, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcFieldOffset, out uint pulClassSize);

    [PreserveSig]
    unsafe int TryGetFieldMarshal(MetadataToken tk, out byte* ppvNativeType, out uint pcbNativeType);

    [PreserveSig]
    int TryGetRVA(MetadataToken tk, out uint pulCodeRVA, out uint pdwImplFlags);

    [PreserveSig]
    int TryGetPermissionSetProps(PermissionToken pm, out uint pdwAction, out nint ppvPermission, out uint pcbPermission);

    [PreserveSig]
    unsafe int TryGetSigFromToken(SignatureToken mdSig, out byte* ppvSig, out uint pcbSig);

    [PreserveSig]
    int TryGetModuleRefProps(ModuleRefToken mur, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[]? szName, uint cchName, out uint pchName);

    [PreserveSig]
    int TryEnumModuleRefs(ref HCorEnum phEnum, out ModuleRefToken rModuleRefs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cmax, out uint pcModuleRefs);

    [PreserveSig]
    unsafe int TryGetTypeSpecFromToken(TypeSpecToken typespec, out byte* ppvSig, out uint pcbSig);

    [PreserveSig]
    unsafe int TryGetNameFromToken(MetadataToken tk, out sbyte* pszUtf8NamePtr);

    [PreserveSig]
    int TryEnumUnresolvedMethods(ref HCorEnum phEnum, out MetadataToken rMethods, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcTokens);

    [PreserveSig]
    int TryGetUserString(StringToken stk, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[]? szString, uint cchString, out uint pchString);

    [PreserveSig]
    int TryGetPinvokeMap(MetadataToken tk, out uint pdwMappingFlags, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szImportName, uint cchImportName, out uint pchImportName, out ModuleRefToken pmrImportDLL);

    [PreserveSig]
    int TryEnumSignatures(ref HCorEnum phEnum, out SignatureToken rSignatures, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cmax, out uint pcSignatures);

    [PreserveSig]
    int TryEnumTypeSpecs(ref HCorEnum phEnum, out TypeSpecToken rTypeSpecs, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cmax, out uint pcTypeSpecs);

    [PreserveSig]
    int TryEnumUserStrings(ref HCorEnum phEnum, out StringToken rStrings, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cmax, out uint pcStrings);

    [PreserveSig]
    int TryGetParamForMethodIndex(MethodDefToken md, uint ulParamSeq, out ParamDefToken ppd);

    [PreserveSig]
    int TryEnumCustomAttributes(ref HCorEnum phEnum, MetadataToken tk, MetadataToken tkType, out CustomAttributeToken rCustomAttributes, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcCustomAttributes);

    [PreserveSig]
    int TryGetCustomAttributeProps(CustomAttributeToken cv, out MetadataToken ptkObj, out MetadataToken ptkType, out nint ppBlob, out uint pcbSize);

    [PreserveSig]
    int TryFindTypeRef(MetadataToken tkResolutionScope, [MarshalAs(UnmanagedType.LPWStr)] string szName, out TypeRefToken ptr);

    [PreserveSig]
    unsafe int TryGetMemberProps(MetadataToken mb, out TypeDefToken pClass, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szMember, uint cchMember, out uint pchMember, out uint pdwAttr, out byte* ppvSigBlob, out uint pcbSigBlob, out uint pulCodeRVA, out uint pdwImplFlags, out CorElementType pdwCPlusTypeFlag, out nint ppValue, out uint pcchValue);

    [PreserveSig]
    unsafe int TryGetFieldProps(FieldDefToken mb, out TypeDefToken pClass, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szField, uint cchField, out uint pchField, out CorFieldAttr pdwAttr, out byte* ppvSigBlob, out uint pcbSigBlob, out CorElementType pdwCPlusTypeFlag, out nint ppValue, out uint pcchValue);

    [PreserveSig]
    unsafe int TryGetPropertyProps(PropertyToken prop, out TypeDefToken pClass, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? szProperty, uint cchProperty, out uint pchProperty, out CorPropertyAttr pdwPropFlags, out byte* ppvSig, out uint pbSig, out CorElementType pdwCPlusTypeFlag, out nint ppDefaultValue, out uint pcchDefaultValue, out MethodDefToken pmdSetter, out MethodDefToken pmdGetter, out MethodDefToken rmdOtherMethod, [MarshalUsing(typeof(EnumeratorMax1Marshaller))] uint cMax, out uint pcOtherMethod);

    [PreserveSig]
    int TryGetParamProps(ParamDefToken tk, out MethodDefToken pmd, out uint pulSequence, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[]? szName, uint cchName, out uint pchName, out CorParamAttr pdwAttr, out CorElementType pdwCPlusTypeFlag, out nint ppValue, out uint pcchValue);

    [PreserveSig]
    int TryGetCustomAttributeByName(MetadataToken tkObj, [MarshalAs(UnmanagedType.LPWStr)] string szName, out nint ppData, out uint pcbData);

    [PreserveSig]
    int TryIsValidToken(MetadataToken tk);

    [PreserveSig]
    int TryGetNestedClassProps(TypeDefToken tdNestedClass, out TypeDefToken ptdEnclosingClass);

    [PreserveSig]
    int TryGetNativeCallConvFromSig(nint pvSig, uint cbSig, out uint pCallConv);

    [PreserveSig]
    int TryIsGlobal(MetadataToken pd, out int pbGlobal);
}