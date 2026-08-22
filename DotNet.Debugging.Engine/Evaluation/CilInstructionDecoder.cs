using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;

namespace DotNet.Debugging.Engine.Evaluation;

internal class CilInstruction {
    public int Offset { get; }
    public OpCode OpCode { get; }
    public object? Operand { get; }

    public CilInstruction(int offset, OpCode opCode, object? operand) {
        Offset = offset;
        OpCode = opCode;
        Operand = operand;
    }
}

internal class DecodedMethod {
    public List<CilInstruction> Instructions { get; }
    // Instruction index by IL offset, for branch targets
    public Dictionary<int, int> Offsets { get; }

    public DecodedMethod(List<CilInstruction> instructions) {
        Instructions = instructions;
        Offsets = new Dictionary<int, int>();
        for (var i = 0; i < instructions.Count; i++)
            Offsets[instructions[i].Offset] = i;
    }
}

internal static class CilInstructionDecoder {
    private static readonly Dictionary<short, OpCode> opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(it => it.FieldType == typeof(OpCode))
        .Select(it => (OpCode)it.GetValue(null)!)
        .ToDictionary(it => it.Value);

    public static DecodedMethod Decode(byte[] il) {
        var instructions = new List<CilInstruction>();
        var position = 0;
        while (position < il.Length) {
            var offset = position;
            var value = il[position++] == 0xfe
                ? unchecked((short)(0xfe00 | il[position++]))
                : (short)il[offset];
            if (!opCodes.TryGetValue(value, out var opCode))
                throw new NotSupportedException($"Unknown CIL opcode 0x{value:X4} at IL_{offset:X4}");

            var operand = ReadOperand(opCode.OperandType, il, ref position);
            instructions.Add(new CilInstruction(offset, opCode, operand));
        }
        return new DecodedMethod(instructions);
    }

    private static object? ReadOperand(OperandType operandType, byte[] il, ref int position) {
        switch (operandType) {
            case OperandType.InlineNone:
                return null;
            case OperandType.ShortInlineI:
                return unchecked((sbyte)il[position++]);
            case OperandType.InlineI:
                return ReadInt32(il, ref position);
            case OperandType.InlineI8:
                return ReadInt64(il, ref position);
            case OperandType.ShortInlineR:
                return BitConverter.Int32BitsToSingle(ReadInt32(il, ref position));
            case OperandType.InlineR:
                return BitConverter.Int64BitsToDouble(ReadInt64(il, ref position));
            case OperandType.ShortInlineVar:
                return (int)il[position++];
            case OperandType.InlineVar:
                return (int)ReadUInt16(il, ref position);
            case OperandType.ShortInlineBrTarget:
                var shortDelta = unchecked((sbyte)il[position++]);
                return position + shortDelta;
            case OperandType.InlineBrTarget:
                var delta = ReadInt32(il, ref position);
                return position + delta;
            case OperandType.InlineSwitch:
                return ReadSwitchTargets(il, ref position);
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
                return ReadInt32(il, ref position);
            default:
                throw new NotSupportedException($"CIL operand type '{operandType}' is not supported");
        }
    }
    private static int ReadInt32(byte[] bytes, ref int position) {
        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
        position += 4;
        return value;
    }
    private static long ReadInt64(byte[] bytes, ref int position) {
        var value = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(position, 8));
        position += 8;
        return value;
    }
    private static ushort ReadUInt16(byte[] bytes, ref int position) {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position, 2));
        position += 2;
        return value;
    }
    private static int[] ReadSwitchTargets(byte[] bytes, ref int position) {
        var count = ReadInt32(bytes, ref position);
        var targets = new int[count];
        for (var i = 0; i < count; i++)
            targets[i] = ReadInt32(bytes, ref position);
        // Targets are relative to the instruction following the switch
        for (var i = 0; i < count; i++)
            targets[i] += position;
        return targets;
    }
}
