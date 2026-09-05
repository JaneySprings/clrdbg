// Stand-ins for the Microsoft.VisualStudio.Debugger.Engine (Dkm) contract types the vendored Roslyn sources still name
// once the Dkm-facing files are excluded from the build (see the project file): the enums the compiler reports its
// results with and two data holders. Members and values are those of Microsoft.VisualStudio.Debugger.Engine 17.14
// (decoded from the assembly's metadata), so the compiler behaves as it does under Visual Studio. Nothing outside this
// assembly needs them, they are internal. The namespaces are Roslyn's, not this project's: the vendored files are not
// edited to point elsewhere
using System;
using System.Collections.ObjectModel;

namespace Microsoft.VisualStudio.Debugger.Clr {
    // Alias kinds for pseudo variables such as $exception
    internal enum DkmClrAliasKind {
        Exception = 0,
        StowedException = 1,
        ReturnValue = 2,
        Variable = 3,
        ObjectId = 4,
    }

    // Only 'ModuleId.GetModuleId(DkmClrModuleInstance)' names it, which nothing calls
    internal class DkmClrModuleInstance {
        public Guid Mvid { get; }
        public string FullName { get; }

        public DkmClrModuleInstance(Guid mvid, string fullName) {
            Mvid = mvid;
            FullName = fullName;
        }
    }
}

namespace Microsoft.VisualStudio.Debugger.Evaluation {
    [Flags]
    internal enum DkmEvaluationFlags {
        None = 0x0,
        TreatAsExpression = 0x1,
        TreatFunctionAsAddress = 0x2,
        NoSideEffects = 0x4,
        NoFuncEval = 0x8,
        DesignTime = 0x10,
        AllowImplicitVariables = 0x20,
        ForceEvaluationNow = 0x40,
        ShowValueRaw = 0x80,
        ForceRealFuncEval = 0x100,
        HideNonPublicMembers = 0x200,
        NoToString = 0x400,
        NoFormatting = 0x800,
        NoRawView = 0x1000,
        NoQuotes = 0x2000,
        DynamicView = 0x4000,
        ResultsOnly = 0x8000,
        NoExpansion = 0x10000,
        EnableExtendedSideEffects = 0x20000,
        FilterToFavorites = 0x40000,
        UseSimpleDisplayString = 0x80000,
        IncreaseMaxStringSize = 0x100000,
        CompactName = 0x200000,
    }

    internal enum DkmEvaluationResultCategory {
        Other = 0,
        Data = 1,
        Method = 2,
        Event = 3,
        Property = 4,
        Class = 5,
        Interface = 6,
        BaseClass = 7,
        InnerClass = 8,
        MostDerivedClass = 9,
    }

    internal enum DkmEvaluationResultAccessType {
        None = 0,
        Public = 1,
        Private = 2,
        Protected = 3,
        Final = 4,
        Internal = 5,
    }

    internal enum DkmEvaluationResultStorageType {
        None = 0,
        Global = 1,
        Static = 2,
        Register = 3,
    }

    [Flags]
    internal enum DkmEvaluationResultTypeModifierFlags {
        None = 0,
        Virtual = 1,
        Constant = 2,
        Synchronized = 4,
        Volatile = 8,
    }
}

namespace Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation {
    [Flags]
    internal enum DkmClrCompilationResultFlags {
        None = 0,
        PotentialSideEffect = 1,
        ReadOnlyResult = 2,
        BoolResult = 4,
    }

    // The payload carrying dynamic flags and tuple element names; only 'CustomTypeInfo' builds and unwraps it, for
    // the excluded Dkm-facing files
    internal class DkmClrCustomTypeInfo {
        public Guid PayloadTypeId { get; }
        public ReadOnlyCollection<byte> Payload { get; }

        private DkmClrCustomTypeInfo(Guid payloadTypeId, ReadOnlyCollection<byte> payload) {
            PayloadTypeId = payloadTypeId;
            Payload = payload;
        }

        public static DkmClrCustomTypeInfo Create(Guid payloadTypeId, ReadOnlyCollection<byte> payload) {
            return new DkmClrCustomTypeInfo(payloadTypeId, payload);
        }
    }
}
