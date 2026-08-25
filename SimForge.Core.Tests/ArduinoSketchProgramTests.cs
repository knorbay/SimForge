using SimForge.Core;
using Xunit;

namespace SimForge.Core.Tests;

public sealed class ArduinoSketchProgramTests
{
    [Fact]
    public void Analyze_RecognizesBlinkOnItsExactPin()
    {
        const string sketch = """
                              void setup() { pinMode(13, OUTPUT); }
                              void loop() {
                                digitalWrite(13, HIGH);
                                delay(750);
                                digitalWrite(13, LOW);
                                delay(750);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal(DigitalOutputMode.Blink, program.Outputs[13]);
        Assert.Equal(0.75, program.IntervalSeconds);
    }

    [Fact]
    public void Analyze_KeepsIndependentOutputModes()
    {
        const string sketch = """
                              void setup() { }
                              void loop() {
                                digitalWrite(7, HIGH);
                                digitalWrite(13, LOW);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal(DigitalOutputMode.High, program.Outputs[7]);
        Assert.Equal(DigitalOutputMode.Low, program.Outputs[13]);
    }

    [Fact]
    public void Analyze_RejectsSymbolicOutputPinWithClearDiagnostic()
    {
        const string sketch = """
                              void setup() { }
                              void loop() { digitalWrite(LED_BUILTIN, HIGH); }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.False(program.IsValid);
        Assert.Equal("Use a numeric output pin", program.Diagnostic);
    }

    [Fact]
    public void Analyze_RejectsUnbalancedBraces()
    {
        var program = ArduinoSketchProgram.Analyze("void setup() { } void loop() { digitalWrite(13, HIGH);");

        Assert.False(program.IsValid);
        Assert.Equal("Check braces", program.Diagnostic);
    }

    [Fact]
    public void Analyze_ClampsVeryShortDelay()
    {
        const string sketch = """
                              void setup() { }
                              void loop() { digitalWrite(13, HIGH); delay(1); }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.Equal(0.05, program.IntervalSeconds);
    }

    [Fact]
    public void Analyze_IgnoresWritesAndBracesInsideCommentsAndStrings()
    {
        const string sketch = """
                              // digitalWrite(7, HIGH); }
                              void setup() { }
                              void loop() {
                                const char* example = "digitalWrite(8, LOW); }";
                                /* digitalWrite(9, HIGH); { */
                                digitalWrite(13, LOW);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Single(program.Outputs);
        Assert.Equal(DigitalOutputMode.Low, program.Outputs[13]);
    }

    [Fact]
    public void Analyze_RejectsOverflowingPinNumberWithoutThrowing()
    {
        const string sketch = """
                              void setup() { }
                              void loop() { digitalWrite(999999999999999999999999, HIGH); }
                              """;

        var exception = Record.Exception(() => ArduinoSketchProgram.Analyze(sketch));
        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.Null(exception);
        Assert.False(program.IsValid);
        Assert.Equal("Pin number is too large", program.Diagnostic);
    }
}
