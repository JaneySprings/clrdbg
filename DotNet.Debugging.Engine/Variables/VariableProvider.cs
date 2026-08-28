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
    private const string ResultsViewEmptyName = "Empty";
    private const string ResultsViewEmptyMessage = "\"Enumeration yielded no results\"";
    // The implicit evaluations (ToString, DebuggerDisplay) of one variables request share this time budget,
    // so a single slow override cannot stall a whole listing - Microsoft's debugger cuts the formatting off the same way
    private const int ImplicitEvalBudgetMilliseconds = 2000;

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
    // Lists the variables behind a reference by name, then reads and formats only the ones the requested page holds -
    // expanding a large collection costs one page of evaluations, not one per member
    public async Task<VariablePage> GetVariablesAsync(int referenceId, int start, int count) {
        var reference = variableManager.Get(referenceId) ?? throw new ArgumentException("Invalid variables reference");
        var slots = new List<VariableSlot>();
        await AddVariablesAsync(reference, slots);

        var pageStart = Math.Clamp(start, 0, slots.Count);
        var pageEnd = Math.Clamp(start + count, pageStart, slots.Count);
        var variables = new List<VariableInfo>(pageEnd - pageStart);
        limitImplicitEvals = true;
        implicitEvalTime.Reset();
        try {
            for (var i = pageStart; i < pageEnd; i++) {
                var variable = await slots[i].MaterializeAsync();
                if (variable != null)
                    variables.Add(variable);
            }
        }
        finally {
            limitImplicitEvals = false;
        }
        return new VariablePage(variables, slots.Count);
    }
    private async Task AddVariablesAsync(VariableReference reference, List<VariableSlot> result) {
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

    private async Task AddScopeVariablesAsync(VariableReference reference, List<VariableSlot> result) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var function = frame.GetFunction();
        var module = debugger.GetModule(function.GetModule());

        AddCurrentException(reference, result);
        var hoistedLocalsContainer = AddArguments(module, function, reference, result);
        // Locals captured by a lambda or hoisted into an async state machine live on the generated class,
        // the locals declared inside the lambda body itself are still plain IL locals
        if (hoistedLocalsContainer != null)
            await AddClosureMembersAsync(hoistedLocalsContainer, reference, result);
        AddLocals(module, function, reference, result);
    }
    private void AddCurrentException(VariableReference reference, List<VariableSlot> result) {
        var exception = debugger.GetCurrentException(reference.ThreadId);
        if (exception == null)
            return;
        result.Add(new VariableSlot("$exception", async () => await CreateVariableAsync("$exception", exception, reference.ThreadId, reference.FrameDepth, "$exception")));
    }
    // Returns the generated closure or state machine instance holding the hoisted locals, when the frame is a lambda or an async method
    private ICorDebugValue? AddArguments(ModuleInfo module, ICorDebugFunction function, VariableReference reference, List<VariableSlot> result) {
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
            if (thisValue != null) {
                var capturedThis = thisValue;
                result.Add(new VariableSlot("this", async () => await CreateVariableAsync("this", capturedThis, reference.ThreadId, reference.FrameDepth, "this")));
            }
        }

        var skipCount = isStatic ? 0 : 1;
        for (var i = skipCount; i < arguments.Length; i++) {
            var paramDef = metadataImport.GetParamForMethodIndex(function.GetToken(), i - skipCount + 1);
            var name = metadataImport.GetParamProps(paramDef).szName;
            if (name == null)
                continue;
            var argument = arguments[i];
            result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, argument, reference.ThreadId, reference.FrameDepth, name)));
        }
        return hoistedLocalsContainer;
    }
    private void AddLocals(ModuleInfo module, ICorDebugFunction function, VariableReference reference, List<VariableSlot> result) {
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
            result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, local, reference.ThreadId, reference.FrameDepth, name)));
        }
    }
    // Lists the hoisted locals of a closure and of the closures enclosing it, linked through their '<>8__' fields
    private async Task AddClosureMembersAsync(ICorDebugValue closure, VariableReference reference, List<VariableSlot> result) {
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

    private async Task AddChildrenAsync(VariableReference reference, List<VariableSlot> result, bool includeResultsView) {
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
        if (unwrapped is ICorDebugArrayValue) {
            AddArrayElementSlots(value, reference, result, reference.EvaluateName);
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
    private async Task AddMembersAndGroupsAsync(ICorDebugValue value, ICorDebugType type, VariableReference reference, List<VariableSlot> result, bool includeNonPublicGroup) {
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
    private async Task AddStaticMembersAndGroupAsync(VariableReference reference, List<VariableSlot> result) {
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
    private async Task<MemberSummary> AddMembersAsync(ICorDebugValue value, ICorDebugType type, MemberFilter filter, VariableReference reference, List<VariableSlot> result) {
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
    private async Task<bool> AddStaticMembersAsync(ICorDebugValue value, ICorDebugType type, MemberFilter filter, VariableReference reference, List<VariableSlot> result) {
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
    private async Task AddFieldsAsync(List<FieldDefToken> fields, IMetaDataImport metadataImport, ICorDebugType type, ICorDebugValue value, VariableReference reference, List<VariableSlot> result) {
        var corClass = type.GetClass();
        foreach (var field in fields) {
            // Which fields the listing holds and how they are named comes from metadata alone, no value is read here
            var fieldProps = metadataImport.GetFieldProps(field);
            if (fieldProps.szField == null)
                continue;
            try {
                if (!TryGetDisplayName(fieldProps.szField, out var name))
                    continue;
                var browsable = GetDebuggerBrowsableState(metadataImport, field);
                if (browsable == DebuggerBrowsableState.Never)
                    continue;

                var isStatic = fieldProps.pdwAttr.IsFdStatic();
                var visibility = GetVisibility(fieldProps.pdwAttr);
                var evaluateName = GetMemberEvaluateName(name, isStatic, reference.EvaluateName, type);
                if (fieldProps.pdwAttr.IsFdLiteral()) {
                    var literal = new VariableInfo(name, ValueFormatter.FormatLiteral(fieldProps.ppValue, fieldProps.pcchValue, fieldProps.pdwCPlusTypeFlag), TypeNameFormatter.GetPrimitiveTypeName(fieldProps.pdwCPlusTypeFlag));
                    literal.Visibility = visibility;
                    literal.EvaluateName = evaluateName;
                    result.Add(new VariableSlot(literal));
                    continue;
                }

                Func<Task<ICorDebugValue?>> readValueAsync = async () => isStatic
                    ? await debugger.FuncEval.GetStaticFieldValueAsync(type, field, debugger.GetILFrame(reference.ThreadId, reference.FrameDepth))
                    : value.UnwrapDebugValueToObject().GetFieldValue(corClass, field);
                if (browsable == DebuggerBrowsableState.RootHidden) {
                    await AddRootHiddenMemberAsync(name, readValueAsync, reference, result, evaluateName, VariableKind.Data, visibility);
                    continue;
                }
                result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, (await readValueAsync())!, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Data, visibility)));
            }
            catch (Exception ex) {
                // A member that cannot even be listed is shown with the error as its value, like one that cannot be read
                result.Add(new VariableSlot(VariableInfo.CreateError(fieldProps.szField, ex.Message)));
            }
        }
    }
    private async Task AddPropertiesAsync(List<PropertyToken> properties, IMetaDataImport metadataImport, ICorDebugType type, ICorDebugValue value, VariableReference reference, List<VariableSlot> result) {
        var module = type.GetClass().GetModule();
        foreach (var property in properties) {
            // No getter runs here, a property only costs a func eval once the page holding it is requested
            var propertyProps = metadataImport.GetPropertyProps(property);
            var name = propertyProps.szProperty;
            if (name == null || propertyProps.pmdGetter.IsNil)
                continue;
            try {
                var browsable = GetDebuggerBrowsableState(metadataImport, property);
                if (browsable == DebuggerBrowsableState.Never)
                    continue;

                var getterAttributes = metadataImport.GetMethodProps(propertyProps.pmdGetter).pdwAttr;
                var isStatic = getterAttributes.IsMdStatic();
                var visibility = GetVisibility(getterAttributes);
                var evaluateName = GetMemberEvaluateName(name, isStatic, reference.EvaluateName, type);

                // The getter is invoked with the original reference value, not the dereferenced object, and with the
                // arguments of the type declaring it - the members of a base type are listed while walking up from the
                // value, and a non-generic type deriving from a generic base has no arguments of its own to invoke them with
                Func<Task<ICorDebugValue?>> invokeGetterAsync = () => {
                    var getter = module.GetFunctionFromToken(propertyProps.pmdGetter);
                    var eval = debugger.GetThread(reference.ThreadId).CreateEval();
                    ICorDebugValue[] arguments = isStatic ? [] : [value];
                    return debugger.FuncEval.CallFunctionAsync(eval, getter, type.GetTypeParameters(), arguments);
                };
                if (browsable == DebuggerBrowsableState.RootHidden) {
                    await AddRootHiddenMemberAsync(name, invokeGetterAsync, reference, result, evaluateName, VariableKind.Property, visibility);
                    continue;
                }
                result.Add(new VariableSlot(name, async () => {
                    var propertyValue = await invokeGetterAsync();
                    if (propertyValue == null)
                        return null;

                    var keepHandle = false;
                    try {
                        var variable = await CreateVariableAsync(name, propertyValue, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Property, visibility);
                        // A value with children stays alive behind its variables reference
                        keepHandle = variable.VariablesReference != 0;
                        return variable;
                    }
                    finally {
                        if (!keepHandle && propertyValue is ICorDebugHandleValue handle)
                            handle.TryDispose();
                    }
                }));
            }
            catch (Exception ex) {
                result.Add(new VariableSlot(VariableInfo.CreateError(name, ex.Message)));
            }
        }
    }
    // A 'RootHidden' member is replaced in the listing by its own elements, so unlike the other members it is read
    // while the listing is built - what it holds decides how many entries the listing has and how they are named
    private async Task AddRootHiddenMemberAsync(string name, Func<Task<ICorDebugValue?>> readValueAsync, VariableReference reference, List<VariableSlot> result, string? evaluateName, VariableKind kind, VariableVisibility? visibility) {
        try {
            var memberValue = await readValueAsync();
            if (memberValue == null)
                return;
            // The value is read now but its elements only once their page is requested, so it has to stay alive
            if (memberValue is ICorDebugHandleValue handle)
                variableManager.Keep(handle);

            if (memberValue.UnwrapDebugValue() is ICorDebugArrayValue) {
                AddArrayElementSlots(memberValue, reference, result, evaluateName);
                return;
            }
            result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, memberValue, reference.ThreadId, reference.FrameDepth, evaluateName, kind, visibility)));
        }
        catch (Exception ex) {
            result.Add(new VariableSlot(VariableInfo.CreateError(name, ex.Message)));
        }
    }
    // The node offering deferred enumeration of an IEnumerable value, its expansion runs the enumeration in the debuggee
    private VariableSlot CreateResultsViewNode(VariableReference reference) {
        var resultsReference = variableManager.Create(new VariableReference(VariableReferenceKind.ResultsView, reference.ThreadId, reference.FrameDepth, reference.Value, null, reference.EvaluateName));
        var node = new VariableInfo(ResultsViewGroup, ResultsViewMessage, string.Empty);
        node.Kind = VariableKind.ResultsView;
        node.VariablesReference = resultsReference;
        return new VariableSlot(node);
    }
    private async Task AddResultsViewItemsAsync(VariableReference reference, List<VariableSlot> result) {
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

            // The row VS shows through SystemCore_EnumerableDebugView's 'Empty' exception property - without
            // it an empty enumeration expands to nothing, which reads as a listing that never finished loading
            if (arrayValue.GetCount() == 0) {
                result.Add(new VariableSlot(new VariableInfo(ResultsViewEmptyName, ResultsViewEmptyMessage, "string")));
                return;
            }

            AddArrayElementSlots(evaluation.Value!, reference, result, GetResultsViewEvaluateName(reference, arrayValue, isGeneric));
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
    // Microsoft's debugger enumerates without loading it (Concord interprets the IL against metadata read from disk), clrdbg loads it
    private async Task EnsureSystemLinqLoadedAsync(EvaluationContext context) {
        if (debugger.Modules.Any(it => string.Equals(it.Name, "System.Linq.dll", StringComparison.OrdinalIgnoreCase)))
            return;
        using var loadResult = await debugger.GetEvaluator().EvaluateAsync("System.Reflection.Assembly.Load(\"System.Linq\")", context);
        if (loadResult.Error != null)
            throw new EvaluationException(loadResult.Error);
    }
    // The expression reported for enumerated items: 'new System.Linq.SystemCore_EnumerableDebugView<T>(value).Items'
    private static string? GetResultsViewEvaluateName(VariableReference reference, ICorDebugArrayValue arrayValue, bool isGeneric) {
        if (reference.EvaluateName == null)
            return null;
        if (!isGeneric)
            return $"new System.Linq.SystemCore_EnumerableDebugView({reference.EvaluateName}).Items";
        var elementTypeName = TypeNameFormatter.GetTypeName(arrayValue.GetExactType().GetFirstTypeParameter());
        return $"new System.Linq.SystemCore_EnumerableDebugView<{elementTypeName}>({reference.EvaluateName}).Items";
    }
    // The elements are named by their index alone, so only the ones of the requested page are ever read and formatted
    private void AddArrayElementSlots(ICorDebugValue arraySource, VariableReference reference, List<VariableSlot> result, string? parentEvaluateName) {
        var arrayValue = (ICorDebugArrayValue)arraySource.UnwrapDebugValue();
        if (arrayValue.GetRank() > 1)
            throw new NotImplementedException("Multidimensional arrays are not supported yet");

        var count = arrayValue.GetCount();
        for (var i = 0; i < count; i++) {
            var index = i;
            var name = $"[{index}]";
            var evaluateName = parentEvaluateName == null ? name : parentEvaluateName + name;
            result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, ReadArrayElement(arraySource, index), reference.ThreadId, reference.FrameDepth, evaluateName)));
        }
    }
    // An evaluation neuters a dereferenced array value, so every element read dereferences the source value again -
    // the source itself (a local's reference, a kept handle) stays valid while the debuggee is stopped
    private static ICorDebugValue ReadArrayElement(ICorDebugValue arraySource, int index) {
        var arrayValue = (ICorDebugArrayValue)arraySource.UnwrapDebugValue();
        return arrayValue.GetElement(1, [checked((uint)index)]);
    }

    private async Task<ICorDebugValue> CreateDebuggerProxyAsync(ICorDebugValue value, string proxyTypeName, int threadId) {
        var valueType = value.GetExactType();
        var valueModule = valueType.GetClass().GetModule();
        var parsedName = SerializedTypeName.Parse(proxyTypeName);
        var (module, proxyTypeDef) = FindLoadedTypeDef(parsedName.FullName, valueModule)
            ?? throw new InvalidOperationException($"The debugger proxy type '{proxyTypeName}' was not found");
        // TODO: select the constructor by signature, proxy types may have several
        var constructorDef = module.GetMetaDataInterface<IMetaDataImport>().FindMethod(proxyTypeDef, ".ctor", 0, 0);
        var constructor = module.GetFunctionFromToken(constructorDef);

        // An open generic proxy ('ICollectionDebugView`1' on List<T>) is closed over the value's own type
        // arguments, a closed one ('CollectionDebuggerProxy`1[...Match]' on MatchCollection) names them itself
        var typeArguments = parsedName.TypeArguments.Count == 0
            ? valueType.GetTypeParameters()
            : parsedName.TypeArguments.Select(it => ResolveSerializedType(it, valueModule)).ToArray();

        var eval = debugger.GetThread(threadId).CreateEval();
        var proxy = await debugger.FuncEval.NewObjectAsync(eval, constructor, typeArguments, [value]);
        return proxy ?? throw new InvalidOperationException($"The debugger proxy '{proxyTypeName}' could not be created");
    }
    private ICorDebugType ResolveSerializedType(SerializedTypeName typeName, ICorDebugModule preferredModule) {
        var (module, typeDef) = FindLoadedTypeDef(typeName.FullName, preferredModule)
            ?? throw new InvalidOperationException($"The type '{typeName.FullName}' was not found in the loaded modules");
        var typeArguments = typeName.TypeArguments.Select(it => ResolveSerializedType(it, module)).ToArray();
        var elementType = IsValueTypeDef(module.GetMetaDataInterface<IMetaDataImport>(), typeDef) ? CorElementType.VALUETYPE : CorElementType.CLASS;
        return ((ICorDebugClass2)module.GetClassFromToken(typeDef)).GetParameterizedType(elementType, typeArguments.Length, typeArguments);
    }
    // The serialized name is looked up without its assembly qualifier, so a type living elsewhere (e.g. in the
    // core library) is searched for across the loaded modules, starting from the module the search prefers
    private (ICorDebugModule, TypeDefToken)? FindLoadedTypeDef(string fullName, ICorDebugModule preferredModule) {
        var typeDef = preferredModule.GetMetaDataInterface<IMetaDataImport>().FindNestedTypeDef(fullName);
        if (typeDef != null)
            return (preferredModule, typeDef.Value);
        foreach (var moduleInfo in debugger.Modules) {
            typeDef = moduleInfo.Module.GetMetaDataInterface<IMetaDataImport>().FindNestedTypeDef(fullName);
            if (typeDef != null)
                return (moduleInfo.Module, typeDef.Value);
        }
        return null;
    }
    private static bool IsValueTypeDef(IMetaDataImport metadataImport, TypeDefToken typeDef) {
        var extends = metadataImport.GetTypeDefProps(typeDef).ptkExtends;
        if (extends.IsNil)
            return false;
        string baseTypeName;
        if (extends.Type == CorTokenType.mdtTypeDef)
            baseTypeName = metadataImport.GetTypeDefProps(new TypeDefToken(extends.Value)).szTypeDef;
        else if (extends.Type == CorTokenType.mdtTypeRef)
            baseTypeName = metadataImport.GetTypeRefProps(new TypeRefToken(extends.Value)).szName;
        else
            return false;
        return baseTypeName == "System.ValueType" || baseTypeName == "System.Enum";
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
    private static VariableSlot CreateGroup(string name, int variablesReference) {
        var group = new VariableInfo(name, string.Empty, string.Empty);
        group.Kind = VariableKind.Group;
        group.VariablesReference = variablesReference;
        return new VariableSlot(group);
    }
    // Ordinal order ('AAA AAB ... aaa aab') with the groups at the end; the elements a 'RootHidden' member was
    // replaced with compare by their index, so '[2]' stays before '[10]'
    private static void SortMembers(List<VariableSlot> members) {
        members.Sort((left, right) => {
            var rankComparison = GetSortRank(left).CompareTo(GetSortRank(right));
            if (rankComparison != 0)
                return rankComparison;
            if (TryGetElementIndex(left.Name, out var leftIndex) && TryGetElementIndex(right.Name, out var rightIndex))
                return leftIndex.CompareTo(rightIndex);
            return string.CompareOrdinal(left.Name, right.Name);
        });
    }
    private static bool TryGetElementIndex(string name, out uint index) {
        index = 0;
        return name.StartsWith('[') && name.EndsWith(']') && uint.TryParse(name.AsSpan(1, name.Length - 2), out index);
    }
    private static int GetSortRank(VariableSlot member) {
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
