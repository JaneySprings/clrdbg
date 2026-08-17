using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class MetaDataImportExtensions {
    public static IEnumerable<TypeDefToken> EnumTypeDefs(this IMetaDataImport instance) {
        return EnumerateTypeDefsCore(instance);
    }

    public static IEnumerable<MethodDefToken> EnumMethods(this IMetaDataImport instance, TypeDefToken cl) {
        return EnumerateMethodsCore(instance, cl);
    }

    public static IEnumerable<MethodDefToken> EnumMethodsWithName(this IMetaDataImport instance, TypeDefToken cl, string szName) {
        return EnumerateMethodsWithNameCore(instance, cl, szName);
    }

    public static IEnumerable<FieldDefToken> EnumFields(this IMetaDataImport instance, TypeDefToken cl) {
        return EnumerateFieldsCore(instance, cl);
    }

    public static IEnumerable<FieldDefToken> EnumFieldsWithName(this IMetaDataImport instance, TypeDefToken cl, string szName) {
        return EnumerateFieldsWithNameCore(instance, cl, szName);
    }

    public static IEnumerable<PropertyToken> EnumProperties(this IMetaDataImport instance, TypeDefToken td) {
        return EnumeratePropertiesCore(instance, td);
    }

    public static (string szName, Guid pmvid) GetScopeProps(this IMetaDataImport instance) => QueryGetScopeProps(instance);

    public static (string szTypeDef, CorTypeAttr pdwTypeDefFlags, MetadataToken ptkExtends) GetTypeDefProps(this IMetaDataImport instance, TypeDefToken typeDefinition) {
        return QueryGetTypeDefProps(instance, typeDefinition);
    }

    public static unsafe (string szField, TypeDefToken pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetFieldProps(this IMetaDataImport instance, FieldDefToken mb) {
        Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass, null, 0u, out var pchField, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
        checked {
            if (pchField == 0) {
                return (szField: string.Empty, pClass: pClass, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
            }
            for (var i = 0; i < 3; i = unchecked(i + 1)) {
                var num = pchField;
                var array = new char[(int)num];
                Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass2, array, num, out var pchField2, out var pdwAttr2, out var ppvSigBlob2, out var pcbSigBlob2, out var pdwCPlusTypeFlag2, out var ppValue2, out var pcchValue2));
                if (pchField2 > num) {
                    pchField = pchField2;
                    continue;
                }
                return (szField: CreateString(array, pchField2, trimTerminator: true), pClass: pClass2, pdwAttr: pdwAttr2, ppvSigBlob: unchecked((nint)ppvSigBlob2), pcbSigBlob: (int)pcbSigBlob2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppValue: ppValue2, pcchValue: (int)pcchValue2);
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }
    }

    public static unsafe (string szMethod, TypeDefToken pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) GetMethodProps(this IMetaDataImport instance, MethodDefToken mb) {
        Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass, null, 0u, out var pchMethod, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags));
        checked {
            if (pchMethod == 0) {
                return (szMethod: string.Empty, pClass: pClass, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags);
            }
            for (var i = 0; i < 3; i = unchecked(i + 1)) {
                var num = pchMethod;
                var array = new char[(int)num];
                Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass2, array, num, out var pchMethod2, out var pdwAttr2, out var ppvSigBlob2, out var pcbSigBlob2, out var pulCodeRVA2, out var pdwImplFlags2));
                if (pchMethod2 > num) {
                    pchMethod = pchMethod2;
                    continue;
                }
                return (szMethod: CreateString(array, pchMethod2, trimTerminator: true), pClass: pClass2, pdwAttr: pdwAttr2, ppvSigBlob: unchecked((nint)ppvSigBlob2), pcbSigBlob: (int)pcbSigBlob2, pulCodeRVA: (int)pulCodeRVA2, pdwImplFlags: (int)pdwImplFlags2);
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }
    }

    public static (string szName, MethodDefToken pmd, int pulSequence, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetParamProps(this IMetaDataImport instance, ParamDefToken tk) {
        Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd, out var pulSequence, null, 0u, out var pchName, out var pdwAttr, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
        checked {
            if (pchName == 0) {
                return (szName: string.Empty, pmd: pmd, pulSequence: (int)pulSequence, pdwAttr: pdwAttr, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
            }
            for (var i = 0; i < 3; i = unchecked(i + 1)) {
                var num = pchName;
                var array = new char[(int)num];
                Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd2, out var pulSequence2, array, num, out var pchName2, out var pdwAttr2, out var pdwCPlusTypeFlag2, out var ppValue2, out var pcchValue2));
                if (pchName2 > num) {
                    pchName = pchName2;
                    continue;
                }
                return (szName: CreateString(array, pchName2, trimTerminator: true), pmd: pmd2, pulSequence: (int)pulSequence2, pdwAttr: pdwAttr2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppValue: ppValue2, pcchValue: (int)pcchValue2);
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }
    }

    public static unsafe (string szProperty, TypeDefToken pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, MethodDefToken pmdSetter, MethodDefToken pmdGetter, MethodDefToken rmdOtherMethod, int pcOtherMethod) GetPropertyProps(this IMetaDataImport instance, PropertyToken prop) {
        Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass, null, 0u, out var pchProperty, out var pdwPropFlags, out var ppvSig, out var pbSig, out var pdwCPlusTypeFlag, out var ppDefaultValue, out var pcchDefaultValue, out var pmdSetter, out var pmdGetter, out var rmdOtherMethod, 1u, out var pcOtherMethod));
        checked {
            if (pchProperty == 0) {
                return (szProperty: string.Empty, pClass: pClass, pdwPropFlags: pdwPropFlags, ppvSig: unchecked((nint)ppvSig), pbSig: (int)pbSig, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppDefaultValue: ppDefaultValue, pcchDefaultValue: (int)pcchDefaultValue, pmdSetter: pmdSetter, pmdGetter: pmdGetter, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
            }
            for (var i = 0; i < 3; i = unchecked(i + 1)) {
                var num = pchProperty;
                var array = new char[(int)num];
                Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass2, array, num, out var pchProperty2, out var pdwPropFlags2, out var ppvSig2, out var pbSig2, out var pdwCPlusTypeFlag2, out var ppDefaultValue2, out var pcchDefaultValue2, out var pmdSetter2, out var pmdGetter2, out var rmdOtherMethod2, 1u, out var pcOtherMethod2));
                if (pchProperty2 > num) {
                    pchProperty = pchProperty2;
                    continue;
                }
                return (szProperty: CreateString(array, pchProperty2, trimTerminator: true), pClass: pClass2, pdwPropFlags: pdwPropFlags2, ppvSig: unchecked((nint)ppvSig2), pbSig: (int)pbSig2, pdwCPlusTypeFlag: pdwCPlusTypeFlag2, ppDefaultValue: ppDefaultValue2, pcchDefaultValue: (int)pcchDefaultValue2, pmdSetter: pmdSetter2, pmdGetter: pmdGetter2, rmdOtherMethod: rmdOtherMethod2, pcOtherMethod: (int)pcOtherMethod2);
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }
    }

    public static (FieldDefToken rFields, int pcTokens) EnumFields(this IMetaDataImport instance, ref HCorEnum phEnum, TypeDefToken cl) {
        Marshal.ThrowExceptionForHR(instance.TryEnumFields(ref phEnum, cl, out var rFields, 1u, out var pcTokens));
        return (rFields: rFields, pcTokens: checked((int)pcTokens));
    }

    public static (FieldDefToken rFields, int pcTokens) EnumFieldsWithName(this IMetaDataImport instance, ref HCorEnum phEnum, TypeDefToken cl, string szName) {
        Marshal.ThrowExceptionForHR(instance.TryEnumFieldsWithName(ref phEnum, cl, szName, out var rFields, 1u, out var pcTokens));
        return (rFields: rFields, pcTokens: checked((int)pcTokens));
    }

    public static (MethodDefToken rMethods, int pcTokens) EnumMethods(this IMetaDataImport instance, ref HCorEnum phEnum, TypeDefToken cl) {
        Marshal.ThrowExceptionForHR(instance.TryEnumMethods(ref phEnum, cl, out var rMethods, 1u, out var pcTokens));
        return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
    }

    public static (MethodDefToken rMethods, int pcTokens) EnumMethodsWithName(this IMetaDataImport instance, ref HCorEnum phEnum, TypeDefToken cl, string szName) {
        Marshal.ThrowExceptionForHR(instance.TryEnumMethodsWithName(ref phEnum, cl, szName, out var rMethods, 1u, out var pcTokens));
        return (rMethods: rMethods, pcTokens: checked((int)pcTokens));
    }

    public static (PropertyToken rProperties, int pcProperties) EnumProperties(this IMetaDataImport instance, ref HCorEnum phEnum, TypeDefToken td) {
        Marshal.ThrowExceptionForHR(instance.TryEnumProperties(ref phEnum, td, out var rProperties, 1u, out var pcProperties));
        return (rProperties: rProperties, pcProperties: checked((int)pcProperties));
    }

    public static (TypeDefToken rTypeDefs, int pcTypeDefs) EnumTypeDefs(this IMetaDataImport instance, ref HCorEnum phEnum) {
        Marshal.ThrowExceptionForHR(instance.TryEnumTypeDefs(ref phEnum, out var rTypeDefs, 1u, out var pcTypeDefs));
        return (rTypeDefs: rTypeDefs, pcTypeDefs: checked((int)pcTypeDefs));
    }

    public static unsafe FieldDefToken FindField(this IMetaDataImport instance, TypeDefToken td, string szName, nint pvSigBlob, int cbSigBlob) {
        Marshal.ThrowExceptionForHR(instance.TryFindField(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
        return pmb;
    }

    public static unsafe MethodDefToken FindMethod(this IMetaDataImport instance, TypeDefToken td, string szName, nint pvSigBlob, int cbSigBlob) {
        Marshal.ThrowExceptionForHR(instance.TryFindMethod(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
        return pmb;
    }

    public static unsafe (TypeDefToken pClass, int pchField, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetFieldProps(this IMetaDataImport instance, FieldDefToken mb, char[]? szField, int cchField) {
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass, szField, (uint)cchField, out var pchField, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
            return (pClass: pClass, pchField: (int)pchField, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
        }
    }

    public static unsafe (TypeDefToken pClass, int pchMethod, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) GetMethodProps(this IMetaDataImport instance, MethodDefToken mb, char[]? szMethod, int cchMethod) {
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass, szMethod, (uint)cchMethod, out var pchMethod, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags));
            return (pClass: pClass, pchMethod: (int)pchMethod, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: (int)pcbSigBlob, pulCodeRVA: (int)pulCodeRVA, pdwImplFlags: (int)pdwImplFlags);
        }
    }

    public static TypeDefToken GetNestedClassProps(this IMetaDataImport instance, TypeDefToken tdNestedClass) {
        Marshal.ThrowExceptionForHR(instance.TryGetNestedClassProps(tdNestedClass, out var ptdEnclosingClass));
        return ptdEnclosingClass;
    }

    public static ParamDefToken GetParamForMethodIndex(this IMetaDataImport instance, MethodDefToken md, int ulParamSeq) {
        Marshal.ThrowExceptionForHR(instance.TryGetParamForMethodIndex(md, checked((uint)ulParamSeq), out var ppd));
        return ppd;
    }

    public static (MethodDefToken pmd, int pulSequence, int pchName, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetParamProps(this IMetaDataImport instance, ParamDefToken tk, char[]? szName, int cchName) {
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd, out var pulSequence, szName, (uint)cchName, out var pchName, out var pdwAttr, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
            return (pmd: pmd, pulSequence: (int)pulSequence, pchName: (int)pchName, pdwAttr: pdwAttr, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: (int)pcchValue);
        }
    }

    public static unsafe (TypeDefToken pClass, int pchProperty, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, MethodDefToken pmdSetter, MethodDefToken pmdGetter, MethodDefToken rmdOtherMethod, int pcOtherMethod) GetPropertyProps(this IMetaDataImport instance, PropertyToken prop, char[]? szProperty, int cchProperty) {
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass, szProperty, (uint)cchProperty, out var pchProperty, out var pdwPropFlags, out var ppvSig, out var pbSig, out var pdwCPlusTypeFlag, out var ppDefaultValue, out var pcchDefaultValue, out var pmdSetter, out var pmdGetter, out var rmdOtherMethod, 1u, out var pcOtherMethod));
            return (pClass: pClass, pchProperty: (int)pchProperty, pdwPropFlags: pdwPropFlags, ppvSig: unchecked((nint)ppvSig), pbSig: (int)pbSig, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppDefaultValue: ppDefaultValue, pcchDefaultValue: (int)pcchDefaultValue, pmdSetter: pmdSetter, pmdGetter: pmdGetter, rmdOtherMethod: rmdOtherMethod, pcOtherMethod: (int)pcOtherMethod);
        }
    }

    public static (int pchName, Guid pmvid) GetScopeProps(this IMetaDataImport instance, char[]? szName, int cchName) {
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetScopeProps(szName, (uint)cchName, out var pchName, out var pmvid));
            return (pchName: (int)pchName, pmvid: pmvid);
        }
    }

    public static (int pchTypeDef, CorTypeAttr pdwTypeDefFlags, MetadataToken ptkExtends) GetTypeDefProps(this IMetaDataImport instance, TypeDefToken td, char[]? szTypeDef, int cchTypeDef) {
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(td, szTypeDef, (uint)cchTypeDef, out var pchTypeDef, out var pdwTypeDefFlags, out var ptkExtends));
            return (pchTypeDef: (int)pchTypeDef, pdwTypeDefFlags: pdwTypeDefFlags, ptkExtends: ptkExtends);
        }
    }

    private static IEnumerable<TypeDefToken> EnumerateTypeDefsCore(IMetaDataImport instance) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(instance.TryEnumTypeDefs(ref handle, out var rTypeDefs, 1u, out var pcTypeDefs));
                switch (pcTypeDefs) {
                    default:
                        throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
                    case 1u:
                        yield return rTypeDefs;
                        break;
                    case 0u:
                        yield break;
                }
            }
        }
        finally {
            if (!handle.IsNull) {
                instance.TryCloseEnum(handle);
            }
        }
    }

    private static IEnumerable<MethodDefToken> EnumerateMethodsCore(IMetaDataImport instance, TypeDefToken cl) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(instance.TryEnumMethods(ref handle, cl, out var rMethods, 1u, out var pcTokens));
                switch (pcTokens) {
                    default:
                        throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
                    case 1u:
                        yield return rMethods;
                        break;
                    case 0u:
                        yield break;
                }
            }
        }
        finally {
            if (!handle.IsNull) {
                instance.TryCloseEnum(handle);
            }
        }
    }

    private static IEnumerable<MethodDefToken> EnumerateMethodsWithNameCore(IMetaDataImport instance, TypeDefToken cl, string szName) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(instance.TryEnumMethodsWithName(ref handle, cl, szName, out var rMethods, 1u, out var pcTokens));
                switch (pcTokens) {
                    default:
                        throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
                    case 1u:
                        yield return rMethods;
                        break;
                    case 0u:
                        yield break;
                }
            }
        }
        finally {
            if (!handle.IsNull) {
                instance.TryCloseEnum(handle);
            }
        }
    }

    private static IEnumerable<FieldDefToken> EnumerateFieldsCore(IMetaDataImport instance, TypeDefToken cl) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(instance.TryEnumFields(ref handle, cl, out var rFields, 1u, out var pcTokens));
                switch (pcTokens) {
                    default:
                        throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
                    case 1u:
                        yield return rFields;
                        break;
                    case 0u:
                        yield break;
                }
            }
        }
        finally {
            if (!handle.IsNull) {
                instance.TryCloseEnum(handle);
            }
        }
    }

    private static IEnumerable<FieldDefToken> EnumerateFieldsWithNameCore(IMetaDataImport instance, TypeDefToken cl, string szName) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(instance.TryEnumFieldsWithName(ref handle, cl, szName, out var rFields, 1u, out var pcTokens));
                switch (pcTokens) {
                    default:
                        throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
                    case 1u:
                        yield return rFields;
                        break;
                    case 0u:
                        yield break;
                }
            }
        }
        finally {
            if (!handle.IsNull) {
                instance.TryCloseEnum(handle);
            }
        }
    }

    private static IEnumerable<PropertyToken> EnumeratePropertiesCore(IMetaDataImport instance, TypeDefToken td) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(instance.TryEnumProperties(ref handle, td, out var rProperties, 1u, out var pcProperties));
                switch (pcProperties) {
                    default:
                        throw new InvalidOperationException("Native metadata enumerator returned an invalid item count.");
                    case 1u:
                        yield return rProperties;
                        break;
                    case 0u:
                        yield break;
                }
            }
        }
        finally {
            if (!handle.IsNull) {
                instance.TryCloseEnum(handle);
            }
        }
    }

    private static (string szName, Guid pmvid) QueryGetScopeProps(IMetaDataImport instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetScopeProps(null, 0u, out var pchName, out var _));
        if (pchName == 0) {
            return (szName: string.Empty, pmvid: default(Guid));
        }
        for (var i = 0; i < 3; i++) {
            var num = pchName;
            var array = new char[checked((int)num)];
            Marshal.ThrowExceptionForHR(instance.TryGetScopeProps(array, num, out var pchName2, out var pmvid2));
            if (pchName2 > num) {
                pchName = pchName2;
                continue;
            }
            return (szName: CreateString(array, pchName2, trimTerminator: true), pmvid: pmvid2);
        }
        throw new InvalidOperationException("Native buffer size did not stabilize.");
    }

    private static (string szTypeDef, CorTypeAttr pdwTypeDefFlags, MetadataToken ptkExtends) QueryGetTypeDefProps(IMetaDataImport instance, TypeDefToken typeDefinition) {
        Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(typeDefinition, null, 0u, out var pchTypeDef, out var _, out var _));
        if (pchTypeDef == 0) {
            return (szTypeDef: string.Empty, pdwTypeDefFlags: CorTypeAttr.tdNotPublic, ptkExtends: default(MetadataToken));
        }
        for (var i = 0; i < 3; i++) {
            var num = pchTypeDef;
            var array = new char[checked((int)num)];
            Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(typeDefinition, array, num, out var pchTypeDef2, out var pdwTypeDefFlags2, out var ptkExtends2));
            if (pchTypeDef2 > num) {
                pchTypeDef = pchTypeDef2;
                continue;
            }
            return (szTypeDef: CreateString(array, pchTypeDef2, trimTerminator: true), pdwTypeDefFlags: pdwTypeDefFlags2, ptkExtends: ptkExtends2);
        }
        throw new InvalidOperationException("Native buffer size did not stabilize.");
    }

    private static string CreateString(char[] buffer, uint count, bool trimTerminator) {
        var num = checked((int)count);
        if (trimTerminator && num > 0 && buffer[num - 1] == '\0') {
            num--;
        }
        return new string(buffer, 0, num);
    }
}