using System.Reflection.Emit;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Evaluation;

namespace DotNet.Debugging.Engine.Extensions;

// What the CIL opcodes do to the interpreter's host values: the constants and slots they encode, the arithmetic,
// comparisons and conversions that run on the host
internal static class OpCodeExtensions {
    public static bool TryGetConstant(this OpCode op, object? operand, out CilValue value) {
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
    public static bool TryGetSlotIndex(this OpCode op, object? operand, OpCode shortFormZero, OpCode longForm, OpCode shortOperandForm, out int index) {
        index = -1;
        if (op.Value >= shortFormZero.Value && op.Value < shortFormZero.Value + 4)
            index = op.Value - shortFormZero.Value;
        else if (op == longForm || op == shortOperandForm)
            index = (int)operand!;
        return index >= 0;
    }
    public static bool IsPrefixed(this OpCode op, string prefix) {
        return op.Name?.StartsWith(prefix, StringComparison.Ordinal) == true;
    }
    public static bool IsBinaryOperation(this OpCode op) {
        return op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Mul
            || op == OpCodes.Add_Ovf || op == OpCodes.Add_Ovf_Un || op == OpCodes.Sub_Ovf || op == OpCodes.Sub_Ovf_Un
            || op == OpCodes.Mul_Ovf || op == OpCodes.Mul_Ovf_Un
            || op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem || op == OpCodes.Rem_Un
            || op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor || op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un;
    }
    public static bool IsConversion(this OpCode op) {
        return op.IsPrefixed("conv.");
    }
    public static bool IsComparisonBranch(this OpCode op) {
        return op.FlowControl == FlowControl.Cond_Branch
            && op != OpCodes.Brtrue && op != OpCodes.Brtrue_S && op != OpCodes.Brfalse && op != OpCodes.Brfalse_S && op != OpCodes.Switch;
    }

    public static CilValue EvaluateBinary(this OpCode op, CilValue left, CilValue right) {
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
    public static bool Compare(this OpCode op, CilValue left, CilValue right) {
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
    public static bool EvaluateBranch(this OpCode op, CilValue left, CilValue right) {
        if (op == OpCodes.Beq || op == OpCodes.Beq_S)
            return OpCodes.Ceq.Compare(left, right);
        if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S)
            return !OpCodes.Ceq.Compare(left, right);
        if (op == OpCodes.Bgt || op == OpCodes.Bgt_S)
            return OpCodes.Cgt.Compare(left, right);
        if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
            return OpCodes.Cgt_Un.Compare(left, right);
        if (op == OpCodes.Blt || op == OpCodes.Blt_S)
            return OpCodes.Clt.Compare(left, right);
        if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S)
            return OpCodes.Clt_Un.Compare(left, right);
        // 'bge' is the negation of 'clt.un' and 'bge.un' that of 'clt' (ECMA-335 III.3): for integers '.un' means an
        // unsigned comparison, for floats it means 'or unordered', so a NaN operand fails every ordered branch
        var isFloat = left.Value is float or double || right.Value is float or double;
        if (op == OpCodes.Bge || op == OpCodes.Bge_S)
            return isFloat ? !OpCodes.Clt_Un.Compare(left, right) : !OpCodes.Clt.Compare(left, right);
        if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S)
            return isFloat ? !OpCodes.Clt.Compare(left, right) : !OpCodes.Clt_Un.Compare(left, right);
        if (op == OpCodes.Ble || op == OpCodes.Ble_S)
            return isFloat ? !OpCodes.Cgt_Un.Compare(left, right) : !OpCodes.Cgt.Compare(left, right);
        if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S)
            return isFloat ? !OpCodes.Cgt.Compare(left, right) : !OpCodes.Cgt_Un.Compare(left, right);
        throw new NotSupportedException($"Conditional branch '{op.Name}' is not supported");
    }
    public static CilValue Convert(this OpCode op, CilValue value) {
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
            return CilValue.FromPrimitive(isFloat ? checked((ulong)value.AsFloat()) : checked((ulong)signed));
        if (op == OpCodes.Conv_Ovf_I) {
            if (isFloat)
                return CilValue.FromPrimitive(IntPtr.Size == 8 ? checked((long)value.AsFloat()) : checked((int)value.AsFloat()));
            return CilValue.FromPrimitive(IntPtr.Size == 8 ? signed : checked((int)signed));
        }
        if (op == OpCodes.Conv_Ovf_U) {
            if (isFloat)
                return CilValue.FromPrimitive(IntPtr.Size == 8 ? checked((ulong)value.AsFloat()) : checked((uint)value.AsFloat()));
            return CilValue.FromPrimitive(IntPtr.Size == 8 ? checked((ulong)signed) : checked((uint)signed));
        }
        throw new NotSupportedException($"Conversion opcode '{op.Name}' is not supported yet");
    }
}
