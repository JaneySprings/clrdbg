using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace DotNet.Debugging.Engine.Variables;

internal enum MemberFilter {
    All,
    Public,
    NonPublic,
}

// A value as it is displayed: its type, text and the DebuggerTypeProxy instance standing in for its members
internal class ValueDisplay {
    public string TypeName { get; }
    public string Value { get; }
    public ICorDebugValue? ProxyValue { get; }
    public bool IsError { get; }

    public ValueDisplay(string typeName, string value, ICorDebugValue? proxyValue, bool isError) {
        TypeName = typeName;
        Value = value;
        ProxyValue = proxyValue;
        IsError = isError;
    }
}

// Builds the variables shown for a frame's scope and for the children of a value
internal class VariableProvider {
    private const string StaticMembersGroup = "Static members";
    private const string NonPublicMembersGroup = "Non-Public members";
    private const string RawViewGroup = "Raw View";
    private const string ResultsViewGroup = "Results View";
    private const string ResultsViewMessage = "Expanding the Results View will enumerate the IEnumerable";
    // The implicit evaluations (ToString, DebuggerDisplay) of one variables request share this time budget,
    // so a single slow override cannot stall a whole listing - vsdbg cuts the formatting off the same way
    private const int ImplicitEvalBudgetMilliseconds = 1000;

    private readonly ManagedDebugger debugger;
    private readonly VariableManager variableManager;
    private readonly Stopwatch implicitEvalTime = new Stopwatch();
    private bool limitImplicitEvals;

    public VariableProvider(ManagedDebugger debugger, VariableManager variableManager) {
        this.debugger = debugger;
        this.variableManager = variableManager;
    }

    public int CreateScopeReference(int threadId, int frameDepth) {
        return variableManager.Create(new VariableReference(VariableReferenceKind.Scope, threadId, frameDepth));
    }
    public async Task<List<VariableInfo>> GetVariablesAsync(int referenceId) {
        var reference = variableManager.Get(referenceId) ?? throw new ArgumentException("Invalid variables reference");
        var result = new List<VariableInfo>();
        limitImplicitEvals = true;
        implicitEvalTime.Reset();
        try {
            await AddVariablesAsync(reference, result);
        }
        finally {
            limitImplicitEvals = false;
        }
        return result;
    }
    private async Task AddVariablesAsync(VariableReference reference, List<VariableInfo> result) {
        switch (reference.Kind) {
            case VariableReferenceKind.Scope:
                await AddScopeVariablesAsync(reference, result);
                break;
            case VariableReferenceKind.Members:
                await AddChildrenAsync(reference, result, includeResultsView: true);
                break;
            case VariableReferenceKind.RawMembers:
                await AddChildrenAsync(reference, result, includeResultsView: false);
                break;
            case VariableReferenceKind.ResultsView:
                await AddResultsViewItemsAsync(reference, result);
                break;
            case VariableReferenceKind.NonPublicMembers:
                await AddMembersAsync(reference.Value!, reference.Value!.UnwrapDebugValueToObject().GetExactType(), MemberFilter.NonPublic, reference, result);
                SortMembers(result);
                break;
            case VariableReferenceKind.StaticMembers:
                await AddStaticMembersAndGroupAsync(reference, result);
                break;
            case VariableReferenceKind.NonPublicStaticMembers:
                await AddStaticMembersAsync(reference.Value!, reference.Value!.UnwrapDebugValueToObject().GetExactType(), MemberFilter.NonPublic, reference, result);
                SortMembers(result);
                break;
        }
    }
    public async Task<VariableInfo> SetVariableAsync(int referenceId, string name, string text) {
        var reference = variableManager.Get(referenceId) ?? throw new InvalidOperationException("The variables reference was not found");
        var target = FindVariableValue(reference, name) ?? throw new InvalidOperationException($"Variable '{name}' not found or setting its value is not supported");
        VariableWriter.Write(target, text);

        var evaluateName = reference.EvaluateName == null ? name : $"{reference.EvaluateName}.{name}";
        return await CreateVariableAsync(name, target, reference.ThreadId, reference.FrameDepth, evaluateName);
    }
    public async Task<VariableInfo> CreateVariableAsync(string name, ICorDebugValue value, int threadId, int frameDepth, string? evaluateName, VariableKind kind = VariableKind.Data, VariableVisibility? visibility = null) {
        var display = await FormatValueAsync(value, threadId, frameDepth, escapeStrings: true);
        var variable = new VariableInfo(name, display.Value, display.TypeName);
        variable.Kind = kind;
        variable.Visibility = visibility;
        variable.EvaluateName = evaluateName;
        variable.IsError = display.IsError;
        variable.VariablesReference = CreateChildrenReference(value, display.TypeName, threadId, frameDepth, display.ProxyValue, evaluateName);
        return variable;
    }
    // Formats a value, running its DebuggerDisplay expression and creating its DebuggerTypeProxy in the debuggee when it has them
    public async Task<ValueDisplay> FormatValueAsync(ICorDebugValue value, int threadId, int frameDepth, bool escapeStrings) {
        var formatted = ValueFormatter.Format(value, escapeStrings);
        var text = formatted.Value;
        if (formatted.RequiresDebuggerDisplay) {
            if (limitImplicitEvals && implicitEvalTime.ElapsedMilliseconds >= ImplicitEvalBudgetMilliseconds) {
                // The budget ran out, the value falls back to the display it would have without the override
                text = $"{{{formatted.TypeName}}}";
            }
            else {
                implicitEvalTime.Start();
                try {
                    var context = new EvaluationContext(debugger.GetThread(threadId), threadId, frameDepth, value);
                    using var result = await debugger.GetEvaluator().EvaluateAsync($"$\"{text}\"", context);
                    if (result.Error != null) {
                        DebuggerLoggingService.LogMessage($"DebuggerDisplay evaluation error: {result.Error}");
                        return new ValueDisplay(formatted.TypeName, result.Error, null, true);
                    }
                    text = ValueFormatter.Format(result.Value!, false).Value;
                }
                finally {
                    implicitEvalTime.Stop();
                }
            }
        }

        ICorDebugValue? proxyValue = null;
        if (formatted.DebuggerProxyTypeName != null)
            proxyValue = await CreateDebuggerProxyAsync(value, formatted.DebuggerProxyTypeName, threadId);
        return new ValueDisplay(formatted.TypeName, text, proxyValue, false);
    }

