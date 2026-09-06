using System.Reflection.Emit;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Engine.Extensions;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class OpCodeExtensionsTests {
    private static bool Branch(OpCode op, object left, object right) {
        return op.EvaluateBranch(CilValue.FromPrimitive(left), CilValue.FromPrimitive(right));
    }

    // ECMA-335 III.3: the ordered branches never take a NaN, the '.un' forms always do
    [Test]
    public void UnorderedOperandsTest() {
        var nan = double.NaN;
        Assert.Multiple(() => {
            Assert.That(Branch(OpCodes.Bge, nan, 0.0), Is.False);
            Assert.That(Branch(OpCodes.Ble, nan, 0.0), Is.False);
            Assert.That(Branch(OpCodes.Bgt, nan, 0.0), Is.False);
            Assert.That(Branch(OpCodes.Blt, nan, 0.0), Is.False);
            Assert.That(Branch(OpCodes.Beq, nan, nan), Is.False);
            Assert.That(Branch(OpCodes.Bge_Un, nan, 0.0), Is.True);
            Assert.That(Branch(OpCodes.Ble_Un, nan, 0.0), Is.True);
            Assert.That(Branch(OpCodes.Bgt_Un, nan, 0.0), Is.True);
            Assert.That(Branch(OpCodes.Blt_Un, nan, 0.0), Is.True);
            Assert.That(Branch(OpCodes.Bne_Un, nan, nan), Is.True);
        });
    }

    [Test]
    public void OrderedOperandsTest() {
        Assert.Multiple(() => {
            Assert.That(Branch(OpCodes.Bge, 1.0, 0.0), Is.True);
            Assert.That(Branch(OpCodes.Ble, 1.0, 0.0), Is.False);
            Assert.That(Branch(OpCodes.Bge, -1, 0), Is.False);
            Assert.That(Branch(OpCodes.Ble, -1, 0), Is.True);
            // For integers '.un' means unsigned: -1 is the largest value
            Assert.That(Branch(OpCodes.Bge_Un, -1, 0), Is.True);
            Assert.That(Branch(OpCodes.Ble_Un, -1, 0), Is.False);
        });
    }

    [Test]
    public void CheckedConversionsTest() {
        Assert.Throws<OverflowException>(() => OpCodes.Conv_Ovf_U8.Convert(CilValue.FromPrimitive(-1L)));
        Assert.Throws<OverflowException>(() => OpCodes.Conv_Ovf_U.Convert(CilValue.FromPrimitive(-1)));
        Assert.That(OpCodes.Conv_Ovf_U8.Convert(CilValue.FromPrimitive(5L)).AsUInt64(), Is.EqualTo(5UL));
        Assert.That(OpCodes.Conv_Ovf_I.Convert(CilValue.FromPrimitive(5L)).AsInt64(), Is.EqualTo(5L));
        Assert.That(OpCodes.Conv_Ovf_I.Convert(CilValue.FromPrimitive(-2.9)).AsInt64(), Is.EqualTo(-2L));
    }
}
