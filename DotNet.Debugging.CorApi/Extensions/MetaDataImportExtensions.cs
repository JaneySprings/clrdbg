using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class MetaDataImportExtensions {
    private delegate int EnumFunc<T>(ref HCorEnum handle, out T item, out uint count);

    public static IEnumerable<MethodDefToken> EnumMethodsWithName(this IMetaDataImport instance, TypeDefToken cl, string szName) {
        return Enumerate(instance, (ref HCorEnum handle, out MethodDefToken item, out uint count) => instance.TryEnumMethodsWithName(ref handle, cl, szName, out item, 1u, out count));
    }

    public static IEnumerable<FieldDefToken> EnumFields(this IMetaDataImport instance, TypeDefToken cl) {
        return Enumerate(instance, (ref HCorEnum handle, out FieldDefToken item, out uint count) => instance.TryEnumFields(ref handle, cl, out item, 1u, out count));
    }

    public static IEnumerable<FieldDefToken> EnumFieldsWithName(this IMetaDataImport instance, TypeDefToken cl, string szName) {
        return Enumerate(instance, (ref HCorEnum handle, out FieldDefToken item, out uint count) => instance.TryEnumFieldsWithName(ref handle, cl, szName, out item, 1u, out count));
    }

    public static IEnumerable<PropertyToken> EnumProperties(this IMetaDataImport instance, TypeDefToken td) {
        return Enumerate(instance, (ref HCorEnum handle, out PropertyToken item, out uint count) => instance.TryEnumProperties(ref handle, td, out item, 1u, out count));
    }

    public static IEnumerable<InterfaceImplToken> EnumInterfaceImpls(this IMetaDataImport instance, TypeDefToken td) {
        return Enumerate(instance, (ref HCorEnum handle, out InterfaceImplToken item, out uint count) => instance.TryEnumInterfaceImpls(ref handle, td, out item, 1u, out count));
    }

    public static (TypeDefToken pClass, MetadataToken ptkIface) GetInterfaceImplProps(this IMetaDataImport instance, InterfaceImplToken iiImpl) {
        Marshal.ThrowExceptionForHR(instance.TryGetInterfaceImplProps(iiImpl, out var pClass, out var ptkIface));
        return (pClass: pClass, ptkIface: ptkIface);
    }

    public static (string szName, MetadataToken ptkResolutionScope) GetTypeRefProps(this IMetaDataImport instance, TypeRefToken tr) {
        Marshal.ThrowExceptionForHR(instance.TryGetTypeRefProps(tr, out var ptkResolutionScope, null, 0u, out var pchName));
        var szName = NativeStrings.Read(pchName, (char[] buffer, out uint length) => instance.TryGetTypeRefProps(tr, out _, buffer, (uint)buffer.Length, out length));
        return (szName: szName, ptkResolutionScope: ptkResolutionScope);
    }

    public static unsafe (nint ppvSig, int pcbSig) GetTypeSpecFromToken(this IMetaDataImport instance, TypeSpecToken typespec) {
        Marshal.ThrowExceptionForHR(instance.TryGetTypeSpecFromToken(typespec, out var ppvSig, out var pcbSig));
        return (ppvSig: unchecked((nint)ppvSig), pcbSig: checked((int)pcbSig));
    }

    public static (string szTypeDef, CorTypeAttr pdwTypeDefFlags, MetadataToken ptkExtends) GetTypeDefProps(this IMetaDataImport instance, TypeDefToken td) {
        Marshal.ThrowExceptionForHR(instance.TryGetTypeDefProps(td, null, 0u, out var pchTypeDef, out var pdwTypeDefFlags, out var ptkExtends));
        var szTypeDef = NativeStrings.Read(pchTypeDef, (char[] buffer, out uint length) => instance.TryGetTypeDefProps(td, buffer, (uint)buffer.Length, out length, out _, out _));
        return (szTypeDef: szTypeDef, pdwTypeDefFlags: pdwTypeDefFlags, ptkExtends: ptkExtends);
    }

    public static unsafe (string szField, TypeDefToken pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetFieldProps(this IMetaDataImport instance, FieldDefToken mb) {
        Marshal.ThrowExceptionForHR(instance.TryGetFieldProps(mb, out var pClass, null, 0u, out var pchField, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
        var szField = NativeStrings.Read(pchField, (char[] buffer, out uint length) => instance.TryGetFieldProps(mb, out _, buffer, (uint)buffer.Length, out length, out _, out _, out _, out _, out _, out _));
        return (szField: szField, pClass: pClass, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: checked((int)pcbSigBlob), pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: checked((int)pcchValue));
    }

    public static unsafe (string szMethod, TypeDefToken pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) GetMethodProps(this IMetaDataImport instance, MethodDefToken mb) {
        Marshal.ThrowExceptionForHR(instance.TryGetMethodProps(mb, out var pClass, null, 0u, out var pchMethod, out var pdwAttr, out var ppvSigBlob, out var pcbSigBlob, out var pulCodeRVA, out var pdwImplFlags));
        var szMethod = NativeStrings.Read(pchMethod, (char[] buffer, out uint length) => instance.TryGetMethodProps(mb, out _, buffer, (uint)buffer.Length, out length, out _, out _, out _, out _, out _));
        return (szMethod: szMethod, pClass: pClass, pdwAttr: pdwAttr, ppvSigBlob: unchecked((nint)ppvSigBlob), pcbSigBlob: checked((int)pcbSigBlob), pulCodeRVA: checked((int)pulCodeRVA), pdwImplFlags: checked((int)pdwImplFlags));
    }

    public static (string szName, MethodDefToken pmd, int pulSequence, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) GetParamProps(this IMetaDataImport instance, ParamDefToken tk) {
        Marshal.ThrowExceptionForHR(instance.TryGetParamProps(tk, out var pmd, out var pulSequence, null, 0u, out var pchName, out var pdwAttr, out var pdwCPlusTypeFlag, out var ppValue, out var pcchValue));
        var szName = NativeStrings.Read(pchName, (char[] buffer, out uint length) => instance.TryGetParamProps(tk, out _, out _, buffer, (uint)buffer.Length, out length, out _, out _, out _, out _));
        return (szName: szName, pmd: pmd, pulSequence: checked((int)pulSequence), pdwAttr: pdwAttr, pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppValue: ppValue, pcchValue: checked((int)pcchValue));
    }

    // The 'other' accessors of a property are not asked for: C# emits none, and a single slot could only hold the first anyway
    public static unsafe (string szProperty, TypeDefToken pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, MethodDefToken pmdSetter, MethodDefToken pmdGetter) GetPropertyProps(this IMetaDataImport instance, PropertyToken prop) {
        Marshal.ThrowExceptionForHR(instance.TryGetPropertyProps(prop, out var pClass, null, 0u, out var pchProperty, out var pdwPropFlags, out var ppvSig, out var pbSig, out var pdwCPlusTypeFlag, out var ppDefaultValue, out var pcchDefaultValue, out var pmdSetter, out var pmdGetter, out _, 1u, out _));
        var szProperty = NativeStrings.Read(pchProperty, (char[] buffer, out uint length) => instance.TryGetPropertyProps(prop, out _, buffer, (uint)buffer.Length, out length, out _, out _, out _, out _, out _, out _, out _, out _, out _, 1u, out _));
        return (szProperty: szProperty, pClass: pClass, pdwPropFlags: pdwPropFlags, ppvSig: unchecked((nint)ppvSig), pbSig: checked((int)pbSig), pdwCPlusTypeFlag: pdwCPlusTypeFlag, ppDefaultValue: ppDefaultValue, pcchDefaultValue: checked((int)pcchDefaultValue), pmdSetter: pmdSetter, pmdGetter: pmdGetter);
    }

    public static unsafe FieldDefToken FindField(this IMetaDataImport instance, TypeDefToken td, string szName, nint pvSigBlob, int cbSigBlob) {
        Marshal.ThrowExceptionForHR(instance.TryFindField(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
        return pmb;
    }

    public static unsafe MethodDefToken FindMethod(this IMetaDataImport instance, TypeDefToken td, string szName, nint pvSigBlob, int cbSigBlob) {
        Marshal.ThrowExceptionForHR(instance.TryFindMethod(td, szName, (byte*)pvSigBlob, checked((uint)cbSigBlob), out var pmb));
        return pmb;
    }

    public static TypeDefToken GetNestedClassProps(this IMetaDataImport instance, TypeDefToken tdNestedClass) {
        Marshal.ThrowExceptionForHR(instance.TryGetNestedClassProps(tdNestedClass, out var ptdEnclosingClass));
        return ptdEnclosingClass;
    }

    public static ParamDefToken GetParamForMethodIndex(this IMetaDataImport instance, MethodDefToken md, int ulParamSeq) {
        Marshal.ThrowExceptionForHR(instance.TryGetParamForMethodIndex(md, checked((uint)ulParamSeq), out var ppd));
        return ppd;
    }

    // Walks a metadata enumeration one token at a time (the interface binds the output array to a single token) and closes it
    private static IEnumerable<T> Enumerate<T>(IMetaDataImport instance, EnumFunc<T> next) {
        var handle = default(HCorEnum);
        try {
            while (true) {
                Marshal.ThrowExceptionForHR(next(ref handle, out var item, out var count));
                if (count == 0)
                    yield break;
                yield return item;
            }
        }
        finally {
            if (!handle.IsNull)
                instance.TryCloseEnum(handle);
        }
    }
}
