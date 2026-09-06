using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// Executes the CIL of a compiled expression. Primitive arithmetic runs on the host, everything touching the
// debuggee (field reads, calls, allocations) goes through ICorDebug and func evals
internal class CilInterpreter {
    private readonly ManagedDebugger debugger;
    private readonly PrimitiveTypeClasses primitiveTypes;

    public CilInterpreter(ManagedDebugger debugger, PrimitiveTypeClasses primitiveTypes) {
        this.debugger = debugger;
        this.primitiveTypes = primitiveTypes;
    }

    public async Task<EvaluationResult> InterpretAsync(CompiledExpression compiled, EvaluationContext context) {
        using var handles = new EvaluationHandleScope();
        var body = compiled.GetMethodBody(compiled.EntryMethod);
        var decoded = compiled.GetDecodedMethod(compiled.EntryMethod);
        var frame = context.RootValue == null ? debugger.GetILFrame(context.ThreadId, context.FrameDepth) : null;
        var arguments = CreateArguments(frame, context);
        var locals = CreateLocals(compiled, frame, body.LocalSignature, context, context.RootValue != null);

        ICorDebugType[] typeGenericArguments;
        ICorDebugType[] methodGenericArguments;
        if (context.RootValue != null) {
            typeGenericArguments = context.RootValue.GetExactType().GetTypeParameters();
            methodGenericArguments = [];
        }
        else {
            SplitFrameTypeParameters(frame!, out typeGenericArguments, out methodGenericArguments);
        }

        // Tokens are resolved preferring the module the expression was compiled against (the frame's module, or the
        // root value's module for DebuggerDisplay), so runtime resolution binds to the same assembly instance Roslyn did
        var preferredModule = context.RootValue != null ? context.RootValue.GetExactType().GetClass().GetModule() : frame!.GetFunction().GetModule();
        var resolver = new EvaluationMetadataResolver(debugger, compiled, context.Thread.GetAppDomain(), typeGenericArguments, methodGenericArguments, debugger.GetModule(preferredModule));
        var state = new EvaluationState();
        var result = await InterpretAsync(compiled, decoded, arguments, locals, resolver, context, handles, state);
        var value = await MaterializeAsync(result, context, handles, resolver.ResolveMethodReturnType(compiled.EntryMethod), resolver);
        return EvaluationResult.FromValue(value, handles.Detach(value));
    }

    private ICilLocation[] CreateArguments(ICorDebugILFrame? frame, EvaluationContext context) {
        if (context.RootValue != null)
            return [new CorDebugLocation(context.RootValue)];
        var arguments = frame!.GetArguments();
        var result = new ICilLocation[arguments.Length];
        for (var i = 0; i < result.Length; i++) {
            var index = i;
            result[i] = arguments[i] == null ? new UnavailableLocation() : new CorDebugLocation(() => GetFrame(context).GetArguments()[index]!);
        }
        return result;
    }
    // The evaluation method's locals start with the frame's locals (so the expression can read and assign them), the rest are temporaries
    private ICilLocation[] CreateLocals(CompiledExpression compiled, ICorDebugILFrame? frame, StandaloneSignatureHandle localSignature, EvaluationContext context, bool isTypeContext) {
        var localCount = localSignature.IsNil
            ? 0
            : compiled.MetadataReader.GetStandaloneSignature(localSignature).DecodeLocalSignature(LocalCountSignatureProvider.Instance, genericContext: null).Length;
        var frameLocals = frame?.GetLocalVariables();
        var result = new ICilLocation[localCount];
        for (var i = 0; i < result.Length; i++) {
            var index = i;
            if (isTypeContext || i >= frameLocals!.Length)
                result[i] = new TemporaryLocation(CilValue.Null());
            else if (frameLocals[i] == null)
                result[i] = new UnavailableLocation();
            else
                result[i] = new CorDebugLocation(() => GetFrame(context).GetLocalVariables()[index]!);
        }
        return result;
    }
    // A frame slot is fetched from the frame on every access, the frame anew each time: the frame does not survive
    // a func eval, and the value object of a value-typed slot is a snapshot - an instance call on the slot (a struct
    // constructor, a mutating method) changes the debuggee's memory behind it, which only a fresh fetch shows
    private ICorDebugILFrame GetFrame(EvaluationContext context) {
        return debugger.GetILFrame(context.ThreadId, context.FrameDepth);
    }
    private static ICilLocation[] CreateTemporaryLocals(EvaluationMetadataResolver resolver, StandaloneSignatureHandle localSignature) {
        var count = resolver.GetEvaluationLocalCount(localSignature);
        var result = new ICilLocation[count];
        for (var i = 0; i < count; i++)
            result[i] = new TemporaryLocation(CilValue.Null());
        return result;
    }
    // A frame's type parameters are the declaring type's followed by the method's own
    private void SplitFrameTypeParameters(ICorDebugILFrame frame, out ICorDebugType[] typeArguments, out ICorDebugType[] methodArguments) {
        ICorDebugType[] typeParameters;
        try {
            typeParameters = frame.GetTypeParameters();
        }
        catch {
            typeArguments = [];
            methodArguments = [];
            return;
        }
        var declaringTypeArity = GetDeclaringTypeArity(frame);
        typeArguments = typeParameters.Take(declaringTypeArity).ToArray();
        methodArguments = typeParameters.Skip(declaringTypeArity).ToArray();
    }
    private int GetDeclaringTypeArity(ICorDebugILFrame frame) {
        try {
            var function = frame.GetFunction();
            var declaringTypeToken = function.GetClass().GetToken();
            var moduleInfo = debugger.GetModule(function.GetModule());
            return moduleInfo.MetadataReader.PeMetadataReader
                .GetTypeDefinition((TypeDefinitionHandle)MetadataTokens.Handle(declaringTypeToken))
                .GetGenericParameters().Count;
        }
        catch {
            return 0;
        }
    }

    private async Task<CilValue> InterpretAsync(
        CompiledExpression compiled,
        DecodedMethod decoded,
        ICilLocation[] arguments,
        ICilLocation[] locals,
        EvaluationMetadataResolver resolver,
        EvaluationContext context,
        EvaluationHandleScope handles,
        EvaluationState state) {
        var instructions = decoded.Instructions;
        var stack = new Stack<CilValue>();
        var index = 0;
        ResolvedCilType? constrainedType = null;
        while (index < instructions.Count) {
            var instruction = instructions[index++];
            try {
                var op = instruction.OpCode;
                if (op == OpCodes.Nop || op == OpCodes.Break)
                    continue;
                if (op == OpCodes.Constrained) {
                    constrainedType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    continue;
                }
                if (op == OpCodes.Ret)
                    return stack.Count == 0 ? CilValue.Null() : stack.Pop();

                if (op.TryGetConstant(instruction.Operand, out var constant)) {
                    stack.Push(constant);
                    continue;
                }
                if (op == OpCodes.Ldstr) {
                    stack.Push(CilValue.FromPrimitive(resolver.ResolveUserString((int)instruction.Operand!)));
                    continue;
                }
                if (op == OpCodes.Ldtoken) {
                    var tokenHandle = MetadataTokens.EntityHandle((int)instruction.Operand!);
                    // A field token is an array initializer's data, handed on to RuntimeHelpers.InitializeArray
                    if (tokenHandle.Kind == HandleKind.FieldDefinition)
                        stack.Push(CilValue.FromHostValue((FieldDefinitionHandle)tokenHandle));
                    else
                        stack.Push(CilValue.FromPrimitive(resolver.ResolveTypeToken((int)instruction.Operand!)));
                    continue;
                }
                if (op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn) {
                    // The delegate constructor that follows gets its own copy of the receiver
                    if (op == OpCodes.Ldvirtftn)
                        stack.Pop();
                    stack.Push(CilValue.FromHostValue(ResolveFunction((int)instruction.Operand!, resolver)));
                    continue;
                }

                if (op.TryGetSlotIndex(instruction.Operand, OpCodes.Ldarg_0, OpCodes.Ldarg, OpCodes.Ldarg_S, out var argumentIndex)) {
                    stack.Push(handles.Root(arguments[argumentIndex].Read()));
                    continue;
                }
                if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S) {
                    stack.Push(CilValue.FromLocation(arguments[(int)instruction.Operand!]));
                    continue;
                }
                if (op == OpCodes.Starg || op == OpCodes.Starg_S) {
                    var argument = arguments[(int)instruction.Operand!];
                    argument.Write(await MaterializeForLocationAsync(argument, stack.Pop(), resolver, context, handles));
                    continue;
                }
                if (op.TryGetSlotIndex(instruction.Operand, OpCodes.Ldloc_0, OpCodes.Ldloc, OpCodes.Ldloc_S, out var localIndex)) {
                    stack.Push(handles.Root(locals[localIndex].Read()));
                    continue;
                }
                if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S) {
                    stack.Push(CilValue.FromLocation(locals[(int)instruction.Operand!]));
                    continue;
                }
                if (op.TryGetSlotIndex(instruction.Operand, OpCodes.Stloc_0, OpCodes.Stloc, OpCodes.Stloc_S, out localIndex)) {
                    var local = locals[localIndex];
                    local.Write(await MaterializeForLocationAsync(local, stack.Pop(), resolver, context, handles));
                    continue;
                }

                if (op == OpCodes.Dup) {
                    stack.Push(stack.Peek());
                    continue;
                }
                if (op == OpCodes.Pop) {
                    stack.Pop();
                    continue;
                }
                if (op == OpCodes.Neg) {
                    stack.Push(stack.Pop().Negate());
                    continue;
                }
                if (op == OpCodes.Not) {
                    stack.Push(CilValue.FromPrimitive(~stack.Pop().AsInt64()));
                    continue;
                }
                if (op.IsBinaryOperation()) {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(op.EvaluateBinary(left, right));
                    continue;
                }
                if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un || op == OpCodes.Clt || op == OpCodes.Clt_Un) {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(CilValue.FromPrimitive(op.Compare(left, right) ? 1 : 0));
                    continue;
                }
                if (op.IsConversion()) {
                    stack.Push(op.Convert(stack.Pop()));
                    continue;
                }