    private async Task AddScopeVariablesAsync(VariableReference reference, List<VariableInfo> result) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var function = frame.GetFunction();
        var module = debugger.GetModule(function.GetModule());

        await AddCurrentExceptionAsync(reference, result);
        var hoistedLocalsContainer = await AddArgumentsAsync(module, function, reference, result);
        // Locals captured by a lambda or hoisted into an async state machine live on the generated class,
        // the locals declared inside the lambda body itself are still plain IL locals
        if (hoistedLocalsContainer != null)
            await AddClosureMembersAsync(hoistedLocalsContainer, reference, result);
        await AddLocalsAsync(module, function, reference, result);
    }
    private async Task AddCurrentExceptionAsync(VariableReference reference, List<VariableInfo> result) {
        var exception = debugger.GetCurrentException(reference.ThreadId);
        if (exception == null)
            return;
        await AddVariableAsync(result, "$exception", async () => await CreateVariableAsync("$exception", exception, reference.ThreadId, reference.FrameDepth, "$exception"));
    }
    // Returns the generated closure or state machine instance holding the hoisted locals, when the frame is a lambda or an async method
    private async Task<ICorDebugValue?> AddArgumentsAsync(ModuleInfo module, ICorDebugFunction function, VariableReference reference, List<VariableInfo> result) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var arguments = frame.GetArguments();
        if (arguments.Length == 0)
            return null;

        var metadataImport = module.Module.GetMetaDataInterface<IMetaDataImport>();
        var methodProps = metadataImport.GetMethodProps(function.GetToken());
        var isStatic = methodProps.pdwAttr.IsMdStatic();

        // The arguments include the implicit 'this' of instance methods, the metadata parameters do not
        ICorDebugValue? hoistedLocalsContainer = null;
        if (!isStatic) {
            var thisValue = arguments[0];
            if (methodProps.szMethod == "MoveNext" || methodProps.szMethod.Contains(">b")) {
                var containingTypeName = metadataImport.GetTypeDefProps(function.GetClass().GetToken()).szTypeDef;
                var containingTypeKind = GeneratedNameParser.GetKind(containingTypeName);
                if (containingTypeKind is GeneratedNameKind.StateMachineType or GeneratedNameKind.LambdaDisplayClass) {
                    // 'this' is the generated class, the user's 'this' is one of its fields (absent when the user's method is static)
                    hoistedLocalsContainer = thisValue;
                    thisValue = GetHoistedThis(thisValue, metadataImport);
                }
            }
            if (thisValue != null)
                await AddVariableAsync(result, "this", async () => await CreateVariableAsync("this", thisValue, reference.ThreadId, reference.FrameDepth, "this"));
        }

        var skipCount = isStatic ? 0 : 1;
        for (var i = skipCount; i < arguments.Length; i++) {
            var paramDef = metadataImport.GetParamForMethodIndex(function.GetToken(), i - skipCount + 1);
            var name = metadataImport.GetParamProps(paramDef).szName;
            if (name == null)
                continue;
            var argument = arguments[i];
            await AddVariableAsync(result, name, async () => await CreateVariableAsync(name, argument, reference.ThreadId, reference.FrameDepth, name));
        }
        return hoistedLocalsContainer;
    }
    private async Task AddLocalsAsync(ModuleInfo module, ICorDebugFunction function, VariableReference reference, List<VariableInfo> result) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var locals = frame.GetLocalVariables();
        if (locals.Length == 0)
            return;

        var ilOffset = frame.GetIP().pnOffset;
        for (var i = 0; i < locals.Length; i++) {
            // Compiler generated locals (e.g. a DefaultInterpolatedStringHandler) have no name
            var name = module.MetadataReader.GetLocalVariableName(function.GetToken(), i, ilOffset);
            if (name == null)
                continue;
            var local = locals[i];
            await AddVariableAsync(result, name, async () => await CreateVariableAsync(name, local, reference.ThreadId, reference.FrameDepth, name));
        }
    }
    // Lists the hoisted locals of a closure and of the closures enclosing it, linked through their '<>8__' fields
    private async Task AddClosureMembersAsync(ICorDebugValue closure, VariableReference reference, List<VariableInfo> result) {
        await AddMembersAsync(closure, closure.GetExactType(), MemberFilter.All, reference, result);

        var objectValue = closure.UnwrapDebugValueToObject();
        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        foreach (var field in metadataImport.EnumFields(corClass.GetToken())) {
            if (GeneratedNameParser.GetKind(metadataImport.GetFieldProps(field).szField) != GeneratedNameKind.DisplayClassLocalOrField)
                continue;
            await AddClosureMembersAsync(objectValue.GetFieldValue(corClass, field), reference, result);
            break;
        }
    }
    // The user's 'this' captured by a closure or state machine, following the chain of enclosing closures
    private static ICorDebugValue? GetHoistedThis(ICorDebugValue generatedInstance, IMetaDataImport metadataImport) {
        var objectValue = generatedInstance.UnwrapDebugValueToObject();
        var corClass = objectValue.GetClass();
        foreach (var field in metadataImport.EnumFields(corClass.GetToken())) {
            var kind = GeneratedNameParser.GetKind(metadataImport.GetFieldProps(field).szField);
            if (kind == GeneratedNameKind.ThisProxyField)
                return objectValue.GetFieldValue(corClass, field);
            if (kind == GeneratedNameKind.DisplayClassLocalOrField) {
                var parentClosure = objectValue.GetFieldValue(corClass, field);
                var parentMetadataImport = parentClosure.UnwrapDebugValueToObject().GetClass().GetModule().GetMetaDataInterface<IMetaDataImport>();
                return GetHoistedThis(parentClosure, parentMetadataImport);
            }
        }
        return null;
    }

    private async Task AddChildrenAsync(VariableReference reference, List<VariableInfo> result, bool includeResultsView) {
        var value = reference.Value!;
        if (reference.ProxyValue != null) {
            // The public members of the DebuggerTypeProxy stand in for the value's own, which stay reachable through 'Raw View'
            await AddMembersAndGroupsAsync(reference.ProxyValue, reference.ProxyValue.UnwrapDebugValueToObject().GetExactType(), reference, result, includeNonPublicGroup: false);
            var rawViewReference = variableManager.Create(new VariableReference(VariableReferenceKind.RawMembers, reference.ThreadId, reference.FrameDepth, value, null, reference.EvaluateName));
            result.Add(CreateGroup(RawViewGroup, rawViewReference));
            SortMembers(result);
            return;
        }

        var unwrapped = value.UnwrapDebugValue();
        if (unwrapped is ICorDebugArrayValue arrayValue) {
            await AddArrayElementsAsync(arrayValue, reference, result, reference.EvaluateName);
        }
        else if (unwrapped is ICorDebugObjectValue objectValue) {
            var type = objectValue.GetExactType();
            await AddMembersAndGroupsAsync(value, type, reference, result, includeNonPublicGroup: true);
            if (includeResultsView && IsEnumerableType(type))
                result.Add(CreateResultsViewNode(reference));
            SortMembers(result);
        }
        else {
            throw new InvalidOperationException("The value has no children");
        }
    }
    private async Task AddMembersAndGroupsAsync(ICorDebugValue value, ICorDebugType type, VariableReference reference, List<VariableInfo> result, bool includeNonPublicGroup) {
        // User code types show all their members inline, library types get the 'Non-Public members' group
        var filter = includeNonPublicGroup && IsUserCodeType(type) ? MemberFilter.All : MemberFilter.Public;
        var summary = await AddMembersAsync(value, type, filter, reference, result);
        if (summary.HasStaticMembers) {
            var staticReference = variableManager.Create(new VariableReference(VariableReferenceKind.StaticMembers, reference.ThreadId, reference.FrameDepth, value, null, reference.EvaluateName));
            result.Add(CreateGroup(StaticMembersGroup, staticReference));
        }
        if (includeNonPublicGroup && summary.HasNonPublicMembers) {
            var nonPublicReference = variableManager.Create(new VariableReference(VariableReferenceKind.NonPublicMembers, reference.ThreadId, reference.FrameDepth, value, null, reference.EvaluateName));
            result.Add(CreateGroup(NonPublicMembersGroup, nonPublicReference));
        }
    }
    private async Task AddStaticMembersAndGroupAsync(VariableReference reference, List<VariableInfo> result) {
        var value = reference.Value!;
        var type = value.UnwrapDebugValueToObject().GetExactType();
        var filter = IsUserCodeType(type) ? MemberFilter.All : MemberFilter.Public;
        var hasNonPublicMembers = await AddStaticMembersAsync(value, type, filter, reference, result);
        if (hasNonPublicMembers) {
            var nonPublicReference = variableManager.Create(new VariableReference(VariableReferenceKind.NonPublicStaticMembers, reference.ThreadId, reference.FrameDepth, value, null, reference.EvaluateName));
            result.Add(CreateGroup(NonPublicMembersGroup, nonPublicReference));
        }
        SortMembers(result);
    }
    // Lists the instance members declared by the type and its base types, reporting whether the 'Static members' and 'Non-Public members' groups are needed
    private async Task<MemberSummary> AddMembersAsync(ICorDebugValue value, ICorDebugType type, MemberFilter filter, VariableReference reference, List<VariableInfo> result) {
        var corClass = type.GetClass();
        var typeToken = corClass.GetToken();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var instanceFields = metadataImport.EnumFields(typeToken).Where(it => !it.IsStatic(metadataImport)).ToList();
        var instanceProperties = metadataImport.EnumProperties(typeToken).Where(it => !it.IsStatic(metadataImport) && !it.IsIndexer(metadataImport)).ToList();

        var summary = new MemberSummary();
        summary.HasStaticMembers = metadataImport.EnumFields(typeToken).Any(it => it.IsStatic(metadataImport))
            || metadataImport.EnumProperties(typeToken).Any(it => it.IsStatic(metadataImport));
        summary.HasNonPublicMembers = filter == MemberFilter.Public && HasNonPublicMembers(metadataImport, instanceFields, instanceProperties);

        await AddFieldsAsync(FilterFields(metadataImport, instanceFields, filter), metadataImport, type, value, reference, result);
        await AddPropertiesAsync(FilterProperties(metadataImport, instanceProperties, filter), metadataImport, type, value, reference, result);

        var baseType = type.GetBaseType();
        if (baseType == null || IsRootType(baseType))
            return summary;
        var baseSummary = await AddMembersAsync(value, baseType, filter, reference, result);
        summary.HasStaticMembers |= baseSummary.HasStaticMembers;
        summary.HasNonPublicMembers |= baseSummary.HasNonPublicMembers;
        return summary;
    }
    // Returns whether the 'Non-Public members' group is needed
    private async Task<bool> AddStaticMembersAsync(ICorDebugValue value, ICorDebugType type, MemberFilter filter, VariableReference reference, List<VariableInfo> result) {
        var corClass = type.GetClass();
        var typeToken = corClass.GetToken();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var staticFields = metadataImport.EnumFields(typeToken).Where(it => it.IsStatic(metadataImport)).ToList();
        var staticProperties = metadataImport.EnumProperties(typeToken).Where(it => it.IsStatic(metadataImport) && !it.IsIndexer(metadataImport)).ToList();
        var hasNonPublicMembers = filter == MemberFilter.Public && HasNonPublicMembers(metadataImport, staticFields, staticProperties);

        await AddFieldsAsync(FilterFields(metadataImport, staticFields, filter), metadataImport, type, value, reference, result);
        await AddPropertiesAsync(FilterProperties(metadataImport, staticProperties, filter), metadataImport, type, value, reference, result);

        var baseType = type.GetBaseType();
        if (baseType == null || IsRootType(baseType))
            return hasNonPublicMembers;
        return hasNonPublicMembers | await AddStaticMembersAsync(value, baseType, filter, reference, result);
    }
    private async Task AddFieldsAsync(List<FieldDefToken> fields, IMetaDataImport metadataImport, ICorDebugType type, ICorDebugValue value, VariableReference reference, List<VariableInfo> result) {
        var corClass = type.GetClass();
        foreach (var field in fields) {
            var fieldProps = metadataImport.GetFieldProps(field);
            if (fieldProps.szField == null)
                continue;

            await AddVariableAsync(result, fieldProps.szField, async () => {
                if (!TryGetDisplayName(fieldProps.szField, out var name))
                    return null;
                var browsable = GetDebuggerBrowsableState(metadataImport, field);
                if (browsable == DebuggerBrowsableState.Never)
                    return null;

                var isStatic = fieldProps.pdwAttr.IsFdStatic();
                var visibility = GetVisibility(fieldProps.pdwAttr);
                var evaluateName = GetMemberEvaluateName(name, isStatic, reference.EvaluateName, type);
                if (fieldProps.pdwAttr.IsFdLiteral()) {
                    var literal = new VariableInfo(name, ValueFormatter.FormatLiteral(fieldProps.ppValue, fieldProps.pcchValue, fieldProps.pdwCPlusTypeFlag), TypeNameFormatter.GetPrimitiveTypeName(fieldProps.pdwCPlusTypeFlag));
                    literal.Visibility = visibility;
                    literal.EvaluateName = evaluateName;
                    return literal;
                }

                var fieldValue = isStatic
                    ? await debugger.FuncEval.GetStaticFieldValueAsync(type, field, debugger.GetILFrame(reference.ThreadId, reference.FrameDepth))
                    : value.UnwrapDebugValueToObject().GetFieldValue(corClass, field);
                if (browsable == DebuggerBrowsableState.RootHidden && fieldValue.UnwrapDebugValue() is ICorDebugArrayValue arrayValue) {
                    await AddArrayElementsAsync(arrayValue, reference, result, evaluateName);
                    return null;
                }
                return await CreateVariableAsync(name, fieldValue, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Data, visibility);
            });
        }
    }
    private async Task AddPropertiesAsync(List<PropertyToken> properties, IMetaDataImport metadataImport, ICorDebugType type, ICorDebugValue value, VariableReference reference, List<VariableInfo> result) {
        var module = type.GetClass().GetModule();
        foreach (var property in properties) {
            var propertyProps = metadataImport.GetPropertyProps(property);
            var name = propertyProps.szProperty;
            if (name == null || propertyProps.pmdGetter.IsNil)
                continue;

            await AddVariableAsync(result, name, async () => {
                var browsable = GetDebuggerBrowsableState(metadataImport, property);
                if (browsable == DebuggerBrowsableState.Never)
                    return null;

                var getterAttributes = metadataImport.GetMethodProps(propertyProps.pmdGetter).pdwAttr;
                var isStatic = getterAttributes.IsMdStatic();
                var visibility = GetVisibility(getterAttributes);
                var evaluateName = GetMemberEvaluateName(name, isStatic, reference.EvaluateName, type);

                // The getter is invoked with the original reference value, not the dereferenced object
                var getter = module.GetFunctionFromToken(propertyProps.pmdGetter);
                var eval = debugger.GetThread(reference.ThreadId).CreateEval();
                ICorDebugValue[] arguments = isStatic ? [] : [value];
                var propertyValue = await debugger.FuncEval.CallFunctionAsync(eval, getter, value.GetExactType().GetTypeParameters(), arguments);
                if (propertyValue == null)
                    return null;

                var keepHandle = false;
                try {
                    if (browsable == DebuggerBrowsableState.RootHidden && propertyValue.UnwrapDebugValue() is ICorDebugArrayValue arrayValue) {
                        await AddArrayElementsAsync(arrayValue, reference, result, evaluateName);
                        return null;
                    }
                    var variable = await CreateVariableAsync(name, propertyValue, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Property, visibility);
                    // A value with children stays alive behind its variables reference
                    keepHandle = variable.VariablesReference != 0;
                    return variable;
                }
                finally {
                    if (!keepHandle && propertyValue is ICorDebugHandleValue handle)
                        handle.TryDispose();
                }
            });
        }
    }
    // The node offering deferred enumeration of an IEnumerable value, its expansion runs the enumeration in the debuggee
    private VariableInfo CreateResultsViewNode(VariableReference reference) {
        var resultsReference = variableManager.Create(new VariableReference(VariableReferenceKind.ResultsView, reference.ThreadId, reference.FrameDepth, reference.Value, null, reference.EvaluateName));
        var node = new VariableInfo(ResultsViewGroup, ResultsViewMessage, string.Empty);
        node.Kind = VariableKind.ResultsView;
        node.VariablesReference = resultsReference;
        return node;
    }
    private async Task AddResultsViewItemsAsync(VariableReference reference, List<VariableInfo> result) {
        var value = reference.Value!;
        var context = new EvaluationContext(debugger.GetThread(reference.ThreadId), reference.ThreadId, reference.FrameDepth, value);
        await EnsureSystemLinqLoadedAsync(context);
        var isGeneric = true;
        var evaluation = await debugger.GetEvaluator().EvaluateAsync("System.Linq.Enumerable.ToArray(this)", context);
        if (evaluation.Error != null) {
            // The value only implements the non generic IEnumerable, so the element type cannot be inferred
            evaluation.Dispose();
            isGeneric = false;
            evaluation = await debugger.GetEvaluator().EvaluateAsync("System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Cast<object>(this))", context);
        }
        try {
            if (evaluation.Error != null)
                throw new EvaluationException(evaluation.Error);
            if (evaluation.Value!.UnwrapDebugValue() is not ICorDebugArrayValue arrayValue)
                throw new InvalidOperationException("The enumeration did not produce an array");

            await AddArrayElementsAsync(arrayValue, reference, result, GetResultsViewEvaluateName(reference, arrayValue, isGeneric));
            if (evaluation.Value is ICorDebugHandleValue handle) {
                // The elements point into the array, so the handle stays alive behind the variables references
                evaluation.KeepHandle();
                variableManager.Keep(handle);
            }
        }
        finally {
            evaluation.Dispose();
        }
    }
    // The enumeration compiles against System.Linq, which the debuggee may not have loaded. Deliberate divergence:
    // vsdbg enumerates without loading it (Concord interprets the IL against metadata read from disk), clrdbg loads it
    private async Task EnsureSystemLinqLoadedAsync(EvaluationContext context) {
        if (debugger.Modules.Any(it => string.Equals(it.Name, "System.Linq.dll", StringComparison.OrdinalIgnoreCase)))
            return;
        using var loadResult = await debugger.GetEvaluator().EvaluateAsync("System.Reflection.Assembly.Load(\"System.Linq\")", context);
        if (loadResult.Error != null)
            throw new EvaluationException(loadResult.Error);
    }
    // The expression vsdbg reports for enumerated items: 'new System.Linq.SystemCore_EnumerableDebugView<T>(value).Items'
    private static string? GetResultsViewEvaluateName(VariableReference reference, ICorDebugArrayValue arrayValue, bool isGeneric) {
        if (reference.EvaluateName == null)
            return null;
        if (!isGeneric)
            return $"new System.Linq.SystemCore_EnumerableDebugView({reference.EvaluateName}).Items";
        var elementTypeName = TypeNameFormatter.GetTypeName(arrayValue.GetExactType().GetFirstTypeParameter());
        return $"new System.Linq.SystemCore_EnumerableDebugView<{elementTypeName}>({reference.EvaluateName}).Items";
    }
    private async Task AddArrayElementsAsync(ICorDebugArrayValue arrayValue, VariableReference reference, List<VariableInfo> result, string? parentEvaluateName) {
        if (arrayValue.GetRank() > 1)
            throw new NotImplementedException("Multidimensional arrays are not supported yet");

        // The elements are read up front, a DebuggerDisplay evaluation below may neuter the array value
        var count = arrayValue.GetCount();
        var elements = new List<ICorDebugValue>(count);
        for (var i = 0; i < count; i++)
            elements.Add(arrayValue.GetElement(1, [checked((uint)i)]));

        for (var i = 0; i < elements.Count; i++) {
            var name = $"[{i}]";
            var element = elements[i];
            var evaluateName = parentEvaluateName == null ? name : parentEvaluateName + name;
            await AddVariableAsync(result, name, async () => await CreateVariableAsync(name, element, reference.ThreadId, reference.FrameDepth, evaluateName));
        }
    }
    // A member that cannot be read is shown with the error as its value
    private static async Task AddVariableAsync(List<VariableInfo> result, string name, Func<Task<VariableInfo?>> create) {
        try {
            var variable = await create();
            if (variable != null)
                result.Add(variable);
        }
        catch (Exception ex) {
            result.Add(VariableInfo.CreateError(name, ex.Message));
        }
    }

    private async Task<ICorDebugValue> CreateDebuggerProxyAsync(ICorDebugValue value, string proxyTypeName, int threadId) {
        var valueType = value.GetExactType();
        var module = valueType.GetClass().GetModule();
        var metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
        var proxyTypeDef = metadataImport.FindNestedTypeDef(proxyTypeName) ?? throw new InvalidOperationException($"The debugger proxy type '{proxyTypeName}' was not found");
        // TODO: select the constructor by signature, proxy types may have several
        var constructorDef = metadataImport.FindMethod(proxyTypeDef, ".ctor", 0, 0);
        var constructor = module.GetFunctionFromToken(constructorDef);

        var eval = debugger.GetThread(threadId).CreateEval();
        var proxy = await debugger.FuncEval.NewObjectAsync(eval, constructor, valueType.GetTypeParameters(), [value]);
        return proxy ?? throw new InvalidOperationException($"The debugger proxy '{proxyTypeName}' could not be created");
    }
    // Non-zero for values with members or elements to expand
    private int CreateChildrenReference(ICorDebugValue value, string typeName, int threadId, int frameDepth, ICorDebugValue? proxyValue, string? evaluateName) {
        var unwrapped = value.UnwrapDebugValue();
        if (unwrapped is ICorDebugArrayValue arrayValue) {
            if (arrayValue.GetCount() == 0)
                return 0;
            return variableManager.Create(new VariableReference(VariableReferenceKind.Members, threadId, frameDepth, value, proxyValue, evaluateName));
        }
        if (unwrapped is not ICorDebugObjectValue objectValue)
            return 0;

        if (typeName.EndsWith('?')) {
            // A nullable has the children of its value, if any
            var underlyingValue = ValueFormatter.GetNullableValue(objectValue);
            if (underlyingValue is not ICorDebugObjectValue underlyingObject)
                return 0;
            objectValue = underlyingObject;
        }

        var elementType = objectValue.GetElementType();
        // Strings, decimals and boxed primitives are displayed as primitives
        if (elementType == CorElementType.STRING || typeName == "decimal" || typeName == "decimal?" || TypeNameFormatter.IsPrimitiveTypeName(typeName))
            return 0;
        if (elementType is CorElementType.CLASS or CorElementType.VALUETYPE or CorElementType.SZARRAY or CorElementType.ARRAY)
            return variableManager.Create(new VariableReference(VariableReferenceKind.Members, threadId, frameDepth, value, proxyValue, evaluateName));
        return 0;
    }

    private ICorDebugValue? FindVariableValue(VariableReference reference, string name) {
        if (reference.Kind == VariableReferenceKind.Scope)
            return FindFrameVariableValue(reference, name);
        if (reference.Value == null)
            return null;

        var unwrapped = reference.Value.UnwrapDebugValue();
        if (unwrapped is ICorDebugArrayValue arrayValue && name.StartsWith('[') && name.EndsWith(']')) {
            if (!uint.TryParse(name.AsSpan(1, name.Length - 2), out var index))
                return null;
            return arrayValue.GetElement(1, [index]);
        }
        if (unwrapped is ICorDebugObjectValue objectValue)
            return objectValue.GetFieldValueByName(debugger.GetILFrame(reference.ThreadId, reference.FrameDepth), name);
        return null;
    }
    private ICorDebugValue? FindFrameVariableValue(VariableReference reference, string name) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var function = frame.GetFunction();
        var module = debugger.GetModule(function.GetModule());
        var ilOffset = frame.GetIP().pnOffset;

        var locals = frame.GetLocalVariables();
        for (var i = 0; i < locals.Length; i++) {
            if (module.MetadataReader.GetLocalVariableName(function.GetToken(), i, ilOffset) == name)
                return locals[i];
        }

        var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var skipCount = metadataImport.GetMethodProps(function.GetToken()).pdwAttr.IsMdStatic() ? 0 : 1;
        var arguments = frame.GetArguments();
        for (var i = skipCount; i < arguments.Length; i++) {
            var paramDef = metadataImport.GetParamForMethodIndex(function.GetToken(), i - skipCount + 1);
            if (metadataImport.GetParamProps(paramDef).szName == name)
                return arguments[i];
        }
        return null;
    }

    private bool IsUserCodeType(ICorDebugType type) {
        try {
            var module = debugger.FindModule(type.GetClass().GetModule());
            return module != null && module.IsUserCode;
        }
        catch {
            return false;
        }
    }
    private static bool IsRootType(ICorDebugType type) {
        var corClass = type.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        return metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef is "System.Object" or "System.ValueType" or "System.Enum";
    }
    // The C# compiler emits the transitive closure of implemented interfaces, so checking the base classes is enough
    private static bool IsEnumerableType(ICorDebugType type) {
        for (var current = type; current != null && !IsRootType(current); current = current.GetBaseType()) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            foreach (var impl in metadataImport.EnumInterfaceImpls(corClass.GetToken())) {
                var interfaceName = GetInterfaceName(metadataImport, metadataImport.GetInterfaceImplProps(impl).ptkIface);
                if (interfaceName is "System.Collections.IEnumerable" or "System.Collections.Generic.IEnumerable`1")
                    return true;
            }
        }
        return false;
    }
    private static string? GetInterfaceName(IMetaDataImport metadataImport, MetadataToken token) {
        switch (token.Type) {
            case CorTokenType.mdtTypeDef:
                return metadataImport.GetTypeDefProps((int)token).szTypeDef;
            case CorTokenType.mdtTypeRef:
                return metadataImport.GetTypeRefProps((int)token).szName;
            case CorTokenType.mdtTypeSpec:
                return GetTypeSpecName(metadataImport, (int)token);
            default:
                return null;
        }
    }
    // A generic interface ('IEnumerable<string>') is a type spec: GENERICINST, CLASS or VALUETYPE, then the coded type token
    private static string? GetTypeSpecName(IMetaDataImport metadataImport, TypeSpecToken token) {
        var (signature, size) = metadataImport.GetTypeSpecFromToken(token);
        if (size < 3 || Marshal.ReadByte(signature, 0) != (byte)CorElementType.GENERICINST)
            return null;
        var offset = 2;
        var codedToken = ReadCompressedUInt(signature, ref offset, size);
        if (codedToken == null)
            return null;
        var rid = (int)(codedToken.Value >> 2);
        switch (codedToken.Value & 3) {
            case 0:
                return GetInterfaceName(metadataImport, (int)CorTokenType.mdtTypeDef | rid);
            case 1:
                return GetInterfaceName(metadataImport, (int)CorTokenType.mdtTypeRef | rid);
            default:
                return null;
        }
    }
    private static uint? ReadCompressedUInt(nint data, ref int offset, int size) {
        if (offset >= size)
            return null;
        var first = Marshal.ReadByte(data, offset);
        if ((first & 0x80) == 0) {
            offset += 1;
            return first;
        }
        if ((first & 0xC0) == 0x80) {
            if (offset + 2 > size)
                return null;
            var value = ((first & 0x3Fu) << 8) | Marshal.ReadByte(data, offset + 1);
            offset += 2;
            return value;
        }
        if (offset + 4 > size)
            return null;
        var wide = ((first & 0x1Fu) << 24) | ((uint)Marshal.ReadByte(data, offset + 1) << 16) | ((uint)Marshal.ReadByte(data, offset + 2) << 8) | Marshal.ReadByte(data, offset + 3);
        offset += 4;
        return wide;
    }
    private static bool HasNonPublicMembers(IMetaDataImport metadataImport, List<FieldDefToken> fields, List<PropertyToken> properties) {
        return fields.Any(it => !it.IsPublic(metadataImport) && TryGetDisplayName(metadataImport.GetFieldProps(it).szField, out _))
            || properties.Any(it => it.HasGetter(metadataImport) && !it.IsPublic(metadataImport));
    }
    private static List<FieldDefToken> FilterFields(IMetaDataImport metadataImport, List<FieldDefToken> fields, MemberFilter filter) {
        if (filter == MemberFilter.All)
            return fields;
        return fields.Where(it => it.IsPublic(metadataImport) == (filter == MemberFilter.Public)).ToList();
    }
    private static List<PropertyToken> FilterProperties(IMetaDataImport metadataImport, List<PropertyToken> properties, MemberFilter filter) {
        if (filter == MemberFilter.All)
            return properties;
        return properties.Where(it => it.HasGetter(metadataImport) && it.IsPublic(metadataImport) == (filter == MemberFilter.Public)).ToList();
    }
    // Compiler generated fields are hidden, except hoisted locals ('<count>5__1') which are shown under their original name
    private static bool TryGetDisplayName(string? fieldName, out string displayName) {
        displayName = fieldName ?? string.Empty;
        if (fieldName == null)
            return false;
        if (!GeneratedNameParser.TryParseGeneratedName(fieldName, out var kind, out var openBracketOffset, out var closeBracketOffset))
            return true;
        if (kind != GeneratedNameKind.HoistedLocalField)
            return false;
        displayName = fieldName.Substring(openBracketOffset + 1, closeBracketOffset - openBracketOffset - 1);
        return true;
    }
    private static DebuggerBrowsableState? GetDebuggerBrowsableState(IMetaDataImport metadataImport, MetadataToken token) {
        if (metadataImport.TryGetCustomAttributeByName(token, AttributeNames.DebuggerBrowsable, out var data, out var size) != Cor.S_OK)
            return null;
        return (DebuggerBrowsableState)CustomAttributeReader.ReadInt32Argument(data, size);
    }
    // 'parent.Member' for instance members, 'Namespace.Type.Member' for static ones, the bare name for hoisted locals
    private static string GetMemberEvaluateName(string memberName, bool isStatic, string? parentEvaluateName, ICorDebugType declaringType) {
        if (isStatic) {
            try {
                return $"{TypeNameFormatter.GetTypeName(declaringType)}.{memberName}";
            }
            catch {
                // Fall through to the instance form
            }
        }
        return parentEvaluateName == null ? memberName : $"{parentEvaluateName}.{memberName}";
    }
    private static VariableVisibility GetVisibility(CorFieldAttr attributes) {
        return (attributes & CorFieldAttr.fdFieldAccessMask) switch {
            CorFieldAttr.fdPublic => VariableVisibility.Public,
            CorFieldAttr.fdFamily or CorFieldAttr.fdFamORAssem => VariableVisibility.Protected,
            CorFieldAttr.fdAssembly or CorFieldAttr.fdFamANDAssem => VariableVisibility.Internal,
            _ => VariableVisibility.Private
        };
    }
    private static VariableVisibility GetVisibility(CorMethodAttr attributes) {
        return (attributes & CorMethodAttr.mdMemberAccessMask) switch {
            CorMethodAttr.mdPublic => VariableVisibility.Public,
            CorMethodAttr.mdFamily or CorMethodAttr.mdFamORAssem => VariableVisibility.Protected,
            CorMethodAttr.mdAssem or CorMethodAttr.mdFamANDAssem => VariableVisibility.Internal,
            _ => VariableVisibility.Private
        };
    }
    private static VariableInfo CreateGroup(string name, int variablesReference) {
        var group = new VariableInfo(name, string.Empty, string.Empty);
        group.Kind = VariableKind.Group;
        group.VariablesReference = variablesReference;
        return group;
    }
    // Ordinal order ('AAA AAB ... aaa aab') with the groups at the end
    private static void SortMembers(List<VariableInfo> members) {
        members.Sort((left, right) => {
            var rankComparison = GetSortRank(left).CompareTo(GetSortRank(right));
            return rankComparison != 0 ? rankComparison : string.CompareOrdinal(left.Name, right.Name);
        });
    }
    private static int GetSortRank(VariableInfo member) {
        return member.Name switch {
            StaticMembersGroup => 1,
            NonPublicMembersGroup => 2,
            RawViewGroup => 3,
            ResultsViewGroup => 4,
            _ => 0
        };
    }

    private class MemberSummary {
        public bool HasStaticMembers { get; set; }
        public bool HasNonPublicMembers { get; set; }
    }
}
