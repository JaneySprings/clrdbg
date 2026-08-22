using DotNet.Debugging.Engine.Breakpoints;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class HitConditionTests {
    [TestCase("3", 3, true)]
    [TestCase("3", 2, false)]
    [TestCase("== 3", 3, true)]
    [TestCase("==3", 4, false)]
    [TestCase(">= 3", 3, true)]
    [TestCase(">=3", 2, false)]
    [TestCase("> 3", 4, true)]
    [TestCase(">3", 3, false)]
    [TestCase("<= 3", 3, true)]
    [TestCase("<=3", 4, false)]
    [TestCase("< 3", 2, true)]
    [TestCase("<3", 3, false)]
    [TestCase("% 3", 6, true)]
    [TestCase("%3", 4, false)]
    [TestCase("%0", 4, false)]
    [TestCase("abc", 1, false)]
    public void CheckHitConditionTest(string condition, int hitCount, bool expected) {
        Assert.That(BreakpointManager.CheckHitCondition(hitCount, condition), Is.EqualTo(expected));
    }
}