                if (op == OpCodes.Br || op == OpCodes.Br_S) {
                    index = decoded.Offsets[(int)instruction.Operand!];
                    continue;
                }
                if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S) {
                    var condition = stack.Pop().IsTrue();
                    var branchOnTrue = op == OpCodes.Brtrue || op == OpCodes.Brtrue_S;
                    if (branchOnTrue == condition)
                        index = decoded.Offsets[(int)instruction.Operand!];
                    continue;
                }
                if (op.IsComparisonBranch()) {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    if (op.EvaluateBranch(left, right))
                        index = decoded.Offsets[(int)instruction.Operand!];
                    continue;
                }
                if (op == OpCodes.Switch) {
                    var selected = stack.Pop().AsInt32();
                    var targets = (int[])instruction.Operand!;
                    if ((uint)selected < (uint)targets.Length)
                        index = decoded.Offsets[targets[selected]];
                    continue;
                }

                if (op == OpCodes.Ldobj || op.IsPrefixed("ldind.")) {
                    stack.Push(handles.Root(stack.Pop().Dereference()));
                    continue;
                }
                if (op == OpCodes.Stobj || op.IsPrefixed("stind.")) {
                    var value = stack.Pop();
                    var address = stack.Pop().Location ?? throw new InvalidOperationException("stind requires a managed location");
                    address.Write(await MaterializeForStoreAsync(value, resolver, context, handles));
                    continue;
                }
                if (op == OpCodes.Cpobj) {
                    var source = stack.Pop().Dereference();
                    var destination = stack.Pop().Location ?? throw new InvalidOperationException("cpobj requires a managed location");
                    destination.Write(await MaterializeForStoreAsync(source, resolver, context, handles));
                    continue;
                }
                if (op == OpCodes.Initobj) {
                    var type = resolver.ResolveTypeToken((int)instruction.Operand!);
                    var location = stack.Pop().Location ?? throw new InvalidOperationException("initobj requires a managed location");
                    location.Write(await CreateDefaultValueAsync(type, resolver, context, handles));
                    continue;
                }

                if (op == OpCodes.Newarr) {
                    var length = checked((uint)stack.Pop().AsInt64());
                    var elementCilType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    var array = await CreateArrayAsync(elementCilType, resolver.GetCorDebugType(elementCilType), length, resolver, context, handles);
                    stack.Push(array == null ? CilValue.Null() : CilValue.FromCorValue(array));
                    continue;
                }
                if (op == OpCodes.Ldlen) {
                    stack.Push(CilValue.FromPrimitive(stack.Pop().GetArrayValue().GetCount()));
                    continue;
                }
                if (op == OpCodes.Ldelema) {
                    var elementIndex = stack.Pop();
                    var array = stack.Pop().GetArrayValue();
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(GetElement(array, elementIndex))));
                    continue;
                }
                if (op == OpCodes.Ldelem || op.IsPrefixed("ldelem.")) {
                    var elementIndex = stack.Pop();
                    var array = stack.Pop().GetArrayValue();
                    stack.Push(handles.Root(new CorDebugLocation(GetElement(array, elementIndex)).Read()));
                    continue;
                }
                if (op == OpCodes.Stelem || op.IsPrefixed("stelem.")) {
                    var element = stack.Pop();
                    var elementIndex = stack.Pop();
                    var array = stack.Pop().GetArrayValue();
                    var location = new CorDebugLocation(GetElement(array, elementIndex));
                    location.Write(await MaterializeForLocationAsync(location, element, resolver, context, handles));
                    continue;
                }

                if (op == OpCodes.Isinst || op == OpCodes.Castclass) {
                    var source = stack.Pop();
                    if (source.IsNull) {
                        stack.Push(CilValue.Null());
                        continue;
                    }
                    var targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    if (await IsInstanceOfTypeAsync(source, targetType, resolver, context, handles))
                        stack.Push(source);
                    else if (op == OpCodes.Isinst)
                        stack.Push(CilValue.Null());
                    else
                        throw new InvalidCastException($"InvalidCastException: Cannot cast the debuggee value to '{GetTypeDisplayName(targetType)}'");
                    continue;
                }
                if (op == OpCodes.Box) {
                    var targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    var source = stack.Pop();
                    // A Nullable<T> boxes to its value, or to null when it has none - the runtime never boxes the Nullable itself
                    if (resolver.TryGetNullableUnderlyingType(targetType, out var underlyingType)) {
                        var nullableValue = source.DereferenceLocation().CorValue?.UnwrapDebugValueToObject().GetNullableValue();
                        if (nullableValue == null)
                            stack.Push(CilValue.Null());
                        else
                            stack.Push(await BoxAsync(CilValue.FromCorValue(nullableValue), resolver.GetCorDebugType(underlyingType), context, handles));
                        continue;
                    }
                    stack.Push(await BoxAsync(source, resolver.GetCorDebugType(targetType), context, handles));
                    continue;
                }
                if (op == OpCodes.Unbox_Any) {
                    var source = stack.Pop();
                    if (source.Location != null)
                        source = source.Dereference();
                    // A host primitive or an unboxed value type is already what the unbox would produce - a
                    // synthetic variable read (GetObjectByAlias) hands back the plain value rather than a boxed object
                    var isHostValue = source.Value != null && source.Value is not ResolvedCilType;
                    if (isHostValue || (source.CorValue != null && source.CorValue is not ICorDebugReferenceValue)) {
                        stack.Push(source);
                        continue;
                    }
                    var targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    if (resolver.TryGetNullableUnderlyingType(targetType, out var underlyingType)) {
                        stack.Push(await UnboxToNullableAsync(source, targetType, underlyingType, resolver, context, handles));
                        continue;
                    }
                    var boxed = source.GetBoxedValue();
                    if (!IsUnboxCompatible(boxed.GetObject(), targetType))
                        throw new InvalidCastException($"InvalidCastException: Cannot unbox the debuggee value to '{GetTypeDisplayName(targetType)}'");
                    // The box's object is a VALUETYPE to the runtime: a primitive is read into a host value for the arithmetic
                    stack.Push(CilValue.FromCorValue(boxed.GetObject()).UnboxPrimitive());
                    continue;
                }
                if (op == OpCodes.Unbox) {
                    var boxed = stack.Pop().GetBoxedValue();
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(boxed.GetObject())));
                    continue;
                }

                if ((op == OpCodes.Ldfld || op == OpCodes.Ldflda || op == OpCodes.Stfld) && resolver.TryResolveEvaluationField((int)instruction.Operand!, out var hostField, out _)) {
                    // A field of a host object: a captured variable, an anonymous type's member
                    var stored = op == OpCodes.Stfld ? stack.Pop() : null;
                    var location = stack.Pop().GetHostObject().GetField(hostField);
                    if (op == OpCodes.Ldfld)
                        stack.Push(location.Read());
                    else if (op == OpCodes.Ldflda)
                        stack.Push(CilValue.FromLocation(location));
                    else
                        location.Write(await MaterializeForStoreAsync(stored!, resolver, context, handles));
                    continue;
                }
                if ((op == OpCodes.Ldsfld || op == OpCodes.Ldsflda || op == OpCodes.Stsfld) && resolver.TryResolveEvaluationField((int)instruction.Operand!, out var hostStaticField, out var hostStaticType)) {
                    // A static of one of the expression assembly's types: a closure class's cached instance and delegates
                    await EnsureTypeInitializedAsync(hostStaticType, compiled, resolver, context, handles, state);
                    var location = state.GetStaticField(hostStaticField);
                    if (op == OpCodes.Ldsfld)
                        stack.Push(location.Read());
                    else if (op == OpCodes.Ldsflda)
                        stack.Push(CilValue.FromLocation(location));
                    else
                        location.Write(await MaterializeForStoreAsync(stack.Pop(), resolver, context, handles));
                    continue;
                }
                if (op == OpCodes.Ldfld) {
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    var receiver = stack.Pop().GetFieldReceiver();
                    stack.Push(handles.Root(CilValue.FromCorValue(receiver.GetFieldValue(field.DeclaringType.Class, field.Token))));
                    continue;
                }
                if (op == OpCodes.Ldflda) {
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    var receiver = stack.Pop().GetFieldReceiver();
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(receiver.GetFieldValue(field.DeclaringType.Class, field.Token))));
                    continue;
                }
                if (op == OpCodes.Stfld) {
                    var value = stack.Pop();
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    var receiver = stack.Pop().GetFieldReceiver();
                    var fieldLocation = new CorDebugLocation(receiver.GetFieldValue(field.DeclaringType.Class, field.Token));
                    fieldLocation.Write(await MaterializeForLocationAsync(fieldLocation, value, resolver, context, handles));
                    continue;
                }
                if (op == OpCodes.Ldsfld) {
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    stack.Push(handles.Root(CilValue.FromCorValue(await GetStaticFieldValueAsync(field, resolver, context))));
                    continue;
                }
                if (op == OpCodes.Ldsflda) {
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(await GetStaticFieldValueAsync(field, resolver, context))));
                    continue;
                }
                if (op == OpCodes.Stsfld) {
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    var staticLocation = new CorDebugLocation(await GetStaticFieldValueAsync(field, resolver, context));
                    staticLocation.Write(await MaterializeForLocationAsync(staticLocation, stack.Pop(), resolver, context, handles));
                    continue;
                }

                if (op == OpCodes.Newobj) {
                    stack.Push(await NewObjectAsync((int)instruction.Operand!, stack, compiled, resolver, context, handles, state));
                    continue;
                }
                if (op == OpCodes.Call || op == OpCodes.Callvirt) {
                    var callConstrainedType = constrainedType;
                    constrainedType = null;
                    var token = (int)instruction.Operand!;
                    if (resolver.TryResolveDebuggerIntrinsic(token, out var intrinsicName)) {
                        await ExecuteDebuggerIntrinsicAsync(intrinsicName, stack, state, resolver, context, handles);
                        continue;
                    }
                    if (resolver.TryResolveArrayMethod(token, out var arrayMethodName, out var arrayIndexCount)) {
                        await ExecuteArrayMethodAsync(arrayMethodName, arrayIndexCount, stack, resolver, context, handles);
                        continue;
                    }
                    if (resolver.TryResolveEvaluationMethod(token, out var evaluationMethod)) {
                        var methodResult = await InvokeEvaluationMethodAsync(compiled, evaluationMethod, stack.PopArguments(evaluationMethod.ArgumentCount), resolver, context, handles, state);
                        if (!evaluationMethod.ReturnsVoid)
                            stack.Push(methodResult);
                        continue;
                    }
                    if (await TryCallHostAsync(token, callConstrainedType, stack, compiled, resolver, context, handles, state))
                        continue;
                    await CallMethodAsync(token, callConstrainedType, stack, resolver, context, handles);
                    continue;
                }

                throw new NotSupportedException($"CIL opcode '{op.Name}' at IL_{instruction.Offset:X4} is not supported yet");
            }
            catch (Exception ex) when (ex is not NotSupportedException and not EvaluationException) {
                throw new InvalidOperationException($"CIL execution failed at IL_{instruction.Offset:X4} ({instruction.OpCode.Name}): {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException("The generated evaluation method ended without ret");
    }

    // The pseudo methods of an array type access an element by its index in every dimension, the
    // multidimensional counterpart of ldelem/stelem/ldelema
    private async Task ExecuteArrayMethodAsync(string methodName, int indexCount, Stack<CilValue> stack, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var element = methodName == "Set" ? stack.Pop() : null;
        var indices = new uint[indexCount];
        for (var i = indexCount - 1; i >= 0; i--)
            indices[i] = checked((uint)stack.Pop().AsInt32());
        var array = stack.Pop().GetArrayValue();

        if (methodName == "Set") {
            var elementLocation = new CorDebugLocation(array.GetElement(indices));
            elementLocation.Write(await MaterializeForLocationAsync(elementLocation, element!, resolver, context, handles));
            return;
        }
        var location = new CorDebugLocation(array.GetElement(indices));
        stack.Push(methodName == "Address" ? CilValue.FromLocation(location) : handles.Root(location.Read()));
    }
    private async Task<CilValue> NewObjectAsync(int token, Stack<CilValue> stack, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        // A type the expression assembly declares (a closure, a display class, an anonymous type) is instantiated on the host
        if (resolver.TryResolveEvaluationMethod(token, out var hostConstructor)) {
            var hostArguments = stack.PopArguments(hostConstructor.Signature.ParameterTypes.Length);
            var instance = CilValue.FromHostValue(new HostObject(hostConstructor.DeclaringType, hostConstructor.TypeArguments));
            await InvokeEvaluationMethodAsync(compiled, hostConstructor, [instance, .. hostArguments], resolver, context, handles, state);
            return instance;
        }
        if (resolver.TryResolveArrayConstructor(token, out var arrayType))
            return await CreateMultidimensionalArrayAsync(arrayType, stack, resolver, context, handles);

        var constructor = resolver.ResolveMethod(token);
        var constructorArguments = stack.PopArguments(constructor.Signature.ParameterTypes.Length);
        // A delegate over a function the expression named cannot exist in the debuggee (a lambda has no code there, a
        // method group no function pointer here): the interpreter invokes it itself
        if (constructorArguments.Length == 2 && constructorArguments[1].DereferenceLocation().Value is HostFunction function) {
            var target = constructorArguments[0].DereferenceLocation();
            return CilValue.FromHostValue(new HostDelegate(target.IsNull ? null : target, function));
        }
        // The runtime refuses to run a string constructor in a func eval, the common ones are built on the host
        if (constructor.DeclaringType.FullName == "System.String")
            return CreateString(constructor, constructorArguments);
        // A span is byref-like, the ones the expression builds live on the host
        if (SpanEmulator.IsSpanType(constructor.DeclaringType.FullName))
            return CreateSpanEmulator(resolver, context, handles).Create(constructor, constructorArguments);

        var byRefArguments = new List<ByRefArgument>();
        var argumentValues = await MaterializeArgumentsAsync(constructor, constructorArguments, receiverOffset: 0, resolver, context, handles, byRefArguments);

        var typeArguments = constructor.DeclaringType.TypeArguments.IsDefaultOrEmpty
            ? []
            : constructor.DeclaringType.TypeArguments.Select(resolver.GetCorDebugType).ToArray();
        var eval = context.Thread.CreateEval();
        ICorDebugValue? newValue;
        try {
            newValue = handles.Track(await debugger.FuncEval.NewObjectAsync(eval, constructor.Function, typeArguments, argumentValues, throwOnException: true));
        }
        finally {
            WriteBackByRefArguments(byRefArguments, handles);
        }
        return newValue == null ? CilValue.Null() : CilValue.FromCorValue(newValue);
    }
    private async Task CallMethodAsync(int token, ResolvedCilType? constrainedType, Stack<CilValue> stack, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        await CallRuntimeMethodAsync(resolver.ResolveMethod(token), constrainedType, stack, resolver, context, handles);
    }
    // Calls a debuggee method with a receiver and arguments of the interpreter's own, the way the IL would; the result
    // is a null value for a void method
    private async Task<CilValue> CallRuntimeMethodAsync(ResolvedRuntimeMethod method, CilValue? receiver, CilValue[] arguments, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var callStack = new Stack<CilValue>();
        if (receiver != null)
            callStack.Push(receiver);
        foreach (var argument in arguments)
            callStack.Push(argument);
        await CallRuntimeMethodAsync(method, null, callStack, resolver, context, handles);
        return callStack.Count == 0 ? CilValue.Null() : callStack.Pop();
    }
    private async Task CallRuntimeMethodAsync(ResolvedRuntimeMethod method, ResolvedCilType? constrainedType, Stack<CilValue> stack, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var argumentValues = stack.PopArguments(method.Signature.ParameterTypes.Length);
        var receiverValue = method.IsStatic ? null : stack.Pop();

        if (method.DeclaringType.FullName == "System.Type" && method.Name == "GetTypeFromHandle") {
            var tokenType = argumentValues[0].Value as ResolvedCilType ?? throw new InvalidOperationException("GetTypeFromHandle requires a type token");
            var typeValue = await GetSystemTypeAsync(tokenType, resolver, context, handles);
            stack.Push(typeValue == null ? CilValue.Null() : CilValue.FromTypeToken(tokenType, typeValue));
            return;
        }
        if (await TryExecuteInterpolationCallAsync(method, receiverValue, argumentValues, stack, resolver, context, handles))
            return;
        if (receiverValue?.Value is StringBuilder || receiverValue?.Location?.Read().Value is StringBuilder || argumentValues.Any(it => it.Value is StringBuilder))
            throw new InvalidOperationException($"Unhandled interpolated-string call '{method.DeclaringType.FullName}.{method.Name}'");

        var byRefArguments = new List<ByRefArgument>();
        var callArguments = await MaterializeArgumentsAsync(method, argumentValues, method.IsStatic ? 0 : 1, resolver, context, handles, byRefArguments);
        if (receiverValue != null) {
            var receiver = receiverValue.DereferenceLocation();
            if (receiver.IsNull)
                throw new NullReferenceException();
            callArguments[0] = await MaterializeReceiverAsync(receiver, context, constrainedType, resolver, handles);
        }

        // The declaring type's arguments come from the receiver's exact type when there is one, walked up to the
        // type declaring the method (an inherited method carries them on the base type), the method's own from the method spec
        var declaringTypeArity = resolver.GetRuntimeTypeGenericArity(method.DeclaringType);
        ICorDebugType[] declaringTypeArguments;
        if (!method.IsStatic && declaringTypeArity > 0 && callArguments[0].GetExactType() is { } receiverType
                && receiverType.FindDeclaringType(method.DeclaringType) is { } declaringType)
            declaringTypeArguments = declaringType.GetTypeParameters().Take(declaringTypeArity).ToArray();
        else if (!method.DeclaringType.TypeArguments.IsDefaultOrEmpty)
            declaringTypeArguments = method.DeclaringType.TypeArguments.Select(resolver.GetCorDebugType).ToArray();
        else
            declaringTypeArguments = [];
        var methodTypeArguments = method.MethodTypeArguments.IsDefaultOrEmpty ? [] : method.MethodTypeArguments.Select(resolver.GetCorDebugType).ToArray();
        ICorDebugType[] typeArguments = [.. declaringTypeArguments, .. methodTypeArguments];

        var eval = context.Thread.CreateEval();
        ICorDebugValue? callResult;
        try {
            callResult = handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, method.Function, typeArguments, callArguments, throwOnException: true));
        }
        finally {
            WriteBackByRefArguments(byRefArguments, handles);
        }

        if (method.Signature.ReturnType == PrimitiveTypeCode.Void.ToString())
            return;
        if (callResult == null)
            stack.Push(CilValue.Null());
        else if (method.Signature.ReturnType.EndsWith('&'))
            stack.Push(CilValue.FromLocation(new CorDebugLocation(callResult)));
        else
            stack.Push(CilValue.FromCorValue(callResult));
    }
    private async Task ExecuteDebuggerIntrinsicAsync(string name, Stack<CilValue> stack, EvaluationState state, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        switch (name) {
            case "CreateVariable": {
                stack.Pop(); // custom type payload
                stack.Pop(); // custom type payload id
                var variableName = stack.Pop().Value as string ?? throw new InvalidOperationException("The synthetic variable name is unavailable");
                var variableType = stack.Pop().Value as ResolvedCilType ?? throw new InvalidOperationException("The synthetic variable type is unavailable");
                state.SyntheticVariables[variableName] = await CreateSyntheticVariableAsync(variableType, resolver, context, handles);
                return;
            }
            case "GetVariableAddress": {
                var variableName = stack.Pop().Value as string ?? throw new InvalidOperationException("The synthetic variable name is unavailable");
                if (!state.SyntheticVariables.TryGetValue(variableName, out var location))
                    throw new InvalidOperationException($"The synthetic variable '{variableName}' is unavailable");
                stack.Push(CilValue.FromLocation(location));
                return;
            }
            case "GetObjectByAlias": {
                var variableName = stack.Pop().Value as string ?? throw new InvalidOperationException("The synthetic variable name is unavailable");
                if (!state.SyntheticVariables.TryGetValue(variableName, out var location))
                    throw new InvalidOperationException($"The synthetic variable '{variableName}' is unavailable");
                stack.Push(handles.Root(location.Read()));
                return;
            }
            case "GetException": {
                var exception = debugger.GetCurrentException(context.ThreadId) ?? throw new InvalidOperationException("No current exception is available");
                stack.Push(handles.Root(CilValue.FromCorValue(exception)));
                return;
            }
            default:
                throw new NotSupportedException($"Debugger intrinsic '{name}' is not supported");
        }
    }

    // Converts an interpreter value into a debuggee value, the form every result must take
    private async Task<ICorDebugValue> MaterializeAsync(CilValue value, EvaluationContext context, EvaluationHandleScope handles, ResolvedCilType? expectedType = null, EvaluationMetadataResolver? resolver = null) {
        var expectedPrimitive = expectedType?.Primitive;
        if (value.CorValue != null && (expectedPrimitive == null || expectedPrimitive == PrimitiveTypeCode.String || expectedPrimitive == PrimitiveTypeCode.Object))
            return value.CorValue;
        // A sequence a System.Linq operator computed here becomes an array, the other host values have no debuggee form
        if (value.Value is HostSequence sequence && resolver != null)
            return (await MaterializeSequenceAsync(sequence, resolver, context, handles)).CorValue!;
        if (value.IsHostValue())
            throw HostValueCannotLeave(value);

        var eval = context.Thread.CreateEval();
        var expectedElementType = expectedPrimitive?.ToCorElementType();
        if (value.Value == null && expectedElementType != null && value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric) {
            var primitiveResult = (ICorDebugGenericValue)eval.CreateValue(expectedElementType.Value, null);
            var data = sourceGeneric.GetValueAsBytes();
            // An enum (or a single-field struct) of another size is widened through its integer
            if (data.Length != primitiveResult.GetSize())
                data = CilValueEncoding.GetBytes(value.AsInt64(), expectedElementType.Value, primitiveResult.GetSize());
            primitiveResult.SetValueFromBytes(data);
            return primitiveResult;
        }
        if (value.Value == null)
            return eval.CreateValue(CorElementType.CLASS, null);
        if (value.Value is string text)
            return handles.Track(await debugger.FuncEval.NewStringAsync(eval, text, throwOnException: true));
        // ICorDebugEval creates no native integers, a nint/nuint result is built as the struct it is in the debuggee
        if (expectedPrimitive is PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr && resolver != null) {
            var pointerType = resolver.GetCorDebugType(expectedType!);
            var pointerResult = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, pointerType.GetClass(), [], throwOnException: true))
                ?? throw new InvalidOperationException("Failed to create the evaluation result native integer");
            new CorDebugLocation(pointerResult.UnwrapDebugValue()).Write(value);
            return pointerResult;
        }
        if (expectedType?.RuntimeType != null && resolver != null) {
            var typedResult = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, expectedType.RuntimeType.Class, [], throwOnException: true))
                ?? throw new InvalidOperationException("Failed to create the evaluation result value type");
            new CorDebugLocation(typedResult.UnwrapDebugValue()).Write(value);
            return typedResult;
        }

        var elementType = expectedElementType ?? CilValueEncoding.GetElementType(value.Value);
        var result = (ICorDebugGenericValue)eval.CreateValue(elementType, null);
        result.SetValueFromBytes(CilValueEncoding.GetBytes(value.Value, elementType, result.GetSize()));
        return result;
    }
    private async Task<ICorDebugValue> MaterializeForCallAsync(CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        value = value.DereferenceLocation();
        if (value.CorValue != null)
            return value.CorValue;
        if (value.Value is HostSequence sequence)
            return (await MaterializeSequenceAsync(sequence, resolver, context, handles)).CorValue!;
        if (value.IsHostValue())
            throw HostValueCannotLeave(value);
        return await MaterializeAsync(value, context, handles);
    }
    // A store into a reference slot (an 'object' local, field or element) boxes a host primitive or a debuggee value
    // type first, the slot takes the reference; any other slot takes the value
    private async Task<CilValue> MaterializeForLocationAsync(ICilLocation location, CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        if (location is CorDebugLocation debuggeeLocation && debuggeeLocation.IsReferenceSlot)
            return await MaterializeForReferenceSlotAsync(value, resolver, context, handles);
        return await MaterializeForStoreAsync(value, resolver, context, handles);
    }
    // Host values without a debuggee representation yet (e.g. strings produced by ldstr) are created in the
    // debuggee first, as a debuggee location can only hold values backed by an ICorDebugValue
    private async Task<CilValue> MaterializeForStoreAsync(CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        value = value.DereferenceLocation();
        if (value.CorValue != null || value.IsNull)
            return value;
        if (value.Value is HostSequence sequence)
            return await MaterializeSequenceAsync(sequence, resolver, context, handles);
        if (value.Value is string text) {
            var eval = context.Thread.CreateEval();
            var materialized = handles.Track(await debugger.FuncEval.NewStringAsync(eval, text, throwOnException: true));
            return CilValue.FromDebuggeeValue(materialized);
        }
        return value;
    }
    // Materializes the call arguments after 'receiverOffset' reserved slots, honouring by-reference parameters
    private async Task<ICorDebugValue[]> MaterializeArgumentsAsync(ResolvedRuntimeMethod method, CilValue[] arguments, int receiverOffset, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, List<ByRefArgument> byRefArguments) {
        var result = new ICorDebugValue[arguments.Length + receiverOffset];
        for (var i = 0; i < arguments.Length; i++) {
            result[i + receiverOffset] = method.Signature.ParameterTypes[i].EndsWith('&')
                ? await MaterializeByRefArgumentAsync(arguments[i], resolver, context, handles, byRefArguments)
                : await MaterializeForCallAsync(arguments[i], resolver, context, handles);
        }
        return result;
    }
    private async Task<ICorDebugValue> MaterializeByRefArgumentAsync(CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, List<ByRefArgument> byRefArguments) {
        if (value.Location is CorDebugLocation location)
            return location.Value;
        if (value.Location is SyntheticVariableLocation synthetic)
            return synthetic.StorageValue;
        if (value.Location == null)
            throw new InvalidOperationException("A by-reference argument requires a managed location");

        // A host temporary is passed as a debuggee copy and written back after the call
        var materialized = await MaterializeForCallAsync(value.Location.Read(), resolver, context, handles);
        byRefArguments.Add(new ByRefArgument(value.Location, materialized));
        return materialized;
    }
    private static void WriteBackByRefArguments(List<ByRefArgument> byRefArguments, EvaluationHandleScope handles) {
        foreach (var argument in byRefArguments)
            argument.Location.Write(handles.Root(CilValue.FromCorValue(argument.Value)));
    }
    // Instance calls need a reference receiver: value types are boxed, honouring the 'constrained.' prefix
    private async Task<ICorDebugValue> MaterializeReceiverAsync(CilValue value, EvaluationContext context, ResolvedCilType? constrainedType, EvaluationMetadataResolver resolver, EvaluationHandleScope handles) {
        if (constrainedType != null && value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric) {
            var exactType = value.CorValue.GetExactType();
            var boxed = await BoxBytesAsync(exactType.GetClass(), exactType.GetTypeParameters(), sourceGeneric.GetValueAsBytes(), context, handles);
            return boxed;
        }
        if (value.CorValue != null)
            return value.CorValue;
        if (value.Value == null)
            return await MaterializeForCallAsync(value, resolver, context, handles);
        // A host constant called through 'constrained.' (an enum member's ToString) is that type's value, not its underlying integer's
        if (constrainedType != null && constrainedType.RuntimeType != null)
            return await BoxHostValueAsync(value.Value, resolver.GetCorDebugType(constrainedType), context, handles);

        var elementType = CilValueEncoding.GetElementType(value.Value);
        if (!primitiveTypes.TryGetClass(elementType, out var boxedClass))
            return await MaterializeForCallAsync(value, resolver, context, handles);
        return await BoxBytesAsync(boxedClass, [], CilValueEncoding.GetBytes(value.Value, elementType), context, handles);
    }
    private async Task<CilValue> BoxAsync(CilValue value, ICorDebugType targetType, EvaluationContext context, EvaluationHandleScope handles) {
        value = value.DereferenceLocation();
        // A host object is a reference already
        if (value.IsHostValue())
            return value;

        if (value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric)
            return CilValue.FromCorValue(await BoxBytesAsync(targetType.GetClass(), targetType.GetTypeParameters(), sourceGeneric.GetValueAsBytes(), context, handles));
        if (value.Value != null)
            return CilValue.FromCorValue(await BoxHostValueAsync(value.Value, targetType, context, handles));
        throw new InvalidOperationException("Cannot box a null value");
    }
    // A host value boxed as the target type, encoded to the box's own element type and size (an enum backed by a
    // byte takes one byte of the host's integer)
    private async Task<ICorDebugValue> BoxHostValueAsync(object hostValue, ICorDebugType targetType, EvaluationContext context, EvaluationHandleScope handles) {
        var eval = context.Thread.CreateEval();
        var boxed = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, targetType.GetClass(), targetType.GetTypeParameters(), throwOnException: true))
            ?? throw new InvalidOperationException("Failed to box the CIL value");
        var generic = (ICorDebugGenericValue)boxed.UnwrapDebugValue();
        // The box's object is a VALUETYPE to the runtime: a primitive's class says how its bytes are laid out (a double
        // is not the bits of an integer), an enum takes its underlying integer at the box's size
        var elementType = generic.GetElementType();
        if (elementType == CorElementType.VALUETYPE)
            elementType = targetType.GetPrimitiveElementType() ?? elementType;
        generic.SetValueFromBytes(CilValueEncoding.GetBytes(hostValue, elementType, generic.GetSize()));
        return boxed;
    }
    private async Task<ICorDebugValue> BoxBytesAsync(ICorDebugClass corClass, ICorDebugType[] typeArguments, byte[] data, EvaluationContext context, EvaluationHandleScope handles) {
        var eval = context.Thread.CreateEval();
        var boxed = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, corClass, typeArguments, throwOnException: true))
            ?? throw new InvalidOperationException("Failed to box the CIL value");
        ((ICorDebugGenericValue)boxed.UnwrapDebugValue()).SetValueFromBytes(data);
        return boxed;
    }

    private async Task<ICorDebugValue> GetStaticFieldValueAsync(ResolvedRuntimeField field, EvaluationMetadataResolver resolver, EvaluationContext context) {
        var type = resolver.GetCorDebugType(field.DeclaringType);
        return await debugger.FuncEval.GetStaticFieldValueAsync(type, field.Token, () => debugger.GetILFrame(context.ThreadId, context.FrameDepth));
    }
    // 'unbox.any Nullable<T>': null becomes an empty Nullable<T>, a boxed T one holding it (the runtime never boxes a Nullable itself)
    private async Task<CilValue> UnboxToNullableAsync(CilValue source, ResolvedCilType nullableType, ResolvedCilType underlyingType, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var nullable = await CreateDefaultValueAsync(nullableType, resolver, context, handles);
        if (source.IsNull)
            return nullable;

        var boxed = source.GetBoxedValue();
        if (!IsUnboxCompatible(boxed.GetObject(), underlyingType))
            throw new InvalidCastException($"InvalidCastException: Cannot unbox the debuggee value to a nullable of '{GetTypeDisplayName(underlyingType)}'");

        var nullableObject = nullable.CorValue!.UnwrapDebugValueToObject();
        var corClass = nullableObject.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var hasValueField = metadataImport.FindField(corClass.GetToken(), "hasValue", 0, 0);
        var valueField = metadataImport.FindField(corClass.GetToken(), "value", 0, 0);
        new CorDebugLocation(nullableObject.GetFieldValue(corClass, hasValueField)).Write(CilValue.FromPrimitive(true));
        new CorDebugLocation(nullableObject.GetFieldValue(corClass, valueField)).Write(CilValue.FromCorValue(boxed.GetObject()));
        return nullable;
    }
    private async Task<CilValue> CreateDefaultValueAsync(ResolvedCilType type, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var primitiveType = type.Primitive?.ToCorElementType();
        if (primitiveType != null) {
            return primitiveType switch {
                CorElementType.R4 => CilValue.FromPrimitive(0f),
                CorElementType.R8 => CilValue.FromPrimitive(0d),
                CorElementType.I8 => CilValue.FromPrimitive(0L),
                CorElementType.U8 => CilValue.FromPrimitive(0UL),
                _ => CilValue.FromPrimitive(0)
            };
        }
        if (type.IsReferenceType)
            return CilValue.Null();

        var runtimeType = type.RuntimeType ?? throw new NotSupportedException("Initializing this CIL type is not supported");
        var typeArguments = runtimeType.TypeArguments.IsDefaultOrEmpty ? [] : runtimeType.TypeArguments.Select(resolver.GetCorDebugType).ToArray();
        var eval = context.Thread.CreateEval();
        var value = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, runtimeType.Class, typeArguments, throwOnException: true))
            ?? throw new InvalidOperationException("Failed to create a default value type");
        return CilValue.FromCorValue(value);
    }
    private async Task<ICilLocation> CreateSyntheticVariableAsync(ResolvedCilType type, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var arrayReference = await CreateArrayAsync(type, resolver.GetCorDebugType(type), 1, resolver, context, handles)
            ?? throw new InvalidOperationException("Failed to allocate the synthetic variable storage");
        if (arrayReference.UnwrapDebugValue() is not ICorDebugArrayValue)
            throw new InvalidOperationException("Failed to allocate the synthetic variable storage");
        return new SyntheticVariableLocation(arrayReference);
    }
    private async Task<ICorDebugValue?> CreateArrayAsync(ResolvedCilType elementCilType, ICorDebugType elementType, uint length, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var eval = context.Thread.CreateEval();
        if (elementType.GetElementType() != CorElementType.VALUETYPE || primitiveTypes.IsPrimitiveClass(elementType.GetClass()))
            return handles.Track(await debugger.FuncEval.NewArrayAsync(eval, elementType, length, throwOnException: true));

        // ICorDebugEval::NewArray can only allocate arrays of primitive and reference types, for other value
        // types (e.g. DateTime) the debuggee throws, so those go through Array.CreateInstance
        var arrayType = await GetSystemTypeAsync(elementCilType, resolver, context, handles) ?? throw new InvalidOperationException("Failed to resolve the element type for the array allocation");
        var createInstance = resolver.ResolveRuntimeMethod("System", "Array", "CreateInstance", "System.Type", PrimitiveTypeCode.Int32.ToString());
        var lengthValue = await MaterializeAsync(CilValue.FromPrimitive(checked((int)length)), context, handles);
        return handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, createInstance.Function, [], [arrayType, lengthValue], throwOnException: true));
    }
    private async Task<bool> IsInstanceOfTypeAsync(CilValue value, ResolvedCilType targetType, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var typeValue = await GetSystemTypeAsync(targetType, resolver, context, handles) ?? throw new InvalidOperationException("Failed to resolve the target System.Type");
        var method = resolver.ResolveRuntimeMethod("System", "Type", "IsInstanceOfType", PrimitiveTypeCode.Object.ToString());
        var sourceValue = value.CorValue ?? throw new NotSupportedException("Runtime type checks require a debuggee value");
        var eval = context.Thread.CreateEval();
        var result = handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, method.Function, [], [typeValue, sourceValue], throwOnException: true));
        return result != null && CilValue.FromCorValue(result).IsTrue();
    }
    private async Task<ICorDebugValue?> GetSystemTypeAsync(ResolvedCilType type, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var getType = resolver.ResolveRuntimeMethod("System", "Type", "GetType", PrimitiveTypeCode.String.ToString());
        var eval = context.Thread.CreateEval();
        var typeName = handles.Track(await debugger.FuncEval.NewStringAsync(eval, resolver.GetAssemblyQualifiedTypeName(type), throwOnException: true));
        return handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, getType.Function, [], [typeName], throwOnException: true));
    }

    // The DefaultInterpolatedStringHandler calls interpolated strings are lowered to run on the host. Real
    // pointer/span arithmetic (Unsafe.*, MemoryMarshal, span-based String.Join) is not modeled and surfaces as an error
    private async Task<bool> TryExecuteInterpolationCallAsync(ResolvedRuntimeMethod method, CilValue? receiver, CilValue[] arguments, Stack<CilValue> stack, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        if (method.DeclaringType.FullName != "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")
            return false;

        var receiverLocation = receiver?.Location;
        if (method.Name == ".ctor") {
            receiverLocation?.Write(CilValue.FromPrimitive(new StringBuilder()));
            return true;
        }

        var builder = receiverLocation?.Read().Value as StringBuilder
            ?? receiver?.Value as StringBuilder
            ?? throw new InvalidOperationException("The interpolated string handler receiver is unavailable");
        switch (method.Name) {
            case "AppendLiteral":
                builder.Append(arguments[0].Value as string);
                return true;
            case "AppendFormatted":
                var value = CoerceInterpolatedValue(arguments[0], method);
                var alignment = arguments.Select(it => it.Value).OfType<int>().Skip(arguments[0].Value is int ? 1 : 0).FirstOrDefault();
                var format = arguments.Select(it => it.Value).OfType<string>().FirstOrDefault();
                var text = await FormatInterpolatedValueAsync(value, format, resolver, context, handles);
                if (alignment != 0)
                    text = alignment > 0 ? text.PadLeft(alignment) : text.PadRight(-alignment);
                builder.Append(text);
                return true;
            case "ToStringAndClear":
            case "ToString":
                stack.Push(CilValue.FromPrimitive(builder.ToString()));
                return true;
            default:
                return false;
        }
    }
    // A comparison or a narrowing conversion leaves an int on the stack, the handler's type argument says what it is
    private static CilValue CoerceInterpolatedValue(CilValue value, ResolvedRuntimeMethod method) {
        if (method.MethodTypeArguments.IsDefaultOrEmpty || value.Value is not int number)
            return value;
        if (method.MethodTypeArguments[0].Primitive == PrimitiveTypeCode.Boolean)
            return CilValue.FromPrimitive(number != 0);
        if (method.MethodTypeArguments[0].Primitive == PrimitiveTypeCode.Char)
            return CilValue.FromPrimitive((char)number);
        return value;
    }
    private async Task<string> FormatInterpolatedValueAsync(CilValue value, string? format, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        value = value.DereferenceLocation();
        if (value.IsNull)
            return string.Empty;
        if (value.IsHostValue())
            throw HostValueCannotLeave(value);
        if (value.Value is IFormattable formattable)
            return formattable.ToString(format, null);
        // A type token carries the System.Type in its debuggee value, the host part only names it to the interpreter
        if (value.Value != null && value.Value is not ResolvedCilType)
            return value.Value.ToString() ?? string.Empty;
        var stringText = value.GetStringText();
        if (stringText != null)
            return stringText;

        var receiver = value.CorValue ?? throw new InvalidOperationException("The interpolated value is unavailable");
        if (receiver.GetExactType().GetElementType() == CorElementType.VALUETYPE)
            receiver = (await BoxAsync(value, receiver.GetExactType(), context, handles)).CorValue!;

        // 'Object.ToString' is dispatched virtually by the func eval, unlike 'String.Concat(object)' it survives a trimmed core library
        var toString = resolver.ResolveRuntimeMethod("System", "Object", "ToString");
        var eval = context.Thread.CreateEval();
        var result = handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, toString.Function, [], [receiver], throwOnException: true));
        return result?.UnwrapDebugValue() is ICorDebugStringValue stringValue ? stringValue.GetString() : string.Empty;
    }

    // A method of the expression assembly (a lambda body, a local function, a closure's or anonymous type's member)
    // runs in the interpreter with temporary slots; an instance method of a host object binds the assembly's own
    // generic parameters to the object's instantiation
    private async Task<CilValue> InvokeEvaluationMethodAsync(CompiledExpression compiled, ResolvedEvaluationMethod method, CilValue[] arguments, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        var typeArguments = method.TypeArguments;
        if (!method.IsStatic && arguments[0].DereferenceLocation().Value is HostObject receiver && !receiver.TypeArguments.IsDefault)
            typeArguments = receiver.TypeArguments;
        using (resolver.EnterGenericContext(typeArguments, method.MethodTypeArguments)) {
            var locals = CreateTemporaryLocals(resolver, resolver.GetEvaluationMethodBody(method.Handle).LocalSignature);
            var slots = arguments.Select(it => (ICilLocation)new TemporaryLocation(it)).ToArray();
            return await InterpretAsync(compiled, compiled.GetDecodedMethod(method.Handle), slots, locals, resolver, context, handles, state);
        }
    }
    // Runs the static constructor of a type the expression assembly declares before its statics are first touched
    private async Task EnsureTypeInitializedAsync(TypeDefinitionHandle type, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        if (!state.InitializedTypes.Add(type))
            return;
        var initializer = resolver.FindTypeInitializer(type);
        if (initializer != null)
            await InvokeEvaluationMethodAsync(compiled, initializer, [], resolver, context, handles, state);
    }
    // The calls the interpreter serves itself rather than the debuggee: the Invoke of a delegate the expression
    // created, the base constructor call of a host object, the data copy of an array initializer, and the System.Linq
    // operators handed a lambda or a sequence computed here
    private async Task<bool> TryCallHostAsync(int token, ResolvedCilType? constrainedType, Stack<CilValue> stack, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        var method = resolver.ResolveMethod(token);
        var argumentCount = method.Signature.ParameterTypes.Length + (method.IsStatic ? 0 : 1);
        if (stack.Count < argumentCount)
            return false;
        // Peeked, receiver first; popped once the call turns out to be ours
        var arguments = stack.Take(argumentCount).Reverse().Select(it => it.DereferenceLocation()).ToArray();
        var typeName = method.DeclaringType.FullName;
        var returnsVoid = method.Signature.ReturnType == PrimitiveTypeCode.Void.ToString();

        if (!method.IsStatic && method.Name == "Invoke" && arguments[0].Value is HostDelegate) {
            stack.PopArguments(argumentCount);
            var result = await InvokeDelegateAsync(arguments[0], arguments.Skip(1).ToArray(), compiled, resolver, context, handles, state);
            if (!returnsVoid)
                stack.Push(result);
            return true;
        }
        if (!method.IsStatic && arguments[0].Value is HostObject) {
            stack.PopArguments(argumentCount);
            // The base constructor a host object's constructor calls has nothing to do
            if (method.Name == ".ctor" && typeName == "System.Object")
                return true;
            throw new NotSupportedException($"'{typeName}.{method.Name}' cannot be called on an object of a type the expression declares");
        }
        if (SpanEmulator.Handles(method, arguments)) {
            stack.PopArguments(argumentCount);
            var spanResult = await CreateSpanEmulator(resolver, context, handles).ExecuteAsync(method, constrainedType, arguments);
            if (!returnsVoid && spanResult != null)
                stack.Push(spanResult);
            return true;
        }
        if (typeName == "System.Runtime.CompilerServices.RuntimeHelpers" && method.Name == "InitializeArray" && arguments[1].Value is FieldDefinitionHandle dataField) {
            stack.PopArguments(argumentCount);
            InitializeArray(arguments[0], dataField, resolver);
            return true;
        }
        if (typeName == "System.Linq.Enumerable" && arguments.Any(it => it.IsHostValue())) {
            stack.PopArguments(argumentCount);
            var emulator = new LinqEmulator(
                (function, functionArguments) => InvokeDelegateAsync(function, functionArguments, compiled, resolver, context, handles, state),
                (source, elementType) => EnumerateAsync(source, elementType, resolver, context, handles),
                type => CreateDefaultValueAsync(type, resolver, context, handles),
                sequence => MaterializeSequenceAsync(sequence, resolver, context, handles),
                sequence => CreateListAsync(sequence, resolver, context, handles));
            stack.Push(await emulator.ExecuteAsync(method, arguments));
            return true;
        }
        return false;
    }
    private SpanEmulator CreateSpanEmulator(EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        return new SpanEmulator(
            sequence => MaterializeSequenceAsync(sequence, resolver, context, handles),
            type => type.Primitive?.ToCorElementType() ?? (type.RuntimeType == null ? null : EvaluationMetadataResolver.GetEnumUnderlyingElementType(type.RuntimeType)),
            resolver.GetEvaluationFieldData);
    }
    // Invokes a delegate for the interpreter's own purposes (an Invoke call, a System.Linq operator's lambda): one
    // the expression created runs here, one the debuggee holds runs there
    private async Task<CilValue> InvokeDelegateAsync(CilValue delegateValue, CilValue[] arguments, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        delegateValue = delegateValue.DereferenceLocation();
        if (delegateValue.Value is HostDelegate hostDelegate) {
            var target = hostDelegate.Target ?? CilValue.Null();
            var evaluationMethod = hostDelegate.Function.EvaluationMethod;
            if (evaluationMethod != null) {
                var methodArguments = evaluationMethod.IsStatic ? arguments : [target, .. arguments];
                var result = await InvokeEvaluationMethodAsync(compiled, evaluationMethod, methodArguments, resolver, context, handles, state);
                return evaluationMethod.ReturnsVoid ? CilValue.Null() : result;
            }
            var runtimeMethod = hostDelegate.Function.RuntimeMethod!;
            return await CallRuntimeMethodAsync(runtimeMethod, runtimeMethod.IsStatic ? null : target, arguments, resolver, context, handles);
        }
        if (delegateValue.IsNull)
            throw new NullReferenceException();
        var invoke = resolver.ResolveDelegateInvoke(delegateValue.CorValue!.GetExactType());
        return await CallRuntimeMethodAsync(invoke, delegateValue, arguments, resolver, context, handles);
    }
    // Copies an array initializer's data (a field of the expression assembly) into the array's elements
    private static void InitializeArray(CilValue arrayValue, FieldDefinitionHandle dataField, EvaluationMetadataResolver resolver) {
        var array = arrayValue.GetArrayValue();
        var count = array.GetCount();
        if (count == 0)
            return;
        var elementSize = ((ICorDebugGenericValue)array.GetElementAtPosition(0).UnwrapDebugValue()).GetSize();
        var data = resolver.GetEvaluationFieldData(dataField, count * elementSize);
        for (var i = 0; i < count; i++)
            ((ICorDebugGenericValue)array.GetElementAtPosition(i).UnwrapDebugValue()).SetValueFromBytes(data.AsSpan(i * elementSize, elementSize).ToArray());
    }
    // 'new int[2, 3]': the runtime's debugger API allocates single-dimensional arrays only, the others go through Array.CreateInstance
    private async Task<CilValue> CreateMultidimensionalArrayAsync(ResolvedCilType arrayType, Stack<CilValue> stack, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var lengths = stack.PopArguments(arrayType.ArrayRank).Select(it => checked((uint)it.AsInt32())).ToArray();
        var elementType = arrayType.ElementType!;
        ICorDebugValue? array;
        if (lengths.Length == 1) {
            array = await CreateArrayAsync(elementType, resolver.GetCorDebugType(elementType), lengths[0], resolver, context, handles);
        }
        else {
            var typeValue = await GetSystemTypeAsync(elementType, resolver, context, handles) ?? throw new InvalidOperationException("Failed to resolve the element type for the array allocation");
            var lengthsArray = await CreateArrayAsync(ResolvedCilType.FromPrimitive(PrimitiveTypeCode.Int32), resolver.GetCorDebugType(ResolvedCilType.FromPrimitive(PrimitiveTypeCode.Int32)), (uint)lengths.Length, resolver, context, handles)
                ?? throw new InvalidOperationException("Failed to allocate the array lengths");
            var lengthsValue = (ICorDebugArrayValue)lengthsArray.UnwrapDebugValue();
            for (var i = 0; i < lengths.Length; i++)
                new CorDebugLocation(lengthsValue.GetElementAtPosition(i)).Write(CilValue.FromPrimitive((int)lengths[i]));
            var createInstance = resolver.ResolveRuntimeMethod("System", "Array", "CreateInstance", "System.Type", "Int32[]");
            var eval = context.Thread.CreateEval();
            array = handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, createInstance.Function, [], [typeValue, lengthsArray], throwOnException: true));
        }
        return array == null ? CilValue.Null() : CilValue.FromCorValue(array);
    }
    // The string constructors over a character and a count or a character array, built on the host
    private static CilValue CreateString(ResolvedRuntimeMethod constructor, CilValue[] arguments) {
        var parameters = constructor.Signature.ParameterTypes;
        if (parameters.SequenceEqual(["Char", "Int32"]))
            return CilValue.FromPrimitive(new string((char)arguments[0].DereferenceLocation().AsInt32(), arguments[1].DereferenceLocation().AsInt32()));
        if (parameters.SequenceEqual(["Char[]"]))
            return CilValue.FromPrimitive(new string(ReadCharacters(arguments[0])));
        if (parameters.SequenceEqual(["Char[]", "Int32", "Int32"]))
            return CilValue.FromPrimitive(new string(ReadCharacters(arguments[0]), arguments[1].DereferenceLocation().AsInt32(), arguments[2].DereferenceLocation().AsInt32()));
        throw new NotSupportedException($"The string constructor ({string.Join(", ", parameters)}) is not supported in the debugger");
    }
    private static char[] ReadCharacters(CilValue arrayValue) {
        var array = arrayValue.DereferenceLocation().GetArrayValue();
        var characters = new char[array.GetCount()];
        for (var i = 0; i < characters.Length; i++)
            characters[i] = (char)CilValue.FromCorValue(array.GetElementAtPosition(i)).AsInt32();
        return characters;
    }
    // The elements of a sequence as interpreter values: a host sequence's own, an array's, anything else enumerated
    // by the debuggee into an array
    private async Task<List<CilValue>> EnumerateAsync(CilValue source, ResolvedCilType elementType, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        source = source.DereferenceLocation();
        if (source.Value is HostSequence sequence)
            return new List<CilValue>(sequence.Items);
        if (source.IsNull)
            throw new EvaluationThrewException("System.ArgumentNullException");
        if (source.CorValue?.UnwrapDebugValue() is not ICorDebugArrayValue array) {
            var toArray = resolver.ResolveRuntimeMethod("System.Linq", "Enumerable", "ToArray", "System.Collections.Generic.IEnumerable`1<!!0>");
            var eval = context.Thread.CreateEval();
            var enumerated = handles.Track(await debugger.FuncEval.CallFunctionAsync(eval, toArray.Function, [resolver.GetCorDebugType(elementType)], [source.CorValue!], throwOnException: true));
            array = enumerated?.UnwrapDebugValue() as ICorDebugArrayValue ?? throw new InvalidOperationException("The enumeration did not produce an array");
        }
        var count = array.GetCount();
        var items = new List<CilValue>(count);
        for (var i = 0; i < count; i++)
            items.Add(handles.Root(CilValue.FromCorValue(array.GetElementAtPosition(i))));
        return items;
    }
    // A host sequence as a debuggee array of its element type. The items are materialized first: the func evals that
    // takes would neuter the array's element values
    private async Task<CilValue> MaterializeSequenceAsync(HostSequence sequence, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var elementType = sequence.ElementType;
        var isReferenceSlot = elementType.IsReferenceType;
        var items = new List<CilValue>(sequence.Items.Count);
        foreach (var item in sequence.Items)
            items.Add(isReferenceSlot ? await MaterializeForReferenceSlotAsync(item, resolver, context, handles) : await MaterializeForStoreAsync(item, resolver, context, handles));

        var array = await CreateArrayAsync(elementType, resolver.GetCorDebugType(elementType), (uint)items.Count, resolver, context, handles)
            ?? throw new InvalidOperationException("Failed to allocate the sequence's array");
        var arrayValue = (ICorDebugArrayValue)array.UnwrapDebugValue();
        for (var i = 0; i < items.Count; i++) {
            if (!items[i].IsNull)
                new CorDebugLocation(arrayValue.GetElementAtPosition(i)).Write(items[i]);
        }
        return handles.Root(CilValue.FromCorValue(array));
    }
    // A value going into a reference slot (an object[] element): a host primitive or a debuggee value type is boxed
    private async Task<CilValue> MaterializeForReferenceSlotAsync(CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        value = value.DereferenceLocation();
        if (value.Value != null && value.Value is not string && value.Value is not ResolvedCilType && !value.IsHostValue()) {
            var elementType = CilValueEncoding.GetElementType(value.Value);
            if (primitiveTypes.TryGetClass(elementType, out var boxedClass))
                return CilValue.FromDebuggeeValue(await BoxBytesAsync(boxedClass, [], CilValueEncoding.GetBytes(value.Value, elementType), context, handles));
        }
        if (value.CorValue != null && value.CorValue is not ICorDebugReferenceValue && value.CorValue.UnwrapDebugValue() is ICorDebugGenericValue)
            return await BoxAsync(value, value.CorValue.GetExactType(), context, handles);
        return await MaterializeForStoreAsync(value, resolver, context, handles);
    }
    private async Task<CilValue> CreateListAsync(HostSequence sequence, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var array = await MaterializeSequenceAsync(sequence, resolver, context, handles);
        var constructor = resolver.ResolveRuntimeMethod("System.Collections.Generic", "List`1", ".ctor", "System.Collections.Generic.IEnumerable`1<!0>");
        var eval = context.Thread.CreateEval();
        var list = handles.Track(await debugger.FuncEval.NewObjectAsync(eval, constructor.Function, [resolver.GetCorDebugType(sequence.ElementType)], [array.CorValue!], throwOnException: true));
        return list == null ? CilValue.Null() : CilValue.FromCorValue(list);
    }
    private static HostFunction ResolveFunction(int token, EvaluationMetadataResolver resolver) {
        if (resolver.TryResolveEvaluationMethod(token, out var evaluationMethod))
            return new HostFunction(evaluationMethod);
        return new HostFunction(resolver.ResolveMethod(token));
    }
    private static NotSupportedException HostValueCannotLeave(CilValue value) {
        if (value.Value is HostDelegate or HostFunction)
            return new NotSupportedException("A lambda can be invoked or handed to a System.Linq operator, the debuggee has no code for it");
        if (value.Value is HostSpan)
            return new NotSupportedException("A span the expression built is read by the debugger, it cannot be handed to the debuggee");
        return new NotSupportedException("An object of a type the expression declares (an anonymous type, a closure) cannot be handed to the debuggee or shown as a result");
    }

    // A boxed value unboxes to an exact type match only, enum/underlying and interface matches are not accepted. The
    // runtime reports the object of a boxed primitive as a VALUETYPE of the primitive's class (System.Int32 for a boxed
    // int), so both a target named by type and a primitive one compare by class
    private bool IsUnboxCompatible(ICorDebugValue boxedObject, ResolvedCilType targetType) {
        ICorDebugClass? targetClass = null;
        if (targetType.Primitive != null) {
            var expectedElementType = targetType.Primitive.Value.ToCorElementType();
            if (expectedElementType == null || !primitiveTypes.TryGetClass(expectedElementType.Value, out targetClass))
                return false;
        }
        else if (targetType.RuntimeType != null) {
            targetClass = targetType.RuntimeType.Class;
        }
        if (targetClass == null)
            return false;

        var boxedClass = boxedObject.GetExactType().GetClass();
        return boxedClass.GetToken() == targetClass.GetToken() && boxedClass.GetModule() == targetClass.GetModule();
    }
    // The element at an index of any integer width, out of range reported the way the debuggee would
    private static ICorDebugValue GetElement(ICorDebugArrayValue array, CilValue index) {
        var position = index.AsInt64();
        if (position < 0 || position >= array.GetCount())
            throw new EvaluationThrewException("System.IndexOutOfRangeException");
        return array.GetElementAtPosition(checked((int)position));
    }
    private static string GetTypeDisplayName(ResolvedCilType type) {
        return type.RuntimeType != null ? type.RuntimeType.FullName : "the requested type";
    }

    private class ByRefArgument {
        public ICilLocation Location { get; }
        public ICorDebugValue Value { get; }

        public ByRefArgument(ICilLocation location, ICorDebugValue value) {
            Location = location;
            Value = value;
        }
    }
}
