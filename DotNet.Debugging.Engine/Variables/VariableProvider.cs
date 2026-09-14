using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Evaluation;

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
    // The name the DebuggerDisplay attribute's 'Name' gives a member shown with this value, null without one
    public string? Name { get; }
    public ICorDebugValue? ProxyValue { get; }

    public ValueDisplay(string typeName, string value, string? name, ICorDebugValue? proxyValue) {
        TypeName = typeName;
        Value = value;
        Name = name;
        ProxyValue = proxyValue;
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
    private const string ResultsViewEmptyMessage = "Enumeration yielded no results";
    // The enumeration of a Results View: the generic form infers the element type, the other lists objects
    private const string GenericEnumeration = "System.Linq.Enumerable.ToArray({0})";
    private const string NonGenericEnumeration = "System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Cast<object>({0}))";
    // The implicit evaluations (ToString, DebuggerDisplay) of one variables request share this time budget, counted
    // from the start of the page, so a single slow override cannot stall a whole listing
    private const int ImplicitEvalBudgetMilliseconds = 2000;
    // A display fragment showing a value with a display of its own renders it in turn; a display that comes back to
    // its own type (a linked node showing its successor) stops there with the type name
    private const int MaxDisplayDepth = 4;

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

        // A slot stands for one entry or for a block of them (the elements of an array), the page is cut out of the entries
        var totalCount = slots.Sum(it => it.Count);
        var pageStart = Math.Clamp(start, 0, totalCount);
        var pageEnd = Math.Clamp(start + count, pageStart, totalCount);
        var variables = new List<VariableInfo>(pageEnd - pageStart);
        limitImplicitEvals = true;
        implicitEvalTime.Restart();
        try {
            var position = 0;
            foreach (var slot in slots) {
                if (position >= pageEnd)
                    break;
                var slotEnd = position + slot.Count;
                for (var i = Math.Max(position, pageStart); i < Math.Min(slotEnd, pageEnd); i++) {
                    var variable = await slot.MaterializeAsync(i - position);
                    if (variable != null)
                        variables.Add(variable);
                }
                position = slotEnd;
            }
        }
        finally {
            limitImplicitEvals = false;
        }
        return new VariablePage(variables, totalCount);
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
                await AddMembersAsync(reference.Value!, reference.Value!.UnwrapDebugValueToObject().GetExactType(), MemberFilter.NonPublic, listStatics: false, reference, result);
                SortMembers(result);
                break;
            case VariableReferenceKind.StaticMembers:
                await AddMembersAsync(reference.Value!, reference.Value!.UnwrapDebugValueToObject().GetExactType(), MemberFilter.All, listStatics: true, reference, result);
                SortMembers(result);
                break;
        }
    }
    // A variable, a field or an element is written in place, a property through its setter
    public async Task<VariableInfo> SetVariableAsync(int referenceId, string name, string text) {
        var reference = variableManager.Get(referenceId) ?? throw new InvalidOperationException("The variables reference was not found");
        var target = FindVariableValue(reference, name);
        if (target == null)
            return await SetPropertyAsync(reference, name, text);
        VariableWriter.Write(target, text);

        // An element name ('[0]') appends to the parent expression without a dot
        string evaluateName;
        if (reference.EvaluateName == null)
            evaluateName = name;
        else if (name.StartsWith('['))
            evaluateName = reference.EvaluateName + name;
        else
            evaluateName = $"{reference.EvaluateName}.{name}";
        return await CreateVariableAsync(name, target, reference.ThreadId, reference.FrameDepth, evaluateName, useDisplayName: reference.Kind != VariableReferenceKind.Scope);
    }
    // Assigns a property the way an expression does, evaluating 'parent.Property = text' in the frame: the setter runs
    // in the debuggee and the text is any expression the frame evaluates
    private async Task<VariableInfo> SetPropertyAsync(VariableReference reference, string name, string text) {
        ListedProperty? property = null;
        string? declaringTypeName = null;
        if (reference.Value != null)
            property = FindListedProperty(reference, name, out declaringTypeName);
        if (property == null)
            throw new InvalidOperationException($"Variable '{name}' not found or setting its value is not supported");

        var evaluateName = property.GetEvaluateName(reference.EvaluateName, declaringTypeName);
        var context = new EvaluationContext(debugger.GetThread(reference.ThreadId), reference.ThreadId, reference.FrameDepth);
        using var evaluation = await debugger.GetEvaluator().EvaluateAsync($"{evaluateName} = {text}", context);
        if (evaluation.Error != null)
            throw new EvaluationException(evaluation.Error);

        var variable = await CreateVariableAsync(name, evaluation.Value!, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Property, property.Visibility, useDisplayName: true);
        // A value with children stays alive behind its variables reference
        if (variable.VariablesReference != 0)
            evaluation.KeepHandle();
        return variable;
    }
    // The property the listing shows under 'name' on the value's type or a base type, with the name of the type
    // declaring it: the members are named and hidden by the rules of the listing (a hidden one goes by
    // 'Name (Namespace.Type)'), so the name the client sends back finds the property it saw
    private ListedProperty? FindListedProperty(VariableReference reference, string name, out string? declaringTypeName) {
        declaringTypeName = null;
        var listStatics = reference.Kind == VariableReferenceKind.StaticMembers;
        var seenNames = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (var type = reference.Value!.UnwrapDebugValueToObject().GetExactType(); type != null && !type.IsRootType(); type = type.GetBaseType()) {
            var corClass = type.GetClass();
            var typeToken = corClass.GetToken();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var hasSymbols = debugger.FindModule(corClass.GetModule())?.HasSymbols == true;
            // The fields take part in the name hiding, whichever members this lookup is after
            ListFields(metadataImport, typeToken, listStatics, seenNames, hasSymbols, out _);
            var properties = ListProperties(metadataImport, typeToken, listStatics, seenNames, hasSymbols, out _);
            // The listing holds no property of a byref-like type
            if (metadataImport.HasAttribute(typeToken, AttributeNames.IsByRefLike))
                continue;

            var typeName = TypeNameFormatter.TryGetTypeName(type);
            var property = properties.FirstOrDefault(it => it.GetDisplayName(typeName) == name);
            if (property != null) {
                declaringTypeName = typeName;
                return property;
            }
        }
        return null;
    }
    // 'useDisplayName': the 'Name' of the value's DebuggerDisplay attribute replaces the name of a member or an element
    // (a dictionary's '[key]' entries), never that of a variable of the scope or of an evaluated expression
    public async Task<VariableInfo> CreateVariableAsync(string name, ICorDebugValue value, int threadId, int frameDepth, string? evaluateName, VariableKind kind = VariableKind.Data, VariableVisibility? visibility = null, bool useDisplayName = false) {
        var display = await FormatValueAsync(value, threadId, frameDepth, escapeStrings: true);
        var variable = new VariableInfo(useDisplayName && display.Name != null ? display.Name : name, display.Value, display.TypeName);
        variable.Kind = kind;
        variable.Visibility = visibility;
        variable.EvaluateName = evaluateName;
        variable.VariablesReference = CreateChildrenReference(value, display.TypeName, threadId, frameDepth, display.ProxyValue, evaluateName);
        return variable;
    }
    // Formats a value, rendering its DebuggerDisplay and creating its DebuggerTypeProxy in the debuggee when it has them.
    // A caller that only shows the text (a logpoint, an exception property) skips the proxy, which is a func eval.
    // 'depth' counts the displays nested in one another, a fragment showing a value with a display of its own
    public async Task<ValueDisplay> FormatValueAsync(ICorDebugValue value, int threadId, int frameDepth, bool escapeStrings, bool createProxy = true, int depth = 0) {
        var formatted = ValueFormatter.Format(value, escapeStrings);
        var text = formatted.Value;
        var typeName = formatted.TypeName;
        string? name = null;
        // A nullable's template and proxy belong to its underlying value
        var displayValue = value;
        if (formatted.TypeName.EndsWith('?'))
            displayValue = value.UnwrapDebugValueToObject().GetNullableValue() ?? value;
        if (formatted.RequiresDebuggerDisplay) {
            if (IsImplicitEvalBudgetSpent || depth >= MaxDisplayDepth) {
                // The budget ran out (or the displays nest too deep), the value falls back to the display it would have without the override
                text = $"{{{formatted.TypeName}}}";
            }
            else {
                text = await RenderDisplayAsync(formatted.Value, displayValue, threadId, frameDepth, depth);
                if (formatted.TypeTemplate != null)
                    typeName = await RenderDisplayAsync(formatted.TypeTemplate, displayValue, threadId, frameDepth, depth);
                if (formatted.NameTemplate != null)
                    name = await RenderDisplayAsync(formatted.NameTemplate, displayValue, threadId, frameDepth, depth);
            }
        }

        ICorDebugValue? proxyValue = null;
        if (createProxy && formatted.DebuggerProxyTypeName != null)
            proxyValue = await CreateDebuggerProxyAsync(displayValue, formatted.DebuggerProxyTypeName, threadId);
        return new ValueDisplay(typeName, text, name, proxyValue);
    }
    private bool IsImplicitEvalBudgetSpent => limitImplicitEvals && implicitEvalTime.ElapsedMilliseconds >= ImplicitEvalBudgetMilliseconds;

    // Every '{expression}' is evaluated in the value's type context and shown the way a variable holding its result is
    // (a string quoted unless ',nq', a nested value through its own DebuggerDisplay or ToString), the text between the
    // fragments stays; a fragment that fails shows its failure in its own place
    private async Task<string> RenderDisplayAsync(string template, ICorDebugValue value, int threadId, int frameDepth, int depth) {
        var result = new StringBuilder();
        foreach (var part in DebuggerDisplayTemplate.Parse(template)) {
            if (part.IsExpression)
                result.Append(await RenderFragmentAsync(part, value, threadId, frameDepth, depth));
            else
                result.Append(part.Text);
        }
        return result.ToString();
    }
    private async Task<string> RenderFragmentAsync(DebuggerDisplayPart fragment, ICorDebugValue value, int threadId, int frameDepth, int depth) {
        var context = new EvaluationContext(debugger.GetThread(threadId), threadId, frameDepth, value);
        using var evaluation = await debugger.GetEvaluator().EvaluateAsync(fragment.Text, context);
        if (evaluation.Error != null) {
            DebuggerLoggingService.LogMessage($"DebuggerDisplay fragment '{fragment.Text}' failed: {evaluation.Error}");
            return evaluation.Error;
        }
        if (evaluation.Value == null)
            return string.Empty;
        return (await FormatValueAsync(evaluation.Value, threadId, frameDepth, escapeStrings: !fragment.NoQuotes, createProxy: false, depth + 1)).Value;
    }

    private async Task AddScopeVariablesAsync(VariableReference reference, List<VariableSlot> result) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var function = frame.GetFunction();
        var module = debugger.GetModule(function.GetModule());

        AddCurrentException(reference, result);
        var hoistedLocalsContainer = AddArguments(frame, module, function, reference, result);
        // Locals captured by a lambda or hoisted into an async state machine live on the generated class,
        // the locals declared inside the lambda body itself are still plain IL locals
        if (hoistedLocalsContainer != null)
            await AddClosureMembersAsync(hoistedLocalsContainer, reference, result);
        await AddLocalsAsync(module, function, reference, result);
    }
    private void AddCurrentException(VariableReference reference, List<VariableSlot> result) {
        var exception = debugger.GetCurrentException(reference.ThreadId);
        if (exception == null)
            return;
        result.Add(new VariableSlot("$exception", async () => await CreateVariableAsync("$exception", exception, reference.ThreadId, reference.FrameDepth, "$exception")));
    }
    // Returns the generated closure or state machine instance holding the hoisted locals, when the frame is a lambda or an async method
    private ICorDebugValue? AddArguments(ICorDebugILFrame frame, ModuleInfo module, ICorDebugFunction function, VariableReference reference, List<VariableSlot> result) {
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
            if (thisValue != null && (methodProps.szMethod == "MoveNext" || methodProps.szMethod.Contains(">b"))) {
                var containingTypeName = metadataImport.GetTypeDefProps(function.GetClass().GetToken()).szTypeDef;
                var containingTypeKind = GeneratedNames.GetKind(containingTypeName);
                if (containingTypeKind is GeneratedNameKind.StateMachineType or GeneratedNameKind.LambdaDisplayClass) {
                    // 'this' is the generated class, the user's 'this' is one of its fields (absent when the user's method is static)
                    hoistedLocalsContainer = thisValue;
                    thisValue = thisValue.GetHoistedThis();
                }
            }
            if (thisValue != null) {
                var capturedThis = thisValue;
                result.Add(new VariableSlot("this", async () => await CreateVariableAsync("this", capturedThis, reference.ThreadId, reference.FrameDepth, "this")));
            }
        }

        var skipCount = isStatic ? 0 : 1;
        for (var i = skipCount; i < arguments.Length; i++) {
            var name = metadataImport.FindParameterName(function.GetToken(), i - skipCount + 1);
            if (name == null)
                continue;
            result.Add(CreateFrameSlot(name, arguments[i], reference));
        }
        return hoistedLocalsContainer;
    }
    // A slot the runtime cannot read at this instruction (optimized away) is listed with that as its value
    private VariableSlot CreateFrameSlot(string name, ICorDebugValue? value, VariableReference reference) {
        if (value == null)
            return new VariableSlot(VariableInfo.CreateError(name, UnavailableLocation.Message));
        return new VariableSlot(name, async () => await CreateVariableAsync(name, value, reference.ThreadId, reference.FrameDepth, name));
    }
    private async Task AddLocalsAsync(ModuleInfo module, ICorDebugFunction function, VariableReference reference, List<VariableSlot> result) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var locals = frame.GetLocalVariables();
        if (locals.Length == 0)
            return;

        var names = module.MetadataReader.GetLocalVariableNames(function.GetToken(), frame.GetIP().pnOffset);
        for (var i = 0; i < locals.Length; i++) {
            // Compiler generated locals (e.g. a DefaultInterpolatedStringHandler) have no name
            if (!names.TryGetValue(i, out var name))
                continue;
            // The display class of a lambda declared here ('CS$<>8__locals0', which the compiler leaves visible for the
            // evaluator's sake) holds the captured locals: those are listed, the class itself is not. A captured parameter
            // is a field of the class and a slot of the frame: the method reads and writes the field from its first
            // statement on, the slot keeps the value it was entered with, so the field replaces the slot listed already
            if (GeneratedNames.GetKind(name) == GeneratedNameKind.DisplayClassLocalOrField) {
                if (locals[i] == null)
                    continue;
                var closureSlots = new List<VariableSlot>();
                await AddClosureMembersAsync(locals[i]!, reference, closureSlots);
                foreach (var closureSlot in closureSlots) {
                    var listedIndex = result.FindIndex(it => it.Name == closureSlot.Name);
                    if (listedIndex >= 0)
                        result[listedIndex] = closureSlot;
                    else
                        result.Add(closureSlot);
                }
                continue;
            }
            result.Add(CreateFrameSlot(name, locals[i], reference));
        }
    }
    // Lists the hoisted locals of a closure and of the closures enclosing it, linked through their '<>8__' fields
    private async Task AddClosureMembersAsync(ICorDebugValue closure, VariableReference reference, List<VariableSlot> result) {
        // A display class is created where its first captured variable comes into scope: until then the field is null
        if (closure.UnwrapDebugValue() is not ICorDebugObjectValue objectValue)
            return;
        await AddMembersAsync(closure, objectValue.GetExactType(), MemberFilter.All, listStatics: false, reference, result);

        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        foreach (var field in metadataImport.EnumFields(corClass.GetToken())) {
            if (GeneratedNames.GetKind(metadataImport.GetFieldProps(field).szField) != GeneratedNameKind.DisplayClassLocalOrField)
                continue;
            await AddClosureMembersAsync(objectValue.GetFieldValue(corClass, field), reference, result);
            break;
        }
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
            if (includeResultsView && type.IsEnumerableType())
                result.Add(CreateResultsViewNode(reference));
            SortMembers(result);
        }
        else {
            throw new InvalidOperationException("The value has no children");
        }
    }
    private async Task AddMembersAndGroupsAsync(ICorDebugValue value, ICorDebugType type, VariableReference reference, List<VariableSlot> result, bool includeNonPublicGroup) {
        var summary = await AddMembersAsync(value, type, MemberFilter.Public, listStatics: false, reference, result);
        if (summary.HasStaticMembers) {
            var staticReference = variableManager.Create(new VariableReference(VariableReferenceKind.StaticMembers, reference.ThreadId, reference.FrameDepth, value, null, reference.EvaluateName));
            result.Add(CreateGroup(StaticMembersGroup, staticReference));
        }
        if (includeNonPublicGroup && summary.HasNonPublicMembers) {
            var nonPublicReference = variableManager.Create(new VariableReference(VariableReferenceKind.NonPublicMembers, reference.ThreadId, reference.FrameDepth, value, null, reference.EvaluateName));
            result.Add(CreateGroup(NonPublicMembersGroup, nonPublicReference));
        }
    }
    // Lists the instance or static members declared by the type and its base types, reporting whether the
    // 'Static members' and 'Non-Public members' groups are needed. 'seenNames' carries the member names met on the
    // way up the hierarchy: a base member is listed under a name a derived member already took only when that
    // member hides it (a field, a 'new' or non-virtual property), and then with its declaring type - an override
    // is the base property itself, listed once
    private async Task<MemberSummary> AddMembersAsync(ICorDebugValue value, ICorDebugType type, MemberFilter filter, bool listStatics, VariableReference reference, List<VariableSlot> result, Dictionary<string, bool>? seenNames = null) {
        seenNames ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        var corClass = type.GetClass();
        var typeToken = corClass.GetToken();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        // Every member of the type takes part in the name hiding, whichever visibility this listing shows
        var hasSymbols = debugger.FindModule(corClass.GetModule())?.HasSymbols == true;
        var fields = ListFields(metadataImport, typeToken, listStatics, seenNames, hasSymbols, out var hasStaticFields);
        var properties = ListProperties(metadataImport, typeToken, listStatics, seenNames, hasSymbols, out var hasStaticProperties);

        var summary = new MemberSummary();
        summary.HasStaticMembers = !listStatics && (hasStaticFields || hasStaticProperties);
        summary.HasNonPublicMembers = filter == MemberFilter.Public && (fields.Any(it => !it.IsInline) || properties.Any(it => !it.IsInline));

        // The declaring type names the hidden and the static members, null when it cannot be formatted
        var typeName = TypeNameFormatter.TryGetTypeName(type);
        await AddFieldsAsync(FilterMembers(fields, filter), metadataImport, type, typeName, value, reference, result);
        // A property getter cannot be func-evaled with a byref-like 'this' (the debuggee can die on it), such a value lists its fields only
        if (!metadataImport.HasAttribute(typeToken, AttributeNames.IsByRefLike))
            await AddPropertiesAsync(FilterMembers(properties, filter), metadataImport, type, typeName, value, reference, result);

        var baseType = type.GetBaseType();
        if (baseType == null || baseType.IsRootType())
            return summary;
        var baseSummary = await AddMembersAsync(value, baseType, filter, listStatics, reference, result, seenNames);
        summary.HasStaticMembers |= baseSummary.HasStaticMembers;
        summary.HasNonPublicMembers |= baseSummary.HasNonPublicMembers;
        return summary;
    }
    // The fields a type shows, in declaration order: compiler generated ones stay out (hoisted locals come back under
    // their own name). The metadata of a field is read once here, the listing and the reads take everything from it.
    // 'hasStaticFields' reports the static fields of the type whichever ones this listing holds
    private static List<ListedField> ListFields(IMetaDataImport metadataImport, TypeDefToken typeToken, bool listStatics, Dictionary<string, bool> seenNames, bool hasSymbols, out bool hasStaticFields) {
        hasStaticFields = false;
        var result = new List<ListedField>();
        foreach (var field in metadataImport.EnumFields(typeToken)) {
            var fieldProps = metadataImport.GetFieldProps(field);
            var isStatic = fieldProps.pdwAttr.IsFdStatic();
            hasStaticFields |= isStatic;
            if (isStatic != listStatics || !TryGetDisplayName(fieldProps.szField, out var name))
                continue;
            // A field hides every base member of its name
            if (TryListMember(seenNames, name, hidesBaseMembers: true, out var isHidden))
                result.Add(new ListedField(field, name, fieldProps.pdwAttr, fieldProps.pdwCPlusTypeFlag, fieldProps.ppValue, fieldProps.pcchValue, IsListedInline(fieldProps.pdwAttr.ToVisibility(), hasSymbols), isHidden));
        }
        return result;
    }
    private static List<ListedProperty> ListProperties(IMetaDataImport metadataImport, TypeDefToken typeToken, bool listStatics, Dictionary<string, bool> seenNames, bool hasSymbols, out bool hasStaticProperties) {
        hasStaticProperties = false;
        var result = new List<ListedProperty>();
        foreach (var property in metadataImport.EnumProperties(typeToken)) {
            var propertyProps = metadataImport.GetPropertyProps(property);
            var getter = propertyProps.pmdGetter;
            if (getter.IsNil)
                continue;
            var getterProps = metadataImport.GetMethodProps(getter);
            var getterAttributes = getterProps.pdwAttr;
            var isStatic = getterAttributes.IsMdStatic();
            hasStaticProperties |= isStatic;
            // An indexer getter requires arguments, so the property cannot be evaluated as a member
            if (isStatic != listStatics || HasParameters(getterProps.ppvSigBlob))
                continue;

            var name = propertyProps.szProperty;
            // An override reuses the slot of the base property, it is that property. A 'new' one (its own slot,
            // virtual or not) hides the base one
            var hidesBaseMembers = !getterAttributes.IsMdVirtual() || getterAttributes.IsMdNewSlot();
            // An accessor that is private yet virtual is an explicit interface implementation (C# has no private
            // virtual members): listed under its interface-qualified name, behind 'Non-Public members' like any private member
            var isExplicitImplementation = getterAttributes.IsMdPrivate() && getterAttributes.IsMdVirtual();
            var isInline = IsListedInline(getterAttributes.ToVisibility(), hasSymbols);
            if (TryListMember(seenNames, name, hidesBaseMembers, out var isHidden))
                result.Add(new ListedProperty(property, getter, getterAttributes, name, isInline, isHidden, isExplicitImplementation));
        }
        return result;
    }
    // A method signature blob starts with the calling convention, then the parameter count (a getter is never generic)
    private static bool HasParameters(nint signatureBlob) {
        return Marshal.ReadByte(signatureBlob, 1) != 0;
    }
    // Whether a member is listed, from what the walk up the hierarchy met of its name before: a name a more
    // derived member took is listed again only when that member hides it. Listed or not, what this member does
    // to the members of its name beneath it is recorded for the base types that follow
    private static bool TryListMember(Dictionary<string, bool> seenNames, string name, bool hidesBaseMembers, out bool isHidden) {
        isHidden = seenNames.TryGetValue(name, out var hiddenByDerived);
        seenNames[name] = hidesBaseMembers;
        return !isHidden || hiddenByDerived;
    }
    // Whether a member is listed with the public ones rather than behind 'Non-Public members'. The internal members
    // of a module with symbols (the user's own code) are, the listing reads better with them inline; those of a
    // module without symbols (a library's internals) stay behind the group
    private static bool IsListedInline(VariableVisibility visibility, bool hasSymbols) {
        return visibility == VariableVisibility.Public || (hasSymbols && visibility == VariableVisibility.Internal);
    }
    private static List<TMember> FilterMembers<TMember>(List<TMember> members, MemberFilter filter) where TMember : ListedMember {
        if (filter == MemberFilter.All)
            return members;
        return members.Where(it => it.IsInline == (filter == MemberFilter.Public)).ToList();
    }
    private async Task AddFieldsAsync(List<ListedField> fields, IMetaDataImport metadataImport, ICorDebugType type, string? typeName, ICorDebugValue value, VariableReference reference, List<VariableSlot> result) {
        var corClass = type.GetClass();
        foreach (var member in fields) {
            var field = member.Token;
            var name = member.GetDisplayName(typeName);
            try {
                // Which fields the listing holds and how they are named comes from metadata alone, no value is read here
                var browsable = metadataImport.GetDebuggerBrowsableState(field);
                if (browsable == DebuggerBrowsableState.Never)
                    continue;

                var isStatic = member.IsStatic;
                var visibility = member.Visibility;
                var evaluateName = member.GetEvaluateName(reference.EvaluateName, typeName);
                if (member.IsLiteral) {
                    var literal = new VariableInfo(name, ValueFormatter.FormatLiteral(member.LiteralValue, member.LiteralLength, member.LiteralType), TypeNameFormatter.GetPrimitiveTypeName(member.LiteralType));
                    literal.Visibility = visibility;
                    literal.EvaluateName = evaluateName;
                    result.Add(new VariableSlot(literal));
                    continue;
                }

                Func<Task<ICorDebugValue?>> readValueAsync = async () => isStatic
                    ? await debugger.FuncEval.GetStaticFieldValueAsync(type, field, () => debugger.GetILFrame(reference.ThreadId, reference.FrameDepth))
                    : value.UnwrapDebugValueToObject().GetFieldValue(corClass, field);
                if (browsable == DebuggerBrowsableState.RootHidden) {
                    await AddRootHiddenMemberAsync(name, readValueAsync, reference, result, evaluateName, VariableKind.Data, visibility);
                    continue;
                }
                result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, (await readValueAsync())!, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Data, visibility, useDisplayName: true)));
            }
            catch (Exception ex) {
                // A member that cannot even be listed is shown with the error as its value, like one that cannot be read
                result.Add(new VariableSlot(VariableInfo.CreateError(name, ex.Message)));
            }
        }
    }
    private async Task AddPropertiesAsync(List<ListedProperty> properties, IMetaDataImport metadataImport, ICorDebugType type, string? typeName, ICorDebugValue value, VariableReference reference, List<VariableSlot> result) {
        var module = type.GetClass().GetModule();
        foreach (var member in properties) {
            var name = member.GetDisplayName(typeName);
            try {
                // No getter runs here, a property only costs a func eval once the page holding it is requested
                var browsable = metadataImport.GetDebuggerBrowsableState(member.Token);
                if (browsable == DebuggerBrowsableState.Never)
                    continue;

                var isStatic = member.IsStatic;
                var visibility = member.Visibility;
                var evaluateName = member.GetEvaluateName(reference.EvaluateName, typeName);

                // The getter is invoked with the original reference value, not the dereferenced object, and with the
                // arguments of the type declaring it - the members of a base type are listed while walking up from the
                // value, and a non-generic type deriving from a generic base has no arguments of its own to invoke them with
                Func<Task<ICorDebugValue?>> invokeGetterAsync = () => {
                    var getter = module.GetFunctionFromToken(member.Getter);
                    var eval = debugger.GetThread(reference.ThreadId).CreateEval();
                    ICorDebugValue[] arguments = isStatic ? [] : [value];
                    return debugger.FuncEval.CallFunctionAsync(eval, getter, type.GetTypeParameters(), arguments, throwOnException: true);
                };
                if (browsable == DebuggerBrowsableState.RootHidden) {
                    await AddRootHiddenMemberAsync(name, invokeGetterAsync, reference, result, evaluateName, VariableKind.Property, visibility);
                    continue;
                }
                result.Add(new VariableSlot(name, async () => {
                    ICorDebugValue? propertyValue;
                    try {
                        propertyValue = await invokeGetterAsync();
                    }
                    catch (EvaluationThrewException ex) {
                        // A getter that throws is a failed read, not the exception as the value
                        if (ex.ExceptionValue is ICorDebugHandleValue thrownHandle)
                            thrownHandle.TryDispose();
                        return VariableInfo.CreateError(name, ex.Message);
                    }
                    if (propertyValue == null)
                        return null;

                    var keepHandle = false;
                    try {
                        var variable = await CreateVariableAsync(name, propertyValue, reference.ThreadId, reference.FrameDepth, evaluateName, VariableKind.Property, visibility, useDisplayName: true);
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
            result.Add(new VariableSlot(name, async () => await CreateVariableAsync(name, memberValue, reference.ThreadId, reference.FrameDepth, evaluateName, kind, visibility, useDisplayName: true)));
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
        var enumeration = GenericEnumeration;
        var evaluation = await debugger.GetEvaluator().EvaluateAsync(string.Format(enumeration, "this"), context);
        if (evaluation.Error != null) {
            // The value only implements the non generic IEnumerable, so the element type cannot be inferred
            evaluation.Dispose();
            enumeration = NonGenericEnumeration;
            evaluation = await debugger.GetEvaluator().EvaluateAsync(string.Format(enumeration, "this"), context);
        }
        try {
            if (evaluation.Error != null)
                throw new EvaluationException(evaluation.Error);
            if (evaluation.Value!.UnwrapDebugValue() is not ICorDebugArrayValue arrayValue)
                throw new InvalidOperationException("The enumeration did not produce an array");

            // Without a row an empty enumeration expands to nothing, which reads as a listing that never finished loading
            if (arrayValue.GetCount() == 0) {
                result.Add(new VariableSlot(new VariableInfo(ResultsViewEmptyName, ResultsViewEmptyMessage, string.Empty)));
                return;
            }

            // The items are addressed through the enumeration that produced them
            var parentEvaluateName = reference.EvaluateName == null ? null : string.Format(enumeration, reference.EvaluateName);
            AddArrayElementSlots(evaluation.Value!, reference, result, parentEvaluateName);
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
    // The enumeration compiles against System.Linq, which the debuggee may not have loaded: it is loaded then (a module event follows)
    private async Task EnsureSystemLinqLoadedAsync(EvaluationContext context) {
        if (debugger.Modules.Any(it => string.Equals(it.Name, "System.Linq.dll", StringComparison.OrdinalIgnoreCase)))
            return;
        using var loadResult = await debugger.GetEvaluator().EvaluateAsync("System.Reflection.Assembly.Load(\"System.Linq\")", context);
        if (loadResult.Error != null)
            throw new EvaluationException(loadResult.Error);
    }
    // The elements are one block slot named by their index, so a listing costs nothing per element: only the ones
    // of the requested page are ever named, read and formatted
    private void AddArrayElementSlots(ICorDebugValue arraySource, VariableReference reference, List<VariableSlot> result, string? parentEvaluateName) {
        var arrayValue = (ICorDebugArrayValue)arraySource.UnwrapDebugValue();
        var count = arrayValue.GetCount();
        if (count == 0)
            return;
        var rank = arrayValue.GetRank();
        var dimensions = arrayValue.GetDimensions(rank);
        var baseIndices = arrayValue.HasBaseIndicies() ? arrayValue.GetBaseIndicies(rank) : new uint[rank];
        Func<int, string> getElementName = position => GetElementName(position, dimensions, baseIndices);
        result.Add(new VariableSlot(count, getElementName, async position => {
            var name = getElementName(position);
            var evaluateName = parentEvaluateName == null ? name : parentEvaluateName + name;
            return await CreateVariableAsync(name, ReadArrayElement(arraySource, position), reference.ThreadId, reference.FrameDepth, evaluateName, useDisplayName: true);
        }));
    }
    // The name of the element at a row major position: '[2]', or '[0, 1]' for a multidimensional array
    private static string GetElementName(int position, uint[] dimensions, uint[] baseIndices) {
        var indices = new long[dimensions.Length];
        var remainder = position;
        for (var dimension = dimensions.Length - 1; dimension >= 0; dimension--) {
            var length = checked((int)dimensions[dimension]);
            indices[dimension] = baseIndices[dimension] + (uint)(remainder % length);
            remainder /= length;
        }
        return $"[{string.Join(", ", indices)}]";
    }
    // An evaluation neuters a dereferenced array value, so every element read dereferences the source value again -
    // the source itself (a local's reference, a kept handle) stays valid while the debuggee is stopped
    private static ICorDebugValue ReadArrayElement(ICorDebugValue arraySource, int position) {
        var arrayValue = (ICorDebugArrayValue)arraySource.UnwrapDebugValue();
        return arrayValue.GetElementAtPosition(position);
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

        // An open generic proxy ('ICollectionDebugView`1' on List<T>) is closed over the type arguments of the type
        // declaring it - the first generic type up the chain, a List<T> subclass has none of its own - a closed one
        // ('CollectionDebuggerProxy`1[...Match]' on MatchCollection) names them itself
        ICorDebugType[] typeArguments;
        if (parsedName.TypeArguments.Count == 0) {
            var proxyOwner = valueType;
            while (proxyOwner.GetTypeParameters().Length == 0 && proxyOwner.GetBaseType() != null)
                proxyOwner = proxyOwner.GetBaseType()!;
            typeArguments = proxyOwner.GetTypeParameters();
        }
        else {
            typeArguments = parsedName.TypeArguments.Select(it => ResolveSerializedType(it, valueModule)).ToArray();
        }

        var eval = debugger.GetThread(threadId).CreateEval();
        var proxy = await debugger.FuncEval.NewObjectAsync(eval, constructor, typeArguments, [value]);
        return proxy ?? throw new InvalidOperationException($"The debugger proxy '{proxyTypeName}' could not be created");
    }
    private ICorDebugType ResolveSerializedType(SerializedTypeName typeName, ICorDebugModule preferredModule) {
        var (module, typeDef) = FindLoadedTypeDef(typeName.FullName, preferredModule)
            ?? throw new InvalidOperationException($"The type '{typeName.FullName}' was not found in the loaded modules");
        var typeArguments = typeName.TypeArguments.Select(it => ResolveSerializedType(it, module)).ToArray();
        var elementType = module.GetMetaDataInterface<IMetaDataImport>().IsValueType(typeDef) ? CorElementType.VALUETYPE : CorElementType.CLASS;
        return ((ICorDebugClass2)module.GetClassFromToken(typeDef)).GetParameterizedType(elementType, typeArguments);
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
            // A nullable has the children of its value, if any: they are listed from the value itself
            var underlyingValue = objectValue.GetNullableValue();
            if (underlyingValue is not ICorDebugObjectValue underlyingObject)
                return 0;
            objectValue = underlyingObject;
            value = underlyingValue;
        }

        var elementType = objectValue.GetElementType();
        // Strings, decimals and boxed primitives are displayed as primitives, an enum as its member name
        if (elementType == CorElementType.STRING || typeName == "decimal" || typeName == "decimal?" || TypeNameFormatter.IsPrimitiveTypeName(typeName) || objectValue.GetExactType().IsEnumType())
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
            var rank = arrayValue.GetRank();
            var parts = name.Substring(1, name.Length - 2).Split(',');
            if (parts.Length != rank)
                return null;

            // The element names show the logical indices, the runtime addresses by zero based offsets
            var baseIndices = arrayValue.HasBaseIndicies() ? arrayValue.GetBaseIndicies(rank) : new uint[rank];
            var indices = new uint[rank];
            for (var i = 0; i < rank; i++) {
                if (!long.TryParse(parts[i], out var index) || index < baseIndices[i])
                    return null;
                indices[i] = checked((uint)(index - baseIndices[i]));
            }
            return arrayValue.GetElement(indices);
        }
        if (unwrapped is ICorDebugObjectValue objectValue) {
            if (objectValue.IsLiteralField(name))
                throw new InvalidOperationException($"'{name}' is a constant and cannot be assigned");
            return objectValue.GetFieldValueByName(debugger.GetILFrame(reference.ThreadId, reference.FrameDepth), name);
        }
        return null;
    }
    private ICorDebugValue? FindFrameVariableValue(VariableReference reference, string name) {
        var frame = debugger.GetILFrame(reference.ThreadId, reference.FrameDepth);
        var function = frame.GetFunction();
        var module = debugger.GetModule(function.GetModule());
        var ilOffset = frame.GetIP().pnOffset;

        var locals = frame.GetLocalVariables();
        var names = module.MetadataReader.GetLocalVariableNames(function.GetToken(), ilOffset);
        for (var i = 0; i < locals.Length; i++) {
            if (names.GetValueOrDefault(i) == name)
                return locals[i];
        }

        // A hoisted local lives on the closure or state machine: the generated 'this' of a lambda or MoveNext, or the
        // display class local of the method declaring a lambda. It is looked up before the arguments: a captured
        // parameter is on the display class as well as in its frame slot, and only the display class copy is live
        var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var skipCount = metadataImport.GetMethodProps(function.GetToken()).pdwAttr.IsMdStatic() ? 0 : 1;
        var arguments = frame.GetArguments();
        var containers = new List<ICorDebugValue>();
        if (skipCount == 1 && arguments.Length > 0 && arguments[0] != null) {
            var containingTypeKind = GeneratedNames.GetKind(metadataImport.GetTypeDefProps(function.GetClass().GetToken()).szTypeDef);
            if (containingTypeKind is GeneratedNameKind.StateMachineType or GeneratedNameKind.LambdaDisplayClass)
                containers.Add(arguments[0]!);
        }
        for (var i = 0; i < locals.Length; i++) {
            if (locals[i] != null && names.TryGetValue(i, out var localName) && GeneratedNames.GetKind(localName) == GeneratedNameKind.DisplayClassLocalOrField)
                containers.Add(locals[i]!);
        }
        foreach (var container in containers) {
            var hoisted = FindHoistedVariableValue(container, name);
            if (hoisted != null)
                return hoisted;
        }

        for (var i = skipCount; i < arguments.Length; i++) {
            if (metadataImport.FindParameterName(function.GetToken(), i - skipCount + 1) == name)
                return arguments[i];
        }
        return null;
    }
    // The field named after the variable on the generated instance, or on an enclosing closure it links to
    private static ICorDebugValue? FindHoistedVariableValue(ICorDebugValue closure, string name) {
        if (closure.UnwrapDebugValue() is not ICorDebugObjectValue objectValue)
            return null;
        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        foreach (var field in metadataImport.EnumFields(corClass.GetToken())) {
            var fieldName = metadataImport.GetFieldProps(field).szField;
            if (TryGetDisplayName(fieldName, out var displayName) && displayName == name)
                return objectValue.GetFieldValue(corClass, field);
            if (GeneratedNames.GetKind(fieldName) == GeneratedNameKind.DisplayClassLocalOrField) {
                var enclosing = FindHoistedVariableValue(objectValue.GetFieldValue(corClass, field), name);
                if (enclosing != null)
                    return enclosing;
            }
        }
        return null;
    }

    // Compiler generated fields are hidden, except hoisted locals ('<count>5__1') which are shown under their original name
    private static bool TryGetDisplayName(string? fieldName, out string displayName) {
        displayName = fieldName ?? string.Empty;
        if (fieldName == null)
            return false;
        if (!GeneratedNames.TryParseGeneratedName(fieldName, out var kind, out var openBracketOffset, out var closeBracketOffset))
            return true;
        if (kind != GeneratedNameKind.HoistedLocalField)
            return false;
        displayName = fieldName.Substring(openBracketOffset + 1, closeBracketOffset - openBracketOffset - 1);
        return true;
    }
    private static VariableSlot CreateGroup(string name, int variablesReference) {
        var group = new VariableInfo(name, string.Empty, string.Empty);
        group.Kind = VariableKind.Group;
        group.VariablesReference = variablesReference;
        return new VariableSlot(group);
    }
    // Ordinal order ('AAA AAB ... aaa aab') with the groups at the end
    private static void SortMembers(List<VariableSlot> members) {
        members.Sort((left, right) => {
            var rankComparison = GetSortRank(left).CompareTo(GetSortRank(right));
            if (rankComparison != 0)
                return rankComparison;
            return string.CompareOrdinal(left.Name, right.Name);
        });
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

    // A field or property the listing shows: whether it goes with the public members or behind 'Non-Public members',
    // and how it is named. One a derived member hides is told apart from it by its declaring type, 'Name (Namespace.Type)'
    private abstract class ListedMember {
        public string Name { get; }
        public bool IsInline { get; }
        public bool IsHidden { get; }
        public bool IsExplicitImplementation { get; }
        public abstract bool IsStatic { get; }
        public abstract VariableVisibility Visibility { get; }

        protected ListedMember(string name, bool isInline, bool isHidden, bool isExplicitImplementation) {
            Name = name;
            IsInline = isInline;
            IsHidden = isHidden;
            IsExplicitImplementation = isExplicitImplementation;
        }

        public string GetDisplayName(string? declaringTypeName) {
            return IsHidden && declaringTypeName != null ? $"{Name} ({declaringTypeName})" : Name;
        }
        // 'parent.Member' for an instance member, 'Namespace.Type.Member' for a static one, the bare name for a
        // hoisted local. A hidden member is only reachable through a cast to its declaring type, an explicit
        // interface implementation ('Namespace.Interface.Member') through one to its interface: '((Namespace.Type)parent).Member'
        public string GetEvaluateName(string? parentEvaluateName, string? declaringTypeName) {
            if (IsStatic && declaringTypeName != null)
                return $"{declaringTypeName}.{Name}";
            if (parentEvaluateName == null)
                return Name;
            if (IsExplicitImplementation && Name.LastIndexOf('.') is var dotIndex && dotIndex > 0)
                return $"(({Name.Substring(0, dotIndex)}){parentEvaluateName}).{Name.Substring(dotIndex + 1)}";
            if (IsHidden && declaringTypeName != null)
                return $"(({declaringTypeName}){parentEvaluateName}).{Name}";
            return $"{parentEvaluateName}.{Name}";
        }
    }
    private class ListedField : ListedMember {
        public FieldDefToken Token { get; }
        public CorFieldAttr Attributes { get; }
        // The constant of a literal field: the element type, the pointer to the value and its length (for strings)
        public CorElementType LiteralType { get; }
        public nint LiteralValue { get; }
        public int LiteralLength { get; }
        public override bool IsStatic => Attributes.IsFdStatic();
        public override VariableVisibility Visibility => Attributes.ToVisibility();
        public bool IsLiteral => Attributes.IsFdLiteral();

        public ListedField(FieldDefToken token, string name, CorFieldAttr attributes, CorElementType literalType, nint literalValue, int literalLength, bool isInline, bool isHidden)
            : base(name, isInline, isHidden, isExplicitImplementation: false) {
            Token = token;
            Attributes = attributes;
            LiteralType = literalType;
            LiteralValue = literalValue;
            LiteralLength = literalLength;
        }
    }
    private class ListedProperty : ListedMember {
        public PropertyToken Token { get; }
        public MethodDefToken Getter { get; }
        public CorMethodAttr GetterAttributes { get; }
        public override bool IsStatic => GetterAttributes.IsMdStatic();
        public override VariableVisibility Visibility => GetterAttributes.ToVisibility();

        public ListedProperty(PropertyToken token, MethodDefToken getter, CorMethodAttr getterAttributes, string name, bool isInline, bool isHidden, bool isExplicitImplementation)
            : base(name, isInline, isHidden, isExplicitImplementation) {
            Token = token;
            Getter = getter;
            GetterAttributes = getterAttributes;
        }
    }
}
