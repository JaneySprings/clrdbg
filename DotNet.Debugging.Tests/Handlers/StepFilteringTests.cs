using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class StepFilteringTests : BaseDebugTestFixture {
    public StepFilteringTests() : base(nameof(StepFilteringTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var size = new Size(3, 4);
        var width = size.Width; // marker:getProperty
        var doubled = Double(width); // marker:afterGet
        size.Width = doubled; // marker:setProperty
        var sum = size + size; // marker:operator
        var area = size.Area; // marker:getComputedProperty
        var max = MaxArea(size.Width, size.Height); // marker:methodWithPropertyArgs
        var wrapped = WrappedValue(); // marker:stepThrough
        var plain = NonUserValue(); // marker:nonUserCode
        Console.WriteLine(sum.Width + area + max + wrapped + doubled + plain); // marker:end

        static int Double(int value) {
            return value * 2; // marker:insideDouble
        }
        static int MaxArea(int width, int height) { // marker:maxAreaHeader
            return width * height; // marker:insideMaxArea
        }
        [System.Diagnostics.DebuggerStepThrough]
        static int WrappedValue() {
            return Inner();
        }
        [System.Diagnostics.DebuggerNonUserCode]
        static int NonUserValue() { // marker:nonUserHeader
            return Inner();
        }
        static int Inner() { // marker:innerHeader
            return 21; // marker:insideInner
        }

        class Size {
            private int width;
            private int height;

            public int Width {
                get { return width; } // marker:insideGetter
                set { width = value; } // marker:insideSetter
            }
            public int Height {
                get { return height; }
            }
            public int Area {
                get { return Compute(width, height); } // marker:insideComputedGetter
            }

            public Size(int width, int height) {
                this.width = width;
                this.height = height;
            }

            public static Size operator +(Size left, Size right) { // marker:operatorHeader
                return new Size(left.width + right.width, left.height + right.height); // marker:insideOperator
            }

            private static int Compute(int width, int height) {
                return width * height; // marker:insideCompute
            }
        }
        """;
    }

    private int StopAtMarker(string marker, bool enableStepFiltering = true, bool justMyCode = true) {
        Launch(justMyCode: justMyCode, properties: new Dictionary<string, JToken> { ["enableStepFiltering"] = enableStepFiltering });
        SetBreakpoints(GetMarkerLine(marker));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        return stopped.ThreadId!.Value;
    }
    private StackFrame StepIn(int threadId) {
        Host.SendRequestSync(new StepInRequest() { ThreadId = threadId });
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        return GetTopStackFrame(stopped.ThreadId!.Value);
    }

    [Test]
    public void StepIntoPropertyGetterIsFilteredTest() {
        var threadId = StopAtMarker("marker:getProperty");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterGet")));
    }

    [Test]
    public void StepIntoPropertyGetterWhenFilteringDisabledTest() {
        var threadId = StopAtMarker("marker:getProperty", enableStepFiltering: false);
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("get_Width"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:insideGetter")));
    }

    [Test]
    public void StepIntoPropertySetterIsFilteredTest() {
        var threadId = StopAtMarker("marker:setProperty");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:operator")));
    }

    [Test]
    public void StepIntoOperatorIsFilteredTest() {
        var threadId = StopAtMarker("marker:operator");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:getComputedProperty")));
    }

    [Test]
    public void StepIntoOperatorWhenFilteringDisabledTest() {
        var threadId = StopAtMarker("marker:operator", enableStepFiltering: false);
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("op_Addition"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:operatorHeader")));
    }

    // The whole accessor is stepped over, including the user method its body calls
    [Test]
    public void StepIntoComputedPropertySkipsItsCallsTest() {
        var threadId = StopAtMarker("marker:getComputedProperty");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:methodWithPropertyArgs")));
    }

    // Property arguments are skipped, the step lands in the called method itself
    [Test]
    public void StepIntoMethodSkipsPropertyArgumentsTest() {
        var threadId = StopAtMarker("marker:methodWithPropertyArgs");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("MaxArea"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:maxAreaHeader")));
    }

    // A [DebuggerStepThrough] method is stepped through into the user code it calls
    [Test]
    public void StepIntoStepThroughMethodLandsInItsCalleeTest() {
        var threadId = StopAtMarker("marker:stepThrough");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Inner"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:innerHeader")));
    }

    // [DebuggerNonUserCode] counts as non-user only while Just My Code is on, the way vsdbg has it
    [Test]
    public void StepIntoNonUserCodeMethodIsSteppedThroughTest() {
        var threadId = StopAtMarker("marker:nonUserCode");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Inner"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:innerHeader")));
    }

    [Test]
    public void StepIntoNonUserCodeMethodWithJustMyCodeOffTest() {
        var threadId = StopAtMarker("marker:nonUserCode", justMyCode: false);
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("NonUserValue"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:nonUserHeader")));
    }

    [Test]
    public void NextOverPropertyLineTest() {
        var threadId = StopAtMarker("marker:getProperty");
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterGet")));
    }
}
