using System.Diagnostics;
using DotNet.Debugging.Engine.Models.Response;
using DotNet.Debugging.Engine.PresentationHintModels;
using DotNet.Debugging.CorApi;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private async Task AddLocalVariables(ModuleInfo module, ICorDebugFunction corDebugFunction, List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue? classContainingHoistedLocalsValue) {
        if (classContainingHoistedLocalsValue is not null) {
            // If we have a classContainingHoistedLocalsValue, it means captured variables from the outer scope are stored
            // as fields on the compiler-generated closure class - read those first, walking the full closure chain
            // so that variables captured from enclosing lambdas are also included.
            // We do NOT return here: non-captured locals declared inside the lambda body are still plain IL locals
            // on the lambda method frame and must also be read below.
            await AddClosureChainMembers(classContainingHoistedLocalsValue, threadId, stackDepth, result);
        }
        var corDebugIlFrame = GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth);
        if (corDebugIlFrame.GetLocalVariables().Length is 0) return;
        var currentIlOffset = corDebugIlFrame.GetIP().pnOffset;
        foreach (var (index, localVariableCorDebugValue) in corDebugIlFrame.GetLocalVariables().Index()) {
            var localVariableName = module.MetadataReader.GetLocalVariableName(corDebugFunction.GetToken(), index, currentIlOffset);
            if (localVariableName is null) continue; // Compiler generated locals will not be found. E.g. DefaultInterpolatedStringHandler
            await WithFailureHandling(result, localVariableName, async () => {
                var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(localVariableCorDebugValue, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : new VariablePresentationHint { Kind = PresentationHintKind.Data };
                result.Add(new VariableInfo {
                    Name = localVariableName,
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = variablePresentationHint,
                    EvaluateName = localVariableName,
                    VariablesReference = GetVariablesReference(localVariableCorDebugValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, localVariableName)
                });
            });
        }
    }

    /// Walks the compiler-generated closure chain starting at <paramref name="closureValue"/>,
    /// calling AddMembers on each closure class. Parent closures are linked via a field of
    /// kind <see cref="GeneratedNameKind.DisplayClassLocalOrField"/> (e.g. "&lt;&gt;8__1").
    private async Task AddClosureChainMembers(ICorDebugValue closureValue, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result) {
        await AddMembers(closureValue, closureValue.GetExactType(), threadId, stackDepth, result);

        // Follow the DisplayClassLocalOrField link to the parent closure, if any
        var objectValue = closureValue.UnwrapDebugValueToObject();
        var metadataImport = objectValue.GetClass().GetModule().GetMetaDataInterface<IMetaDataImport>();
        var fields = metadataImport.EnumFields(objectValue.GetClass().GetToken());
        foreach (var field in fields) {
            var fieldProps = metadataImport.GetFieldProps(field);
            if (GeneratedNameParser.GetKind(fieldProps.szField) is GeneratedNameKind.DisplayClassLocalOrField) {
                var parentClosureValue = objectValue.GetFieldValue(objectValue.GetClass(), field);
                await AddClosureChainMembers(parentClosureValue, threadId, stackDepth, result);
                break; // only one parent link per closure class
            }
        }
    }

    /// Returns classContainingHoistedLocalsValue if applicable
    private async Task<ICorDebugValue?> AddArguments(ModuleInfo module, ICorDebugFunction corDebugFunction, List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth) {
        var corDebugIlFrame = GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth);
        var arguments = corDebugIlFrame.GetArguments();
        if (arguments.Length is 0) return null;
        var metadataImport = module.Module.GetMetaDataInterface<IMetaDataImport>();

        // localsScope.Frame.Arguments includes the implicit "this" parameter for instance methods,
        // but GetParamForMethodIndex does NOT include it - it is named by convention
        // so we need to check the method attributes to see if it's static or instance, to conditionally handle "this"
        var methodProps = metadataImport!.GetMethodProps(corDebugFunction.GetToken());
        var isStatic = methodProps.pdwAttr.IsMdStatic();
        ICorDebugValue? classContainingHoistedLocalsValue = null;
        if (isStatic is false) {
            var methodName = methodProps.szMethod;
            var implicitThisValue = arguments[0];
            if (methodName is "MoveNext" || methodName.Contains(">b")) // async or lambda
            {
                var containingClassName = metadataImport.GetTypeDefProps(corDebugFunction.GetClass().GetToken()).szTypeDef;
                var classGeneratedNameKind = GeneratedNameParser.GetKind(containingClassName);
                if (classGeneratedNameKind is GeneratedNameKind.StateMachineType or GeneratedNameKind.LambdaDisplayClass) {
                    // In this case, 'this' is actually a compiler generated class that contains a field pointing to the 'this' that the user expects
                    // We are also going to use this to decide that the containing class contains hoisted locals, so we should return it
                    classContainingHoistedLocalsValue = implicitThisValue;
                    // This may return null, as even though we have checked isStatic is true, that is for the MoveNext method - the user's method may be static, and therefore would have no 'this' proxy field
                    implicitThisValue = GetAsyncOrLambdaProxyFieldValue(implicitThisValue, metadataImport);
                }
            }
            if (implicitThisValue is not null) {
                await WithFailureHandling(result, "this", async () => {
                    var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(implicitThisValue, threadId, stackDepth, true);
                    VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : new VariablePresentationHint { Kind = PresentationHintKind.Data };
                    result.Add(new VariableInfo {
                        Name = "this", // Hardcoded - 'this' has no metadata
                        Value = value,
                        Type = friendlyTypeName,
                        PresentationHint = variablePresentationHint,
                        EvaluateName = "this",
                        VariablesReference = GetVariablesReference(implicitThisValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, "this")
                    });
                });
            }
        }
        var skipCount = isStatic ? 0 : 1; // Skip 'this' for instance methods, as we already handled it
        foreach (var (index, argumentCorDebugValue) in arguments.Skip(skipCount).Index()) {
            // index 0 is the return value, so we add 1 to get to the arguments
            // GetParamForMethodIndex does not include the instance 'this' parameter
            var paramDef = metadataImport!.GetParamForMethodIndex(corDebugFunction.GetToken(), index + 1);
            var paramProps = metadataImport.GetParamProps(paramDef);
            var argumentName = paramProps.szName;
            if (argumentName is null) continue;
            await WithFailureHandling(result, argumentName, async () => {
                var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(argumentCorDebugValue, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : new VariablePresentationHint { Kind = PresentationHintKind.Data };
                result.Add(new VariableInfo {
                    Name = argumentName,
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = variablePresentationHint,
                    EvaluateName = argumentName,
                    VariablesReference = GetVariablesReference(argumentCorDebugValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, argumentName)
                });
            });
        }
        return classContainingHoistedLocalsValue;
    }

    private async Task AddCurrentException(List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth) {
        var thread = _threads.GetValueOrDefault(threadId.Value);
        ArgumentNullException.ThrowIfNull(thread);
        thread.TryGetCurrentException(out var currentException);
        if (currentException is not null) {
            await WithFailureHandling(result, "$exception", async () => {
                var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(currentException, threadId, stackDepth, true);
                VariablePresentationHint? presentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : new VariablePresentationHint { Kind = PresentationHintKind.Data };
                result.Add(new VariableInfo {
                    Name = "$exception",
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = presentationHint,
                    EvaluateName = "$exception",
                    VariablesReference = GetVariablesReference(currentException, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, "$exception")
                });
            });
        }
    }

    private int GetVariablesReference(ICorDebugValue corDebugValue, string friendlyTypeName, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue? debuggerProxyInstance, string? evaluateName = null) {
        var unwrappedDebugValue = corDebugValue.UnwrapDebugValue();
        if (unwrappedDebugValue is ICorDebugArrayValue arrayValue) {
            if (arrayValue.GetCount() is 0) return 0;
            return GenerateUniqueVariableReference(corDebugValue, threadId, stackDepth, debuggerProxyInstance, evaluateName);
        }
        else if (unwrappedDebugValue is ICorDebugObjectValue objectValue) {
            var isNullableStruct = friendlyTypeName.EndsWith('?');
            if (isNullableStruct) {
                var underlyingValueOrNull = GetUnderlyingValueOrNullFromNullableStruct(objectValue);
                if (underlyingValueOrNull is null) return 0;
                if (underlyingValueOrNull is not ICorDebugObjectValue objValue) return 0; // underlying value is primitive
                objectValue = objValue;
            }

            var type = objectValue.GetElementType();
            // Strings are objects but typically displayed as primitives
            if (type is CorElementType.STRING) return 0;
            // Decimal is a struct but should be treated as a primitive
            if (friendlyTypeName is "decimal" or "decimal?") return 0;
            // a boxed primitive is CorElementType.VALUETYPE but should be displayed as a primitive. They can never be nullable.
            if (friendlyTypeName is "bool" or "byte" or "sbyte" or "char" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double" or "nint" or "nuint") return 0;
            if (type is CorElementType.CLASS or CorElementType.VALUETYPE or CorElementType.SZARRAY or CorElementType.ARRAY) {
                return GenerateUniqueVariableReference(corDebugValue, threadId, stackDepth, debuggerProxyInstance, evaluateName);
            }
        }
        return 0;
    }

    private int GenerateUniqueVariableReference(ICorDebugValue value, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue? debuggerProxyInstance, string? evaluateName) {
        var variablesReference = new VariablesReference(StoredReferenceKind.StackVariable, value, threadId, stackDepth, debuggerProxyInstance, evaluateName);
        var reference = _variableManager.CreateReference(variablesReference);
        return reference;
    }

    private async Task AddMembersAndStaticPseudoVariable(ICorDebugValue corDebugValue, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, string? parentEvaluateName, bool includeNonPublicGroup = true) {
        // User code types show all their members inline, only non user (library) types get the 'Non-Public members' group
        var visibility = includeNonPublicGroup && IsUserCodeType(corDebugType) ? MemberVisibility.All : MemberVisibility.Public;
        var (hasStaticMembers, hasNonPublicMembers) = await AddMembers(corDebugValue, corDebugType, threadId, stackDepth, result, visibility, parentEvaluateName);
        if (hasStaticMembers) {
            var variableInfo = new VariableInfo {
                Name = "Static members",
                Value = "",
                Type = "",
                PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                VariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.StaticClassVariable, corDebugValue, threadId, stackDepth, null, parentEvaluateName))
            };
            result.Add(variableInfo);
        }
        if (includeNonPublicGroup && hasNonPublicMembers) {
            var variableInfo = new VariableInfo {
                Name = "Non-Public members",
                Value = "",
                Type = "",
                PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                VariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.NonPublicStackVariable, corDebugValue, threadId, stackDepth, null, parentEvaluateName))
            };
            result.Add(variableInfo);
        }
    }

    /// Returns bools indicating if the 'Static members' and 'Non-Public members' pseudo variables are required
    private async Task<(bool HasStaticMembers, bool HasNonPublicMembers)> AddMembers(ICorDebugValue corDebugValue, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, MemberVisibility visibility = MemberVisibility.All, string? parentEvaluateName = null) {
        var corDebugClass = corDebugType.GetClass();
        var module = corDebugClass.GetModule();
        var typeToken = corDebugClass.GetToken();
        var metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
        var instanceFieldDefs = metadataImport.EnumFields(typeToken).Where(f => f.IsStatic(metadataImport) is false).ToArray();
        var instanceProperties = metadataImport.EnumProperties(typeToken).Where(p => p.IsStatic(metadataImport) is false).ToArray();
        var hasStaticMembers = metadataImport.EnumFields(typeToken).Any(f => f.IsStatic(metadataImport))
            || metadataImport.EnumProperties(typeToken).Any(p => p.IsStatic(metadataImport));
        var hasNonPublicMembers = visibility is MemberVisibility.Public && (
            instanceFieldDefs.Any(f => f.MatchesVisibility(metadataImport, MemberVisibility.NonPublic) && f.IsDisplayable(metadataImport))
            || instanceProperties.Any(p => p.MatchesVisibility(metadataImport, MemberVisibility.NonPublic)));

        var visibleFieldDefs = instanceFieldDefs.Where(f => f.MatchesVisibility(metadataImport, visibility)).ToArray();
        var visibleProperties = instanceProperties.Where(p => p.MatchesVisibility(metadataImport, visibility)).ToArray();

        await AddFields(visibleFieldDefs, metadataImport, corDebugType, corDebugValue, result, threadId, stackDepth, parentEvaluateName);
        // We need to pass the un-unwrapped reference value here, as we need to invoke CallParameterizedFunction with the correct parameters
        await AddProperties(visibleProperties, metadataImport, corDebugType, threadId, stackDepth, corDebugValue, result, parentEvaluateName);

        // Handle members on base types recursively
        var baseType = corDebugType.GetBase();
        if (baseType is null) return (hasStaticMembers, hasNonPublicMembers);
        var baseTypeName = GetCorDebugTypeFriendlyName(baseType);
        if (baseTypeName is "System.Object" or "System.ValueType" or "System.Enum") return (hasStaticMembers, hasNonPublicMembers);
        var baseResult = await AddMembers(corDebugValue, baseType, threadId, stackDepth, result, visibility, parentEvaluateName);
        return (hasStaticMembers | baseResult.HasStaticMembers, hasNonPublicMembers | baseResult.HasNonPublicMembers);
    }

    /// Returns a bool indicating if the 'Non-Public members' pseudo variable is required
    private async Task<bool> AddStaticMembers(ICorDebugValue corDebugValue, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, MemberVisibility visibility = MemberVisibility.All, string? parentEvaluateName = null) {
        var corDebugClass = corDebugType.GetClass();
        var module = corDebugClass.GetModule();
        var typeToken = corDebugClass.GetToken();
        var metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
        var staticFieldDefs = metadataImport.EnumFields(typeToken).Where(s => s.IsStatic(metadataImport)).ToArray();
        var staticProperties = metadataImport.EnumProperties(typeToken).Where(s => s.IsStatic(metadataImport)).ToArray();
        var hasNonPublicMembers = visibility is MemberVisibility.Public && (
            staticFieldDefs.Any(f => f.MatchesVisibility(metadataImport, MemberVisibility.NonPublic) && f.IsDisplayable(metadataImport))
            || staticProperties.Any(p => p.MatchesVisibility(metadataImport, MemberVisibility.NonPublic)));

        var visibleStaticFieldDefs = staticFieldDefs.Where(f => f.MatchesVisibility(metadataImport, visibility)).ToArray();
        var visibleStaticProperties = staticProperties.Where(p => p.MatchesVisibility(metadataImport, visibility)).ToArray();

        await AddFields(visibleStaticFieldDefs, metadataImport, corDebugType, corDebugValue, result, threadId, stackDepth, parentEvaluateName);
        // We need to pass the un-unwrapped reference value here, as we need to invoke CallParameterizedFunction with the correct parameters
        await AddProperties(visibleStaticProperties, metadataImport, corDebugType, threadId, stackDepth, corDebugValue, result, parentEvaluateName);

        // Handle members on base types recursively
        var baseType = corDebugType.GetBase();
        if (baseType is null) return hasNonPublicMembers;
        var baseTypeName = GetCorDebugTypeFriendlyName(baseType);
        if (baseTypeName is "System.Object" or "System.ValueType" or "System.Enum") return hasNonPublicMembers;
        return hasNonPublicMembers | await AddStaticMembers(corDebugValue, baseType, threadId, stackDepth, result, visibility, parentEvaluateName);
    }

    private bool IsUserCodeType(ICorDebugType corDebugType) {
        try {
            var corModule = corDebugType.GetClass().GetModule();
            return _modules.TryGetValue(corModule.GetBaseAddress(), out var moduleInfo) && moduleInfo.IsUserCode;
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Orders a member listing in ordinal order (i.e. 'AAA AAB ... aaa aab'), with the pseudo groups at the end
    /// </summary>
    private static void SortMembers(List<VariableInfo> members) {
        members.Sort((left, right) => {
            var rankComparison = GetMemberRank(left).CompareTo(GetMemberRank(right));
            if (rankComparison is not 0) return rankComparison;
            return string.CompareOrdinal(left.Name, right.Name);
        });
    }

    private static int GetMemberRank(VariableInfo member) => member.Name switch {
        "Static members" => 1,
        "Non-Public members" => 2,
        "Raw View" => 3,
        _ => 0
    };

    /// <summary>
    /// 'parent.Member' for instance members, 'Namespace.Type.Member' for static ones (vsdbg's form), the bare name for hoisted locals
    /// </summary>
    private static string GetMemberEvaluateName(string memberName, bool isStatic, string? parentEvaluateName, ICorDebugType declaringType) {
        if (isStatic) {
            try {
                return $"{GetCorDebugTypeFriendlyName(declaringType)}.{memberName}";
            }
            catch {
                // Fall through to the instance form
            }
        }
        return parentEvaluateName is null ? memberName : $"{parentEvaluateName}.{memberName}";
    }

    private static PresentationHintVisibility GetVisibility(CorFieldAttr attributes) => (attributes & CorFieldAttr.fdFieldAccessMask) switch {
        CorFieldAttr.fdPublic => PresentationHintVisibility.Public,
        CorFieldAttr.fdFamily or CorFieldAttr.fdFamORAssem => PresentationHintVisibility.Protected,
        CorFieldAttr.fdAssembly or CorFieldAttr.fdFamANDAssem => PresentationHintVisibility.Internal,
        _ => PresentationHintVisibility.Private
    };

    private static PresentationHintVisibility GetVisibility(CorMethodAttr attributes) => (attributes & CorMethodAttr.mdMemberAccessMask) switch {
        CorMethodAttr.mdPublic => PresentationHintVisibility.Public,
        CorMethodAttr.mdFamily or CorMethodAttr.mdFamORAssem => PresentationHintVisibility.Protected,
        CorMethodAttr.mdAssem or CorMethodAttr.mdFamANDAssem => PresentationHintVisibility.Internal,
        _ => PresentationHintVisibility.Private
    };

    private async Task AddFields(FieldDefToken[] fieldTokens, IMetaDataImport metadataImport, ICorDebugType corDebugType, ICorDebugValue corDebugValue, List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth, string? parentEvaluateName) {
        var corDebugClass = corDebugType.GetClass();
        foreach (var fieldToken in fieldTokens) {
            var fieldProps = metadataImport.GetFieldProps(fieldToken);
            var fieldName = fieldProps.szField;
            if (fieldName is null) continue;
            await WithFailureHandling(result, fieldName, async () => {
                GeneratedNameParser.TryParseGeneratedName(fieldName, out var generatedNameKind, out var openBracketOffset, out var closeBracketOffset);
                if (generatedNameKind is GeneratedNameKind.HoistedLocalField) {
                    // e.g. we are in an async method - local variables in the user's method are stored in fields on a generated class, e.g. "<intVar>5__1"
                    // we want to extract "intVar"
                    var originalLocalVariableName = fieldName.AsSpan()[(openBracketOffset + 1)..closeBracketOffset];
                    fieldName = originalLocalVariableName.ToString();
                }
                else if (generatedNameKind is not GeneratedNameKind.None) {
                    return;
                }
                var isStatic = fieldProps.pdwAttr.IsFdStatic();
                var isLiteral = fieldProps.pdwAttr.IsFdLiteral();
                var fieldVisibility = GetVisibility(fieldProps.pdwAttr);
                var fieldEvaluateName = GetMemberEvaluateName(fieldName, isStatic, parentEvaluateName, corDebugType);
                var debuggerBrowsableRootHidden = false;
                var hasDebuggerBrowsableAttribute = metadataImport.TryGetCustomAttributeByName(fieldToken, "System.Diagnostics.DebuggerBrowsableAttribute", out var debuggerBrowsableAttributePointer, out var debuggerBrowsableAttributeSize) is Cor.S_OK;
                if (hasDebuggerBrowsableAttribute) {
                    // https://github.com/Samsung/netcoredbg/blob/6476bc00c2beaab9255c750235a68de3a3d0cfae/src/debugger/evaluator.cpp#L913
                    var debuggerBrowsableState = (DebuggerBrowsableState)GetDebuggerBrowsableCustomAttributeResultInt(debuggerBrowsableAttributePointer, debuggerBrowsableAttributeSize);
                    if (debuggerBrowsableState == DebuggerBrowsableState.Never) return; // I may not end up doing this, as it would be ideal to still be able to hover the variable in the editor and see the value
                    if (debuggerBrowsableState == DebuggerBrowsableState.RootHidden) debuggerBrowsableRootHidden = true;
                }
                if (isLiteral) {
                    var literalValue = GetLiteralValue(fieldProps.ppValue, fieldProps.pdwCPlusTypeFlag);
                    var literalVariableInfo = new VariableInfo {
                        Name = fieldName,
                        Value = literalValue.ToString()!,
                        Type = GetFriendlyTypeName(fieldProps.pdwCPlusTypeFlag),
                        PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Data, Visibility = fieldVisibility },
                        EvaluateName = fieldEvaluateName,
                        VariablesReference = 0
                    };
                    result.Add(literalVariableInfo);
                    return;
                }

                var objectValue = corDebugValue.UnwrapDebugValueToObject();
                var fieldCorDebugValue = isStatic ? await corDebugType.GetStaticFieldValueAsync(ProcessRuntimeEventsUntilEvalEvent, EvalStatus, fieldToken, GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth)) : objectValue.GetFieldValue(corDebugClass, fieldToken);
                if (debuggerBrowsableRootHidden) {
                    var unwrappedDebugValue = fieldCorDebugValue.UnwrapDebugValue();
                    if (unwrappedDebugValue is ICorDebugArrayValue arrayValue) {
                        await AddArrayElements(arrayValue, threadId, stackDepth, result, fieldEvaluateName);
                        return;
                    }
                }
                var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(fieldCorDebugValue, threadId, stackDepth, true);
                var variablePresentationHint = new VariablePresentationHint {
                    Kind = PresentationHintKind.Data,
                    Attributes = resultIsError ? AttributesValue.FailedEvaluation : null,
                    Visibility = fieldVisibility
                };
                var variableInfo = new VariableInfo {
                    Name = fieldName,
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = variablePresentationHint,
                    EvaluateName = fieldEvaluateName,
                    VariablesReference = GetVariablesReference(fieldCorDebugValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, fieldEvaluateName)
                };
                result.Add(variableInfo);
            });
        }
    }

    internal class EvalException(string message) : Exception(message);
    private async Task AddProperties(PropertyToken[] propertyTokens, IMetaDataImport metadataImport, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue corDebugValue, List<VariableInfo> result, string? parentEvaluateName) {
        var corDebugClass = corDebugType.GetClass();
        foreach (var propertyToken in propertyTokens) {
            var propertyProps = metadataImport.GetPropertyProps(propertyToken);
            var propertyName = propertyProps.szProperty;
            if (propertyName is null) continue;
            await WithFailureHandling(result, propertyName, async () => {
                var variablesReferenceIlFrame = GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth);

                // Get the get method for the property
                var getMethodDef = propertyProps.pmdGetter;
                if (getMethodDef == 0) return; // No get method

                // Get method attributes to check if it's static
                var getterMethodProps = metadataImport.GetMethodProps(getMethodDef);
                var getterAttr = getterMethodProps.pdwAttr;

                var isStatic = getterAttr.IsMdStatic();
                var propertyVisibility = GetVisibility(getterAttr);
                var propertyEvaluateName = GetMemberEvaluateName(propertyName, isStatic, parentEvaluateName, corDebugType);

                var debuggerBrowsableRootHidden = false;
                var hasDebuggerBrowsableAttribute = metadataImport.TryGetCustomAttributeByName(propertyToken, "System.Diagnostics.DebuggerBrowsableAttribute", out var debuggerBrowsableAttributePointer, out var debuggerBrowsableAttributeSize) is Cor.S_OK;
                if (hasDebuggerBrowsableAttribute) {
                    // https://github.com/Samsung/netcoredbg/blob/6476bc00c2beaab9255c750235a68de3a3d0cfae/src/debugger/evaluator.cpp#L913
                    var debuggerBrowsableState = (DebuggerBrowsableState)GetDebuggerBrowsableCustomAttributeResultInt(debuggerBrowsableAttributePointer, debuggerBrowsableAttributeSize);
                    if (debuggerBrowsableState == DebuggerBrowsableState.Never) return; // I may not end up doing this, as it would be ideal to still be able to hover the variable in the editor and see the value
                    if (debuggerBrowsableState == DebuggerBrowsableState.RootHidden) debuggerBrowsableRootHidden = true;
                }

                var getMethod = corDebugClass.GetModule().GetFunctionFromToken(getMethodDef);
                var eval = variablesReferenceIlFrame.GetChain().GetThread().CreateEval();

                // May not be correct, will need further testing
                var parameterizedContainingType = corDebugValue.GetExactType();

                var typeParameterTypes = parameterizedContainingType.GetTypeParameters();

                // For instance properties, pass the object; for static, pass nothing
                ICorDebugValue[] corDebugValues = isStatic ? [] : [corDebugValue];

                var returnValue = await eval.CallParameterizedFunctionAsync(ProcessRuntimeEventsUntilEvalEvent, EvalStatus, getMethod, typeParameterTypes.Length, typeParameterTypes, corDebugValues.Length, corDebugValues);

                if (returnValue is null) return;
                var retainReturnValue = false;
                try {
                    if (debuggerBrowsableRootHidden) {
                        var unwrappedDebugValue = returnValue.UnwrapDebugValue();
                        if (unwrappedDebugValue is ICorDebugArrayValue arrayValue) {
                            await AddArrayElements(arrayValue, threadId, stackDepth, result, propertyEvaluateName);
                            return;
                        }
                    }
                    var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(returnValue, threadId, stackDepth, true);
                    var variablePresentationHint = new VariablePresentationHint {
                        Kind = PresentationHintKind.Property,
                        Attributes = resultIsError ? AttributesValue.FailedEvaluation : null,
                        Visibility = propertyVisibility
                    };
                    var variablesReference = GetVariablesReference(returnValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, propertyEvaluateName);
                    var variableInfo = new VariableInfo {
                        Name = propertyName,
                        Value = value,
                        Type = friendlyTypeName,
                        PresentationHint = variablePresentationHint,
                        EvaluateName = propertyEvaluateName,
                        VariablesReference = variablesReference
                    };
                    retainReturnValue = variablesReference != 0;
                    result.Add(variableInfo);
                }
                finally {
                    if (!retainReturnValue && returnValue is ICorDebugHandleValue handle) handle.TryDispose();
                }
            });
        }
    }

    private async Task AddArrayElements(ICorDebugArrayValue arrayValue, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, string? parentEvaluateName) {
        var rank = arrayValue.GetRank();
        if (rank > 1) throw new NotImplementedException("Multidimensional arrays not yet supported");
        var itemCount = arrayValue.GetCount();

        // Get the elements first, as the CorDebugArrayValue arrayValue may get neutered during 'await GetValueForCorDebugValueAsync' below, if any evals are required
        var elements = Enumerable.Range(0, itemCount).Select(i => arrayValue.GetElement(1, [checked((uint)i)])).ToArray();
        foreach (var (i, element) in elements.Index()) {
            var name = $"[{i}]";
            var elementEvaluateName = parentEvaluateName is null ? name : $"{parentEvaluateName}{name}";
            await WithFailureHandling(result, name, async () => {
                var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(element, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : new VariablePresentationHint { Kind = PresentationHintKind.Data };
                var variableReference = GetVariablesReference(element, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance, elementEvaluateName);
                var variableInfo = new VariableInfo {
                    Name = name,
                    Type = friendlyTypeName,
                    Value = value,
                    PresentationHint = variablePresentationHint,
                    EvaluateName = elementEvaluateName,
                    VariablesReference = variableReference
                };
                result.Add(variableInfo);
            });
        }
    }

    private static async Task WithFailureHandling(List<VariableInfo> result, string fieldName, Func<Task> func) {
        try {
            await func();
        }
        catch (Exception ex) {
            result.Add(new VariableInfo {
                Name = fieldName,
                Value = ex.Message,
                Type = null,
                PresentationHint = new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation },
                VariablesReference = 0
            });
        }
    }
}