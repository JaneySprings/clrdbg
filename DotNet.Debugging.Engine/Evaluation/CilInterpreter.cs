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
        var locals = CreateLocals(compiled, frame, body.LocalSignature, context.RootValue != null);

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
        var syntheticVariables = new Dictionary<string, ICilLocation>(StringComparer.Ordinal);
        var result = await InterpretAsync(compiled, decoded, arguments, locals, resolver, context, handles, syntheticVariables);
        var value = await MaterializeAsync(result, context, handles, resolver.ResolveMethodReturnType(compiled.EntryMethod), resolver);
        return EvaluationResult.FromValue(value, handles.Detach(value));
    }

    private static ICilLocation[] CreateArguments(ICorDebugILFrame? frame, EvaluationContext context) {
        if (context.RootValue != null)
            return [new CorDebugLocation(context.RootValue)];
        return frame!.GetArguments().Select(it => (ICilLocation)new CorDebugLocation(it)).ToArray();
    }
    // The evaluation method's locals start with the frame's locals (so the expression can read and assign them), the rest are temporaries
    private static ICilLocation[] CreateLocals(CompiledExpression compiled, ICorDebugILFrame? frame, StandaloneSignatureHandle localSignature, bool isTypeContext) {
        var localCount = localSignature.IsNil
            ? 0
            : compiled.MetadataReader.GetStandaloneSignature(localSignature).DecodeLocalSignature(LocalCountSignatureProvider.Instance, genericContext: null).Length;
        var frameLocals = frame?.GetLocalVariables();
        var result = new ICilLocation[localCount];
        for (var i = 0; i < result.Length; i++) {
            result[i] = !isTypeContext && i < frameLocals!.Length
                ? new CorDebugLocation(frameLocals[i])
                : new TemporaryLocation(CilValue.Null());
        }
        return result;
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
        Dictionary<string, ICilLocation> syntheticVariables) {
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
                    stack.Push(CilValue.FromPrimitive(resolver.ResolveTypeToken((int)instruction.Operand!)));
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
                    arguments[(int)instruction.Operand!].Write(await MaterializeForStoreAsync(stack.Pop(), context, handles));
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
                    locals[localIndex].Write(await MaterializeForStoreAsync(stack.Pop(), context, handles));
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
                    address.Write(await MaterializeForStoreAsync(value, context, handles));
                    continue;
                }
                if (op == OpCodes.Cpobj) {
                    var source = stack.Pop().Dereference();
                    var destination = stack.Pop().Location ?? throw new InvalidOperationException("cpobj requires a managed location");
                    destination.Write(await MaterializeForStoreAsync(source, context, handles));
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
                    new CorDebugLocation(array.GetElementAtPosition(elementIndex)).Write(await MaterializeForStoreAsync(element, context, handles));
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
                    var boxed = GetBoxedValue(source);
                    var targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
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
                    new CorDebugLocation(receiver.GetFieldValue(field.DeclaringType.Class, field.Token)).Write(await MaterializeForStoreAsync(value, context, handles));
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
                    new CorDebugLocation(await GetStaticFieldValueAsync(field, resolver, context)).Write(await MaterializeForStoreAsync(stack.Pop(), context, handles));
                    continue;
                }

                if (op == OpCodes.Newobj) {
                    stack.Push(await NewObjectAsync((int)instruction.Operand!, stack, resolver, context, handles));
                    continue;
                }
                if (op == OpCodes.Call || op == OpCodes.Callvirt) {
                    var callConstrainedType = constrainedType;
                    constrainedType = null;
                    var token = (int)instruction.Operand!;
                    if (resolver.TryResolveDebuggerIntrinsic(token, out var intrinsicName)) {
                        await ExecuteDebuggerIntrinsicAsync(intrinsicName, stack, syntheticVariables, resolver, context, handles);
                        continue;
                    }
                    if (resolver.TryResolveArrayMethod(token, out var arrayMethodName, out var arrayIndexCount)) {
                        await ExecuteArrayMethodAsync(arrayMethodName, arrayIndexCount, stack, context, handles);
                        continue;
                    }
                    if (resolver.TryResolveEvaluationMethod(token, out var evaluationMethod)) {
                        var methodArguments = PopArguments(stack, evaluationMethod.Signature.ParameterTypes.Length + (evaluationMethod.IsStatic ? 0 : 1));
                        var methodResult = await InterpretAsync(
                            compiled,
                            compiled.GetDecodedMethod(evaluationMethod.Handle),
                            methodArguments.Select(it => (ICilLocation)new TemporaryLocation(it)).ToArray(),
                            CreateTemporaryLocals(resolver, resolver.GetEvaluationMethodBody(evaluationMethod.Handle).LocalSignature),
                            resolver,
                            context,
                            handles,
                            syntheticVariables);
                        if (evaluationMethod.Signature.ReturnType != PrimitiveTypeCode.Void.ToString())
                            stack.Push(methodResult);
                        continue;
                    }
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
    private async Task ExecuteArrayMethodAsync(string methodName, int indexCount, Stack<CilValue> stack, EvaluationContext context, EvaluationHandleScope handles) {
        var element = methodName == "Set" ? stack.Pop() : null;
        var indices = new uint[indexCount];
        for (var i = indexCount - 1; i >= 0; i--)
            indices[i] = checked((uint)stack.Pop().AsInt32());
        var array = GetArrayValue(stack.Pop());

        if (methodName == "Set") {
            new CorDebugLocation(array.GetElement(indexCount, indices)).Write(await MaterializeForStoreAsync(element!, context, handles));
            return;
        }
        var location = new CorDebugLocation(array.GetElement(indexCount, indices));
        stack.Push(methodName == "Address" ? CilValue.FromLocation(location) : handles.Root(location.Read()));
    }
    private async Task<CilValue> NewObjectAsync(int token, Stack<CilValue> stack, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        var constructor = resolver.ResolveMethod(token);
        var constructorArguments = PopArguments(stack, constructor.Signature.ParameterTypes.Length);
        var byRefArguments = new List<ByRefArgument>();
        var argumentValues = await MaterializeArgumentsAsync(constructor, constructorArguments, receiverOffset: 0, context, handles, byRefArguments);

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
        var method = resolver.ResolveMethod(token);
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
        var callArguments = await MaterializeArgumentsAsync(method, argumentValues, method.IsStatic ? 0 : 1, context, handles, byRefArguments);
        if (receiverValue != null) {
            var receiver = receiverValue.Location != null ? receiverValue.Dereference() : receiverValue;
            if (receiver.IsNull)
                throw new NullReferenceException();
            callArguments[0] = await MaterializeReceiverAsync(receiver, context, constrainedType, handles);
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
            if (corClass.GetToken() == declaringToken && corClass.GetModule().GetBaseAddress() == declaringType.Module.BaseAddress)
                return type;
        }
        return null;
    }
    private async Task ExecuteDebuggerIntrinsicAsync(string name, Stack<CilValue> stack, Dictionary<string, ICilLocation> syntheticVariables, EvaluationMetadataResolver resolver, EvaluationContext context, EvaluationHandleScope handles) {
        switch (name) {
            case "CreateVariable": {
                stack.Pop(); // custom type payload
                stack.Pop(); // custom type payload id
                var variableName = stack.Pop().Value as string ?? throw new InvalidOperationException("The synthetic variable name is unavailable");
                var variableType = stack.Pop().Value as ResolvedCilType ?? throw new InvalidOperationException("The synthetic variable type is unavailable");
                syntheticVariables[variableName] = await CreateSyntheticVariableAsync(variableType, resolver, context, handles);
                return;
            }
            case "GetVariableAddress": {
                var variableName = stack.Pop().Value as string ?? throw new InvalidOperationException("The synthetic variable name is unavailable");
                if (!syntheticVariables.TryGetValue(variableName, out var location))
                    throw new InvalidOperationException($"The synthetic variable '{variableName}' is unavailable");
                stack.Push(CilValue.FromLocation(location));
                return;
            }
            case "GetObjectByAlias": {
                var variableName = stack.Pop().Value as string ?? throw new InvalidOperationException("The synthetic variable name is unavailable");
                if (!syntheticVariables.TryGetValue(variableName, out var location))
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

        var eval = context.Thread.CreateEval();
        var expectedElementType = GetPrimitiveElementType(expectedPrimitive);
        if (value.Value == null && expectedElementType != null && value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric) {
            var primitiveResult = (ICorDebugGenericValue)eval.CreateValue(expectedElementType.Value, null);
            primitiveResult.SetValueFromBytes(sourceGeneric.GetValueAsBytes());
            return primitiveResult;
        }
        if (value.Value == null)
            return eval.CreateValue(CorElementType.CLASS, null);
        if (value.Value is string text)
            return handles.Track(await debugger.FuncEval.NewStringAsync(eval, text, throwOnException: true));
        if (expectedType?.RuntimeType != null && resolver != null) {
            var typedResult = handles.Track(await debugger.FuncEval.NewObjectNoConstructorAsync(eval, expectedType.RuntimeType.Class, [], throwOnException: true))
                ?? throw new InvalidOperationException("Failed to create the evaluation result value type");
            new CorDebugLocation(typedResult).Write(value);
            return typedResult;
        }

        var elementType = expectedElementType ?? GetPrimitiveElementType(value.Value);
        var result = (ICorDebugGenericValue)eval.CreateValue(elementType, null);
        var materializedValue = elementType == CorElementType.BOOLEAN ? value.IsTrue() : value.Value;
        result.SetValueFromBytes(CilValueEncoding.GetBytes(materializedValue, elementType));
        return result;
    }
    private async Task<ICorDebugValue> MaterializeForCallAsync(CilValue value, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();
        if (value.CorValue != null)
            return value.CorValue;
        return await MaterializeAsync(value, context, handles);
    }
    // Host values without a debuggee representation yet (e.g. strings produced by ldstr) are created in the
    // debuggee first, as a debuggee location can only hold values backed by an ICorDebugValue
    private async Task<CilValue> MaterializeForStoreAsync(CilValue value, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();
        if (value.CorValue != null || value.IsNull)
            return value;
        if (value.Value is string text) {
            var eval = context.Thread.CreateEval();
            var materialized = handles.Track(await debugger.FuncEval.NewStringAsync(eval, text, throwOnException: true));
            return CilValue.FromDebuggeeValue(materialized);
        }
        return value;
    }
    // Materializes the call arguments after 'receiverOffset' reserved slots, honouring by-reference parameters
    private async Task<ICorDebugValue[]> MaterializeArgumentsAsync(ResolvedRuntimeMethod method, CilValue[] arguments, int receiverOffset, EvaluationContext context, EvaluationHandleScope handles, List<ByRefArgument> byRefArguments) {
        var result = new ICorDebugValue[arguments.Length + receiverOffset];
        for (var i = 0; i < arguments.Length; i++) {
            result[i + receiverOffset] = method.Signature.ParameterTypes[i].EndsWith('&')
                ? await MaterializeByRefArgumentAsync(arguments[i], context, handles, byRefArguments)
                : await MaterializeForCallAsync(arguments[i], context, handles);
        }
        return result;
    }
    private async Task<ICorDebugValue> MaterializeByRefArgumentAsync(CilValue value, EvaluationContext context, EvaluationHandleScope handles, List<ByRefArgument> byRefArguments) {
        if (value.Location is CorDebugLocation location)
            return location.Value;
        if (value.Location is SyntheticVariableLocation synthetic)
            return synthetic.StorageValue;
        if (value.Location == null)
            throw new InvalidOperationException("A by-reference argument requires a managed location");

        // A host temporary is passed as a debuggee copy and written back after the call
        var materialized = await MaterializeForCallAsync(value.Location.Read(), context, handles);
        byRefArguments.Add(new ByRefArgument(value.Location, materialized));
        return materialized;
    }
    private static void WriteBackByRefArguments(List<ByRefArgument> byRefArguments, EvaluationHandleScope handles) {
        foreach (var argument in byRefArguments)
            argument.Location.Write(handles.Root(CilValue.FromCorValue(argument.Value)));
    }
    // Instance calls need a reference receiver: value types are boxed, honouring the 'constrained.' prefix
    private async Task<ICorDebugValue> MaterializeReceiverAsync(CilValue value, EvaluationContext context, ResolvedCilType? constrainedType, EvaluationHandleScope handles) {
        if (constrainedType != null && value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric) {
            var exactType = value.CorValue.GetExactType();
            var boxed = await BoxBytesAsync(exactType.GetClass(), exactType.GetTypeParameters(), sourceGeneric.GetValueAsBytes(), context, handles);
            return boxed;
        }
        if (value.CorValue != null)
            return value.CorValue;
        if (value.Value == null)
            return await MaterializeForCallAsync(value, context, handles);

        var elementType = GetPrimitiveElementType(value.Value);
        if (!primitiveTypes.TryGetClass(elementType, out var boxedClass))
            return await MaterializeForCallAsync(value, context, handles);
        return await BoxBytesAsync(boxedClass, [], CilValueEncoding.GetBytes(value.Value, elementType), context, handles);
    }
    private async Task<CilValue> BoxAsync(CilValue value, ICorDebugType targetType, EvaluationContext context, EvaluationHandleScope handles) {
        if (value.Location != null)
            value = value.Dereference();

        byte[] data;
        if (value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric)
            data = sourceGeneric.GetValueAsBytes();
        else if (value.Value != null)
            data = CilValueEncoding.GetBytes(value.Value, GetPrimitiveElementType(value.Value));
        else
            throw new InvalidOperationException("Cannot box a null value");

        var boxed = await BoxBytesAsync(targetType.GetClass(), targetType.GetTypeParameters(), data, context, handles);
        return CilValue.FromCorValue(boxed);
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
        var frame = debugger.GetILFrame(context.ThreadId, context.FrameDepth);
        return await debugger.FuncEval.GetStaticFieldValueAsync(type, field.Token, frame);
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
    // A boxed value unboxes to an exact primitive or runtime type match only, enum/underlying and interface matches are not accepted
    private static bool IsUnboxCompatible(ICorDebugValue boxedObject, ResolvedCilType targetType) {
        var unwrapped = boxedObject.UnwrapDebugValue();
        if (targetType.Primitive != null) {
            var expectedElementType = GetPrimitiveElementType(targetType.Primitive);
            return unwrapped is ICorDebugGenericValue generic && expectedElementType != null && generic.GetElementType() == expectedElementType;
        }
        if (targetType.RuntimeType != null) {
            var exactType = boxedObject.GetExactType();
            if (exactType.GetElementType() is not (CorElementType.VALUETYPE or CorElementType.CLASS))
                return false;
            var targetClass = targetType.RuntimeType.Class;
            return exactType.GetClass().GetToken() == targetClass.GetToken()
                && exactType.GetClass().GetModule().GetBaseAddress() == targetClass.GetModule().GetBaseAddress();
        }
        return false;
    }
    private static bool IsReferenceType(ResolvedCilType type) {
        if (type.Primitive is PrimitiveTypeCode.String or PrimitiveTypeCode.Object || type.ElementType != null)
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

    private static CilValue EvaluateBinary(OpCode op, CilValue left, CilValue right) {
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
    private static bool Compare(OpCode op, CilValue left, CilValue right) {
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
