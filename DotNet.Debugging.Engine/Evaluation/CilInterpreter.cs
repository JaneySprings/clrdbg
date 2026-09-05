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

                if (TryGetConstant(op, instruction.Operand, out var constant)) {
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

                if (TryGetSlotIndex(op, instruction.Operand, OpCodes.Ldarg_0, OpCodes.Ldarg, OpCodes.Ldarg_S, out var argumentIndex)) {
                    stack.Push(handles.Root(arguments[argumentIndex].Read()));
                    continue;
                }
                if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S) {
                    stack.Push(CilValue.FromLocation(arguments[(int)instruction.Operand!]));
                    continue;
                }
                if (op == OpCodes.Starg || op == OpCodes.Starg_S) {
                    arguments[(int)instruction.Operand!].Write(await MaterializeForStoreAsync(stack.Pop(), resolver, context, handles));
                    continue;
                }
                if (TryGetSlotIndex(op, instruction.Operand, OpCodes.Ldloc_0, OpCodes.Ldloc, OpCodes.Ldloc_S, out var localIndex)) {
                    stack.Push(handles.Root(locals[localIndex].Read()));
                    continue;
                }
                if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S) {
                    stack.Push(CilValue.FromLocation(locals[(int)instruction.Operand!]));
                    continue;
                }
                if (TryGetSlotIndex(op, instruction.Operand, OpCodes.Stloc_0, OpCodes.Stloc, OpCodes.Stloc_S, out localIndex)) {
                    locals[localIndex].Write(await MaterializeForStoreAsync(stack.Pop(), resolver, context, handles));
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
                    stack.Push(Negate(stack.Pop()));
                    continue;
                }
                if (op == OpCodes.Not) {
                    stack.Push(CilValue.FromPrimitive(~stack.Pop().AsInt64()));
                    continue;
                }
                if (IsBinaryOperation(op)) {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(EvaluateBinary(op, left, right));
                    continue;
                }
                if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un || op == OpCodes.Clt || op == OpCodes.Clt_Un) {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(CilValue.FromPrimitive(Compare(op, left, right) ? 1 : 0));
                    continue;
                }
                if (IsConversion(op)) {
                    stack.Push(Convert(op, stack.Pop()));
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
                if (IsComparisonBranch(op)) {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    if (EvaluateBranch(op, left, right))
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

                if (op == OpCodes.Ldobj || IsPrefixed(op, "ldind.")) {
                    stack.Push(handles.Root(stack.Pop().Dereference()));
                    continue;
                }
                if (op == OpCodes.Stobj || IsPrefixed(op, "stind.")) {
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
                    var length = checked((uint)stack.Pop().AsInt32());
                    var elementCilType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    var array = await CreateArrayAsync(elementCilType, resolver.GetCorDebugType(elementCilType), length, resolver, context, handles);
                    stack.Push(array == null ? CilValue.Null() : CilValue.FromCorValue(array));
                    continue;
                }
                if (op == OpCodes.Ldlen) {
                    stack.Push(CilValue.FromPrimitive(GetArrayValue(stack.Pop()).GetCount()));
                    continue;
                }
                if (op == OpCodes.Ldelema) {
                    var elementIndex = stack.Pop().AsInt32();
                    var array = GetArrayValue(stack.Pop());
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(array.GetElementAtPosition(elementIndex))));
                    continue;
                }
                if (op == OpCodes.Ldelem || IsPrefixed(op, "ldelem.")) {
                    var elementIndex = stack.Pop().AsInt32();
                    var array = GetArrayValue(stack.Pop());
                    stack.Push(handles.Root(new CorDebugLocation(array.GetElementAtPosition(elementIndex)).Read()));
                    continue;
                }
                if (op == OpCodes.Stelem || IsPrefixed(op, "stelem.")) {
                    var element = stack.Pop();
                    var elementIndex = stack.Pop().AsInt32();
                    var array = GetArrayValue(stack.Pop());
                    new CorDebugLocation(array.GetElementAtPosition(elementIndex)).Write(await MaterializeForStoreAsync(element, resolver, context, handles));
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
                        throw new InvalidCastException($"InvalidCastException: Cannot cast the debuggee value to '{GetTypeDisplayName(resolver, targetType)}'");
                    continue;
                }
                if (op == OpCodes.Box) {
                    var targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    stack.Push(await BoxAsync(stack.Pop(), resolver.GetCorDebugType(targetType), context, handles));
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
                    var boxed = GetBoxedValue(source);
                    if (!IsUnboxCompatible(boxed.GetObject(), targetType))
                        throw new InvalidCastException($"InvalidCastException: Cannot unbox the debuggee value to '{GetTypeDisplayName(resolver, targetType)}'");
                    stack.Push(CilValue.FromCorValue(boxed.GetObject()));
                    continue;
                }
                if (op == OpCodes.Unbox) {
                    var boxed = GetBoxedValue(stack.Pop());
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(boxed.GetObject())));
                    continue;
                }

                if ((op == OpCodes.Ldfld || op == OpCodes.Ldflda || op == OpCodes.Stfld) && resolver.TryResolveEvaluationField((int)instruction.Operand!, out var hostField, out _)) {
                    // A field of a host object: a captured variable, an anonymous type's member
                    var stored = op == OpCodes.Stfld ? stack.Pop() : null;
                    var location = GetHostObject(stack.Pop()).GetField(hostField);
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
                    var receiver = GetFieldReceiver(stack.Pop());
                    stack.Push(handles.Root(CilValue.FromCorValue(receiver.GetFieldValue(field.DeclaringType.Class, field.Token))));
                    continue;
                }
                if (op == OpCodes.Ldflda) {
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    var receiver = GetFieldReceiver(stack.Pop());
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(receiver.GetFieldValue(field.DeclaringType.Class, field.Token))));
                    continue;
                }
                if (op == OpCodes.Stfld) {
                    var value = stack.Pop();
                    var field = resolver.ResolveField((int)instruction.Operand!);
                    var receiver = GetFieldReceiver(stack.Pop());
                    new CorDebugLocation(receiver.GetFieldValue(field.DeclaringType.Class, field.Token)).Write(await MaterializeForStoreAsync(value, resolver, context, handles));
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
                    new CorDebugLocation(await GetStaticFieldValueAsync(field, resolver, context)).Write(await MaterializeForStoreAsync(stack.Pop(), resolver, context, handles));
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
                        var methodResult = await InvokeEvaluationMethodAsync(compiled, evaluationMethod, PopArguments(stack, evaluationMethod.ArgumentCount), resolver, context, handles, state);
                        if (!evaluationMethod.ReturnsVoid)
                            stack.Push(methodResult);
                        continue;
                    }
                    if (await TryCallHostAsync(token, stack, compiled, resolver, context, handles, state))
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
        var array = GetArrayValue(stack.Pop());

        if (methodName == "Set") {
            new CorDebugLocation(array.GetElement(indices)).Write(await MaterializeForStoreAsync(element!, resolver, context, handles));
            return;
        }
        var location = new CorDebugLocation(array.GetElement(indices));
        stack.Push(methodName == "Address" ? CilValue.FromLocation(location) : handles.Root(location.Read()));
    }
    private async Task<CilValue> NewObjectAsync(int token, Stack<CilValue> stack, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        // A type the expression assembly declares (a closure, a display class, an anonymous type) is instantiated on the host
        if (resolver.TryResolveEvaluationMethod(token, out var hostConstructor)) {
            var hostArguments = PopArguments(stack, hostConstructor.Signature.ParameterTypes.Length);
            var instance = CilValue.FromHostValue(new HostObject(hostConstructor.DeclaringType, hostConstructor.TypeArguments));
            await InvokeEvaluationMethodAsync(compiled, hostConstructor, [instance, .. hostArguments], resolver, context, handles, state);
            return instance;
        }
        if (resolver.TryResolveArrayConstructor(token, out var arrayType))
            return await CreateMultidimensionalArrayAsync(arrayType, stack, resolver, context, handles);

        var constructor = resolver.ResolveMethod(token);
        var constructorArguments = PopArguments(stack, constructor.Signature.ParameterTypes.Length);
        // A delegate over a function the expression named cannot exist in the debuggee (a lambda has no code there, a
        // method group no function pointer here): the interpreter invokes it itself
        if (constructorArguments.Length == 2 && Dereference(constructorArguments[1]).Value is HostFunction function) {
            var target = Dereference(constructorArguments[0]);
            return CilValue.FromHostValue(new HostDelegate(target.IsNull ? null : target, function));
        }
        // The runtime refuses to run a string constructor in a func eval, the common ones are built on the host
        if (resolver.GetRuntimeTypeName(constructor.DeclaringType) == "System.String")
            return CreateString(constructor, constructorArguments);

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
        var argumentValues = PopArguments(stack, method.Signature.ParameterTypes.Length);
        var receiverValue = method.IsStatic ? null : stack.Pop();

        if (resolver.GetRuntimeTypeName(method.DeclaringType) == "System.Type" && method.Name == "GetTypeFromHandle") {
            var tokenType = argumentValues[0].Value as ResolvedCilType ?? throw new InvalidOperationException("GetTypeFromHandle requires a type token");
            var typeValue = await GetSystemTypeAsync(tokenType, resolver, context, handles);
            stack.Push(typeValue == null ? CilValue.Null() : CilValue.FromTypeToken(tokenType, typeValue));
            return;
        }
        if (await TryExecuteInterpolationCallAsync(method, receiverValue, argumentValues, stack, resolver, context, handles))
            return;
        if (receiverValue?.Value is StringBuilder || receiverValue?.Location?.Read().Value is StringBuilder || argumentValues.Any(it => it.Value is StringBuilder))
            throw new InvalidOperationException($"Unhandled interpolated-string call '{resolver.GetRuntimeTypeName(method.DeclaringType)}.{method.Name}'");

        var byRefArguments = new List<ByRefArgument>();
        var callArguments = await MaterializeArgumentsAsync(method, argumentValues, method.IsStatic ? 0 : 1, resolver, context, handles, byRefArguments);
        if (receiverValue != null) {
            var receiver = receiverValue.Location != null ? receiverValue.Dereference() : receiverValue;
            if (receiver.IsNull)
                throw new NullReferenceException();
            callArguments[0] = await MaterializeReceiverAsync(receiver, context, constrainedType, resolver, handles);
        }

        // The declaring type's arguments come from the receiver's exact type when there is one, walked up to the
        // type declaring the method (an inherited method carries them on the base type), the method's own from the method spec
        var declaringTypeArity = resolver.GetRuntimeTypeGenericArity(method.DeclaringType);
        ICorDebugType[] declaringTypeArguments;
        if (!method.IsStatic && declaringTypeArity > 0 && callArguments[0].GetExactType() is { } receiverType
                && FindDeclaringType(receiverType, method.DeclaringType) is { } declaringType)
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
    // The base of the receiver's exact type that is the method's declaring type, carrying its instantiation. Null when
    // the declaring type is outside the class chain (an interface, an array's methods), leaving the method spec to supply it
    private static ICorDebugType? FindDeclaringType(ICorDebugType receiverType, ResolvedRuntimeType declaringType) {
        var declaringToken = (TypeDefToken)MetadataTokens.GetToken(declaringType.Handle);
        for (var type = (ICorDebugType?)receiverType; type != null; type = type.GetBaseType()) {
            if (type.GetElementType() is not (CorElementType.VALUETYPE or CorElementType.CLASS))
                return null;
            var corClass = type.GetClass();
            if (corClass.GetToken() == declaringToken && corClass.GetModule() == declaringType.Module.Module)
                return type;
        }
        return null;
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
        if (IsHostValue(value))
            throw HostValueCannotLeave(value);

        var eval = context.Thread.CreateEval();
        var expectedElementType = GetPrimitiveElementType(expectedPrimitive);
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
            new CorDebugLocation(pointerResult).Write(value);
            return pointerResult;
        }
        if (expectedType?.RuntimeType != null && resolver != null) {
            var typedResult = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, expectedType.RuntimeType.Class, [], throwOnException: true))
                ?? throw new InvalidOperationException("Failed to create the evaluation result value type");
            new CorDebugLocation(typedResult).Write(value);
            return typedResult;
        }

        var elementType = expectedElementType ?? GetPrimitiveElementType(value.Value);
        var result = (ICorDebugGenericValue)eval.CreateValue(elementType, null);
        result.SetValueFromBytes(CilValueEncoding.GetBytes(value.Value, elementType, result.GetSize()));
        return result;
    }
    private async Task<ICorDebugValue> MaterializeForCallAsync(CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();
        if (value.CorValue != null)
            return value.CorValue;
        if (value.Value is HostSequence sequence)
            return (await MaterializeSequenceAsync(sequence, resolver, context, handles)).CorValue!;
        if (IsHostValue(value))
            throw HostValueCannotLeave(value);
        return await MaterializeAsync(value, context, handles);
    }
    // Host values without a debuggee representation yet (e.g. strings produced by ldstr) are created in the
    // debuggee first, as a debuggee location can only hold values backed by an ICorDebugValue
    private async Task<CilValue> MaterializeForStoreAsync(CilValue value, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();
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

        var elementType = GetPrimitiveElementType(value.Value);
        if (!primitiveTypes.TryGetClass(elementType, out var boxedClass))
            return await MaterializeForCallAsync(value, resolver, context, handles);
        return await BoxBytesAsync(boxedClass, [], CilValueEncoding.GetBytes(value.Value, elementType), context, handles);
    }
    private async Task<CilValue> BoxAsync(CilValue value, ICorDebugType targetType, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();
        // A host object is a reference already
        if (IsHostValue(value))
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
        generic.SetValueFromBytes(CilValueEncoding.GetBytes(hostValue, generic.GetElementType(), generic.GetSize()));
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

        var boxed = GetBoxedValue(source);
        if (!IsUnboxCompatible(boxed.GetObject(), underlyingType))
            throw new InvalidCastException($"InvalidCastException: Cannot unbox the debuggee value to a nullable of '{GetTypeDisplayName(resolver, underlyingType)}'");

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
        var primitiveType = GetPrimitiveElementType(type.Primitive);
        if (primitiveType != null) {
            return primitiveType switch {
                CorElementType.R4 => CilValue.FromPrimitive(0f),
                CorElementType.R8 => CilValue.FromPrimitive(0d),
                CorElementType.I8 => CilValue.FromPrimitive(0L),
                CorElementType.U8 => CilValue.FromPrimitive(0UL),
                _ => CilValue.FromPrimitive(0)
            };
        }
        if (IsReferenceType(type))
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
        if (resolver.GetRuntimeTypeName(method.DeclaringType) != "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")
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
                var value = arguments[0];
                var alignment = arguments.Select(it => it.Value).OfType<int>().Skip(value.Value is int ? 1 : 0).FirstOrDefault();
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
    private async Task<string> FormatInterpolatedValueAsync(CilValue value, string? format, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();
        if (value.IsNull)
            return string.Empty;
        if (value.Value is IFormattable formattable)
            return formattable.ToString(format, null);
        if (value.Value != null)
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
        if (!method.IsStatic && Dereference(arguments[0]).Value is HostObject receiver && !receiver.TypeArguments.IsDefault)
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
    private async Task<bool> TryCallHostAsync(int token, Stack<CilValue> stack, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        var method = resolver.ResolveMethod(token);
        var argumentCount = method.Signature.ParameterTypes.Length + (method.IsStatic ? 0 : 1);
        if (stack.Count < argumentCount)
            return false;
        // Peeked, receiver first; popped once the call turns out to be ours
        var arguments = stack.Take(argumentCount).Reverse().Select(Dereference).ToArray();
        var typeName = resolver.GetRuntimeTypeName(method.DeclaringType);
        var returnsVoid = method.Signature.ReturnType == PrimitiveTypeCode.Void.ToString();

        if (!method.IsStatic && method.Name == "Invoke" && arguments[0].Value is HostDelegate) {
            PopArguments(stack, argumentCount);
            var result = await InvokeDelegateAsync(arguments[0], arguments.Skip(1).ToArray(), compiled, resolver, context, handles, state);
            if (!returnsVoid)
                stack.Push(result);
            return true;
        }
        if (!method.IsStatic && arguments[0].Value is HostObject) {
            PopArguments(stack, argumentCount);
            // The base constructor a host object's constructor calls has nothing to do
            if (method.Name == ".ctor" && typeName == "System.Object")
                return true;
            throw new NotSupportedException($"'{typeName}.{method.Name}' cannot be called on an object of a type the expression declares");
        }
        if (typeName == "System.Runtime.CompilerServices.RuntimeHelpers" && method.Name == "InitializeArray" && arguments[1].Value is FieldDefinitionHandle dataField) {
            PopArguments(stack, argumentCount);
            InitializeArray(arguments[0], dataField, resolver);
            return true;
        }
        if (typeName == "System.Linq.Enumerable" && arguments.Any(IsHostValue)) {
            PopArguments(stack, argumentCount);
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
    // Invokes a delegate for the interpreter's own purposes (an Invoke call, a System.Linq operator's lambda): one
    // the expression created runs here, one the debuggee holds runs there
    private async Task<CilValue> InvokeDelegateAsync(CilValue delegateValue, CilValue[] arguments, CompiledExpression compiled, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles, EvaluationState state) {
        delegateValue = Dereference(delegateValue);
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
        var array = GetArrayValue(arrayValue);
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
        var lengths = PopArguments(stack, arrayType.ArrayRank).Select(it => checked((uint)it.AsInt32())).ToArray();
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
            return CilValue.FromPrimitive(new string((char)Dereference(arguments[0]).AsInt32(), Dereference(arguments[1]).AsInt32()));
        if (parameters.SequenceEqual(["Char[]"]))
            return CilValue.FromPrimitive(new string(ReadCharacters(arguments[0])));
        if (parameters.SequenceEqual(["Char[]", "Int32", "Int32"]))
            return CilValue.FromPrimitive(new string(ReadCharacters(arguments[0]), Dereference(arguments[1]).AsInt32(), Dereference(arguments[2]).AsInt32()));
        throw new NotSupportedException($"The string constructor ({string.Join(", ", parameters)}) is not supported in the debugger");
    }
    private static char[] ReadCharacters(CilValue arrayValue) {
        var array = GetArrayValue(Dereference(arrayValue));
        var characters = new char[array.GetCount()];
        for (var i = 0; i < characters.Length; i++)
            characters[i] = (char)CilValue.FromCorValue(array.GetElementAtPosition(i)).AsInt32();
        return characters;
    }
    // The elements of a sequence as interpreter values: a host sequence's own, an array's, anything else enumerated
    // by the debuggee into an array
    private async Task<List<CilValue>> EnumerateAsync(CilValue source, ResolvedCilType elementType, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        source = Dereference(source);
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
        var isReferenceSlot = IsReferenceType(elementType);
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
        value = Dereference(value);
        if (value.Value != null && value.Value is not string && value.Value is not ResolvedCilType && !IsHostValue(value)) {
            var elementType = GetPrimitiveElementType(value.Value);
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
    private static HostObject GetHostObject(CilValue receiver) {
        receiver = Dereference(receiver);
        if (receiver.IsNull)
            throw new NullReferenceException();
        return receiver.Value as HostObject ?? throw new InvalidOperationException("The field belongs to a type the expression declares, the receiver is not an object of it");
    }
    private static CilValue Dereference(CilValue value) {
        return value.Location != null ? value.Dereference() : value;
    }
    private static bool IsHostValue(CilValue value) {
        return value.Value is HostObject or HostDelegate or HostSequence or HostFunction;
    }
    private static NotSupportedException HostValueCannotLeave(CilValue value) {
        if (value.Value is HostDelegate or HostFunction)
            return new NotSupportedException("A lambda can be invoked or handed to a System.Linq operator, the debuggee has no code for it");
        return new NotSupportedException("An object of a type the expression declares (an anonymous type, a closure) cannot be handed to the debuggee or shown as a result");
    }

    private static CilValue[] PopArguments(Stack<CilValue> stack, int count) {
        var arguments = new CilValue[count];
        for (var i = count - 1; i >= 0; i--)
            arguments[i] = stack.Pop();
        return arguments;
    }
    private static ICorDebugArrayValue GetArrayValue(CilValue value) {
        return value.CorValue?.UnwrapDebugValue() as ICorDebugArrayValue ?? throw new NullReferenceException("The array reference is null");
    }
    private static ICorDebugObjectValue GetFieldReceiver(CilValue receiver) {
        var corValue = receiver.CorValue;
        if (corValue == null && receiver.Location is CorDebugLocation directLocation)
            corValue = directLocation.Value;
        else if (corValue == null && receiver.Location != null)
            corValue = receiver.Location.Read().CorValue;
        return corValue?.UnwrapDebugValueToObject() ?? throw new NullReferenceException("The instance field receiver is null");
    }
    private static ICorDebugBoxValue GetBoxedValue(CilValue source) {
        if (source.Location != null)
            source = source.Dereference();
        if (source.IsNull)
            throw new NullReferenceException();
        var boxed = source.CorValue is ICorDebugReferenceValue reference
            ? reference.Dereference() as ICorDebugBoxValue
            : source.CorValue as ICorDebugBoxValue;
        return boxed ?? throw new InvalidCastException("The CIL value is not boxed");
    }
    // A boxed value unboxes to an exact type match only, enum/underlying and interface matches are not accepted. The
    // runtime reports the object of a boxed primitive as a VALUETYPE of the primitive's class (System.Int32 for a boxed
    // int), so both a target named by type and a primitive one compare by class
    private bool IsUnboxCompatible(ICorDebugValue boxedObject, ResolvedCilType targetType) {
        ICorDebugClass? targetClass = null;
        if (targetType.Primitive != null) {
            var expectedElementType = GetPrimitiveElementType(targetType.Primitive);
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
    private static bool IsReferenceType(ResolvedCilType type) {
        if (type.Primitive is PrimitiveTypeCode.String or PrimitiveTypeCode.Object || type.ElementType != null || type.HostType != null)
            return true;
        return type.RuntimeType != null && !EvaluationMetadataResolver.IsValueType(type.RuntimeType);
    }
    private static string GetTypeDisplayName(EvaluationMetadataResolver resolver, ResolvedCilType type) {
        return type.RuntimeType != null ? resolver.GetRuntimeTypeName(type.RuntimeType) : "the requested type";
    }
    private static CorElementType? GetPrimitiveElementType(PrimitiveTypeCode? primitive) {
        return primitive switch {
            PrimitiveTypeCode.Boolean => CorElementType.BOOLEAN,
            PrimitiveTypeCode.Char => CorElementType.CHAR,
            PrimitiveTypeCode.SByte => CorElementType.I1,
            PrimitiveTypeCode.Byte => CorElementType.U1,
            PrimitiveTypeCode.Int16 => CorElementType.I2,
            PrimitiveTypeCode.UInt16 => CorElementType.U2,
            PrimitiveTypeCode.Int32 => CorElementType.I4,
            PrimitiveTypeCode.UInt32 => CorElementType.U4,
            PrimitiveTypeCode.Int64 => CorElementType.I8,
            PrimitiveTypeCode.UInt64 => CorElementType.U8,
            PrimitiveTypeCode.Single => CorElementType.R4,
            PrimitiveTypeCode.Double => CorElementType.R8,
            PrimitiveTypeCode.IntPtr => CorElementType.I,
            PrimitiveTypeCode.UIntPtr => CorElementType.U,
            _ => null
        };
    }
    private static CorElementType GetPrimitiveElementType(object value) {
        return value switch {
            bool => CorElementType.BOOLEAN,
            char => CorElementType.CHAR,
            sbyte => CorElementType.I1,
            byte => CorElementType.U1,
            short => CorElementType.I2,
            ushort => CorElementType.U2,
            int => CorElementType.I4,
            uint => CorElementType.U4,
            long => CorElementType.I8,
            ulong => CorElementType.U8,
            float => CorElementType.R4,
            double => CorElementType.R8,
            _ => throw new NotSupportedException($"Value '{value.GetType().Name}' is not a primitive CIL value")
        };
    }

    private static bool TryGetConstant(OpCode op, object? operand, out CilValue value) {
        value = null!;
        if (op == OpCodes.Ldnull)
            value = CilValue.Null();
        // 'ldc.i4.m1' through 'ldc.i4.8' are consecutive opcodes loading -1 through 8
        else if (op.Value >= OpCodes.Ldc_I4_M1.Value && op.Value <= OpCodes.Ldc_I4_8.Value)
            value = CilValue.FromPrimitive(op.Value - OpCodes.Ldc_I4_0.Value);
        else if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S)
            value = CilValue.FromPrimitive(System.Convert.ToInt32(operand));
        else if (op == OpCodes.Ldc_I8)
            value = CilValue.FromPrimitive((long)operand!);
        else if (op == OpCodes.Ldc_R4)
            value = CilValue.FromPrimitive((float)operand!);
        else if (op == OpCodes.Ldc_R8)
            value = CilValue.FromPrimitive((double)operand!);
        return value != null;
    }
    // The '.0' through '.3' short forms of ldarg/ldloc/stloc are consecutive opcodes encoding the slot index
    private static bool TryGetSlotIndex(OpCode op, object? operand, OpCode shortFormZero, OpCode longForm, OpCode shortOperandForm, out int index) {
        index = -1;
        if (op.Value >= shortFormZero.Value && op.Value < shortFormZero.Value + 4)
            index = op.Value - shortFormZero.Value;
        else if (op == longForm || op == shortOperandForm)
            index = (int)operand!;
        return index >= 0;
    }
    private static bool IsPrefixed(OpCode op, string prefix) {
        return op.Name?.StartsWith(prefix, StringComparison.Ordinal) == true;
    }
    private static bool IsBinaryOperation(OpCode op) {
        return op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Mul
            || op == OpCodes.Add_Ovf || op == OpCodes.Add_Ovf_Un || op == OpCodes.Sub_Ovf || op == OpCodes.Sub_Ovf_Un
            || op == OpCodes.Mul_Ovf || op == OpCodes.Mul_Ovf_Un
            || op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem || op == OpCodes.Rem_Un
            || op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor || op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un;
    }
    private static bool IsConversion(OpCode op) {
        return IsPrefixed(op, "conv.");
    }
    private static bool IsComparisonBranch(OpCode op) {
        return op.FlowControl == FlowControl.Cond_Branch
            && op != OpCodes.Brtrue && op != OpCodes.Brtrue_S && op != OpCodes.Brfalse && op != OpCodes.Brfalse_S && op != OpCodes.Switch;
    }

    internal static CilValue EvaluateBinary(OpCode op, CilValue left, CilValue right) {
        if (left.Value is float or double || right.Value is float or double) {
            var a = left.AsFloat();
            var b = right.AsFloat();
            double result;
            if (op == OpCodes.Add)
                result = a + b;
            else if (op == OpCodes.Sub)
                result = a - b;
            else if (op == OpCodes.Mul)
                result = a * b;
            else if (op == OpCodes.Div)
                result = a / b;
            else if (op == OpCodes.Rem)
                result = a % b;
            else
                throw new NotSupportedException($"Floating-point operation '{op.Name}' is not supported");
            if (left.Value is double || right.Value is double)
                return CilValue.FromPrimitive(result);
            return CilValue.FromPrimitive((float)result);
        }

        if (left.Value is long or ulong || right.Value is long or ulong) {
            var a = left.AsInt64();
            var b = right.AsInt64();
            if (op == OpCodes.Div_Un)
                return CilValue.FromPrimitive(unchecked((ulong)a) / unchecked((ulong)b));
            if (op == OpCodes.Rem_Un)
                return CilValue.FromPrimitive(unchecked((ulong)a) % unchecked((ulong)b));
            if (op == OpCodes.Shr_Un)
                return CilValue.FromPrimitive(unchecked((long)(unchecked((ulong)a) >> ((int)b & 0x3f))));
            if (op == OpCodes.Add_Ovf)
                return CilValue.FromPrimitive(checked(a + b));
            if (op == OpCodes.Sub_Ovf)
                return CilValue.FromPrimitive(checked(a - b));
            if (op == OpCodes.Mul_Ovf)
                return CilValue.FromPrimitive(checked(a * b));
            if (op == OpCodes.Add_Ovf_Un)
                return CilValue.FromPrimitive(checked(unchecked((ulong)a) + unchecked((ulong)b)));
            if (op == OpCodes.Sub_Ovf_Un)
                return CilValue.FromPrimitive(checked(unchecked((ulong)a) - unchecked((ulong)b)));
            if (op == OpCodes.Mul_Ovf_Un)
                return CilValue.FromPrimitive(checked(unchecked((ulong)a) * unchecked((ulong)b)));
            if (op == OpCodes.Add)
                return CilValue.FromPrimitive(a + b);
            if (op == OpCodes.Sub)
                return CilValue.FromPrimitive(a - b);
            if (op == OpCodes.Mul)
                return CilValue.FromPrimitive(a * b);
            if (op == OpCodes.Div)
                return CilValue.FromPrimitive(a / b);
            if (op == OpCodes.Rem)
                return CilValue.FromPrimitive(a % b);
            if (op == OpCodes.And)
                return CilValue.FromPrimitive(a & b);
            if (op == OpCodes.Or)
                return CilValue.FromPrimitive(a | b);
            if (op == OpCodes.Xor)
                return CilValue.FromPrimitive(a ^ b);
            if (op == OpCodes.Shl)
                return CilValue.FromPrimitive(a << ((int)b & 0x3f));
            if (op == OpCodes.Shr)
                return CilValue.FromPrimitive(a >> ((int)b & 0x3f));
            throw new NotSupportedException($"Integer operation '{op.Name}' is not supported");
        }

        var x = left.AsInt32();
        var y = right.AsInt32();
        if (op == OpCodes.Div_Un)
            return CilValue.FromPrimitive(unchecked((uint)x) / unchecked((uint)y));
        if (op == OpCodes.Rem_Un)
            return CilValue.FromPrimitive(unchecked((uint)x) % unchecked((uint)y));
        if (op == OpCodes.Shr_Un)
            return CilValue.FromPrimitive(unchecked((int)(unchecked((uint)x) >> (y & 0x1f))));
        if (op == OpCodes.Add_Ovf)
            return CilValue.FromPrimitive(checked(x + y));
        if (op == OpCodes.Sub_Ovf)
            return CilValue.FromPrimitive(checked(x - y));
        if (op == OpCodes.Mul_Ovf)
            return CilValue.FromPrimitive(checked(x * y));
        if (op == OpCodes.Add_Ovf_Un)
            return CilValue.FromPrimitive(checked(unchecked((uint)x) + unchecked((uint)y)));
        if (op == OpCodes.Sub_Ovf_Un)
            return CilValue.FromPrimitive(checked(unchecked((uint)x) - unchecked((uint)y)));
        if (op == OpCodes.Mul_Ovf_Un)
            return CilValue.FromPrimitive(checked(unchecked((uint)x) * unchecked((uint)y)));
        if (op == OpCodes.Add)
            return CilValue.FromPrimitive(x + y);
        if (op == OpCodes.Sub)
            return CilValue.FromPrimitive(x - y);
        if (op == OpCodes.Mul)
            return CilValue.FromPrimitive(x * y);
        if (op == OpCodes.Div)
            return CilValue.FromPrimitive(x / y);
        if (op == OpCodes.Rem)
            return CilValue.FromPrimitive(x % y);
        if (op == OpCodes.And)
            return CilValue.FromPrimitive(x & y);
        if (op == OpCodes.Or)
            return CilValue.FromPrimitive(x | y);
        if (op == OpCodes.Xor)
            return CilValue.FromPrimitive(x ^ y);
        if (op == OpCodes.Shl)
            return CilValue.FromPrimitive(x << (y & 0x1f));
        if (op == OpCodes.Shr)
            return CilValue.FromPrimitive(x >> (y & 0x1f));
        throw new NotSupportedException($"Integer operation '{op.Name}' is not supported");
    }
    private static CilValue Negate(CilValue value) {
        if (value.Value is float or double)
            return CilValue.FromPrimitive(-value.AsFloat());
        if (value.Value is long or ulong)
            return CilValue.FromPrimitive(-value.AsInt64());
        return CilValue.FromPrimitive(-value.AsInt32());
    }
    internal static bool Compare(OpCode op, CilValue left, CilValue right) {
        if (op == OpCodes.Ceq) {
            if (left.IsNull || right.IsNull)
                return left.IsNull == right.IsNull;
            if (left.CorValue is ICorDebugReferenceValue leftReference && right.CorValue is ICorDebugReferenceValue rightReference)
                return leftReference.GetValue() == rightReference.GetValue();
            if (left.Value is float or double || right.Value is float or double)
                return left.AsFloat() == right.AsFloat();
            if (left.TryGetInt64(out var leftInteger) && right.TryGetInt64(out var rightInteger))
                return leftInteger == rightInteger;
            return Equals(left.Value, right.Value);
        }
        if (left.CorValue is ICorDebugReferenceValue || right.CorValue is ICorDebugReferenceValue || left.IsNull || right.IsNull) {
            if (op == OpCodes.Cgt_Un)
                return !left.IsNull && right.IsNull;
            if (op == OpCodes.Clt_Un)
                return left.IsNull && !right.IsNull;
            throw new InvalidOperationException($"Reference values cannot be compared with '{op.Name}'");
        }
        if (left.Value is float or double || right.Value is float or double) {
            var a = left.AsFloat();
            var b = right.AsFloat();
            if (op == OpCodes.Cgt)
                return a > b;
            if (op == OpCodes.Clt)
                return a < b;
            // The unordered variants are true for NaN
            if (op == OpCodes.Cgt_Un)
                return double.IsNaN(a) || double.IsNaN(b) || a > b;
            return double.IsNaN(a) || double.IsNaN(b) || a < b;
        }
        if (op == OpCodes.Cgt_Un)
            return unchecked((ulong)left.AsInt64()) > unchecked((ulong)right.AsInt64());
        if (op == OpCodes.Clt_Un)
            return unchecked((ulong)left.AsInt64()) < unchecked((ulong)right.AsInt64());
        return op == OpCodes.Cgt ? left.AsInt64() > right.AsInt64() : left.AsInt64() < right.AsInt64();
    }
    private static bool EvaluateBranch(OpCode op, CilValue left, CilValue right) {
        if (op == OpCodes.Beq || op == OpCodes.Beq_S)
            return Compare(OpCodes.Ceq, left, right);
        if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S)
            return !Compare(OpCodes.Ceq, left, right);
        if (op == OpCodes.Bgt || op == OpCodes.Bgt_S)
            return Compare(OpCodes.Cgt, left, right);
        if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
            return Compare(OpCodes.Cgt_Un, left, right);
        if (op == OpCodes.Blt || op == OpCodes.Blt_S)
            return Compare(OpCodes.Clt, left, right);
        if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S)
            return Compare(OpCodes.Clt_Un, left, right);
        if (op == OpCodes.Bge || op == OpCodes.Bge_S)
            return !Compare(OpCodes.Clt, left, right);
        if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S)
            return !Compare(OpCodes.Clt_Un, left, right);
        if (op == OpCodes.Ble || op == OpCodes.Ble_S)
            return !Compare(OpCodes.Cgt, left, right);
        if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S)
            return !Compare(OpCodes.Cgt_Un, left, right);
        throw new NotSupportedException($"Conditional branch '{op.Name}' is not supported");
    }
    private static CilValue Convert(OpCode op, CilValue value) {
        var isFloat = value.Value is float or double;
        var signed = isFloat ? unchecked((long)value.AsFloat()) : value.AsInt64();
        var unsigned = isFloat ? unchecked((ulong)value.AsFloat()) : value.AsUInt64();
        if (op == OpCodes.Conv_I1)
            return CilValue.FromPrimitive((int)(sbyte)signed);
        if (op == OpCodes.Conv_U1)
            return CilValue.FromPrimitive((int)(byte)signed);
        if (op == OpCodes.Conv_I2)
            return CilValue.FromPrimitive((int)(short)signed);
        if (op == OpCodes.Conv_U2)
            return CilValue.FromPrimitive((int)(ushort)signed);
        if (op == OpCodes.Conv_I4)
            return CilValue.FromPrimitive((int)signed);
        if (op == OpCodes.Conv_U4)
            return CilValue.FromPrimitive((uint)signed);
        if (op == OpCodes.Conv_I8)
            return CilValue.FromPrimitive(signed);
        if (op == OpCodes.Conv_U8)
            return CilValue.FromPrimitive(unsigned);
        if (op == OpCodes.Conv_R4)
            return CilValue.FromPrimitive(isFloat ? (float)value.AsFloat() : (float)signed);
        if (op == OpCodes.Conv_R8)
            return CilValue.FromPrimitive(isFloat ? value.AsFloat() : (double)signed);
        if (op == OpCodes.Conv_R_Un)
            return CilValue.FromPrimitive((double)unsigned);
        if (op == OpCodes.Conv_I)
            return CilValue.FromPrimitive(IntPtr.Size == 8 ? signed : (int)signed);
        if (op == OpCodes.Conv_U)
            return CilValue.FromPrimitive(IntPtr.Size == 8 ? unsigned : (uint)unsigned);
        if (op == OpCodes.Conv_Ovf_I1)
            return CilValue.FromPrimitive((int)checked((sbyte)(isFloat ? value.AsFloat() : signed)));
        if (op == OpCodes.Conv_Ovf_U1)
            return CilValue.FromPrimitive((int)checked((byte)(isFloat ? value.AsFloat() : signed)));
        if (op == OpCodes.Conv_Ovf_I2)
            return CilValue.FromPrimitive((int)checked((short)(isFloat ? value.AsFloat() : signed)));
        if (op == OpCodes.Conv_Ovf_U2)
            return CilValue.FromPrimitive((int)checked((ushort)(isFloat ? value.AsFloat() : signed)));
        if (op == OpCodes.Conv_Ovf_I4)
            return CilValue.FromPrimitive(checked((int)(isFloat ? value.AsFloat() : signed)));
        if (op == OpCodes.Conv_Ovf_U4)
            return CilValue.FromPrimitive(checked((uint)(isFloat ? value.AsFloat() : signed)));
        if (op == OpCodes.Conv_Ovf_I8)
            return CilValue.FromPrimitive(isFloat ? checked((long)value.AsFloat()) : signed);
        if (op == OpCodes.Conv_Ovf_U8)
            return CilValue.FromPrimitive(isFloat ? checked((ulong)value.AsFloat()) : unsigned);
        throw new NotSupportedException($"Conversion opcode '{op.Name}' is not supported yet");
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
