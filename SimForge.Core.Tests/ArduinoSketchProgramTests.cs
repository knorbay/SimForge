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
    public void Analyze_RecognizesBuiltInLedSymbol()
    {
        const string sketch = """
                              void setup() { }
                              void loop() { digitalWrite(LED_BUILTIN, HIGH); }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal(DigitalOutputMode.High, program.Outputs[13]);
    }

    [Fact]
    public void Analyze_RejectsUnbalancedBraces()
    {
        var program = ArduinoSketchProgram.Analyze("void setup() { } void loop() { digitalWrite(13, HIGH);");

        Assert.False(program.IsValid);
        Assert.Equal("Check brackets and braces", program.Diagnostic);
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
        Assert.Equal("Pin number must be between 0 and 255", program.Diagnostic);
    }

    [Fact]
    public void Analyze_ResolvesDefinesConstantsAndNumericLevels()
    {
        const string sketch = """
                              #define STATUS_PIN 12
                              const unsigned long waitMs = 250;
                              void setup() { pinMode(STATUS_PIN, OUTPUT); }
                              void loop() {
                                digitalWrite(STATUS_PIN, 1);
                                delay(waitMs);
                                digitalWrite(STATUS_PIN, 0);
                                delay(waitMs);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal(DigitalOutputMode.Blink, program.Outputs[12]);
        Assert.Equal(0.25, program.OutputProfiles[12].HighDurationSeconds);
        Assert.Equal(0.25, program.OutputProfiles[12].LowDurationSeconds);
    }

    [Fact]
    public void Analyze_PreservesAsymmetricBlinkTiming()
    {
        const string sketch = """
                              constexpr byte LED_PIN = D13;
                              void setup() { pinMode(LED_PIN, OUTPUT); }
                              void loop() {
                                digitalWrite(LED_PIN, HIGH);
                                delay(200);
                                digitalWrite(LED_PIN, LOW);
                                delay(800);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        var profile = program.OutputProfiles[13];
        Assert.True(profile.InitialState);
        Assert.Equal(0.2, profile.HighDurationSeconds);
        Assert.Equal(0.8, profile.LowDurationSeconds);
    }

    [Fact]
    public void Analyze_AllowsInputOnlySensorSketch()
    {
        const string sketch = """
                              void setup() { Serial.begin(9600); }
                              void loop() {
                                Serial.println(analogRead(A0));
                                delay(500);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal("Input sketch ready", program.Diagnostic);
        Assert.Equal("A0", Assert.Single(program.InputPins));
        Assert.Empty(program.Outputs);
    }

    [Fact]
    public void Analyze_RejectsWriteToExplicitInputPin()
    {
        const string sketch = """
                              void setup() { pinMode(7, INPUT_PULLUP); }
                              void loop() { digitalWrite(7, HIGH); }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.False(program.IsValid);
        Assert.Equal("Pin D7 is not OUTPUT", program.Diagnostic);
    }

    [Fact]
    public void Analyze_RejectsFunctionPrototypesWithoutBodies()
    {
        var program = ArduinoSketchProgram.Analyze("void setup(); void loop(); digitalWrite(13, HIGH);");

        Assert.False(program.IsValid);
        Assert.Equal("Missing setup()", program.Diagnostic);
    }

    [Fact]
    public void Analyze_RecognizesCommonDhtLibraryReads()
    {
        const string sketch = """
                              #include <DHT.h>
                              void setup() { Serial.begin(9600); }
                              void loop() {
                                float temperature = dht.readTemperature();
                                Serial.println(temperature);
                                delay(2000);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal("D2", Assert.Single(program.InputPins));
    }

    [Fact]
    public void Analyze_ParsesInlineAnalogReadCondition()
    {
        const string sketch = """
                              void setup() { pinMode(13, OUTPUT); }
                              void loop() {
                                if (analogRead(A0) > 600) {
                                  digitalWrite(13, HIGH);
                                } else {
                                  digitalWrite(13, LOW);
                                }
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        Assert.Equal(DigitalOutputMode.Conditional, program.Outputs[13]);
        var rule = Assert.Single(program.ConditionalOutputs);
        Assert.Equal("A0", rule.Condition.Pin);
        Assert.Equal(ArduinoInputKind.Analog, rule.Condition.Kind);
        Assert.False(rule.Evaluate(600));
        Assert.True(rule.Evaluate(601));
    }

    [Fact]
    public void Analyze_ParsesSensorVariableAndConstantThreshold()
    {
        const string sketch = """
                              const int echoPin = 2;
                              const int alertPin = 13;
                              const int nearEchoUs = 1200;
                              void setup() { pinMode(alertPin, OUTPUT); }
                              void loop() {
                                long echoTime = pulseIn(echoPin, HIGH);
                                if (echoTime <= nearEchoUs)
                                  digitalWrite(alertPin, HIGH);
                                else
                                  digitalWrite(alertPin, LOW);
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        var rule = Assert.Single(program.ConditionalOutputs);
        Assert.Equal(ArduinoInputKind.PulseDurationMicroseconds, rule.Condition.Kind);
        Assert.Equal("D2", rule.Condition.Pin);
        Assert.True(rule.Evaluate(1200));
        Assert.False(rule.Evaluate(1201));
    }

    [Fact]
    public void Analyze_UsesConfiguredPinForDhtConditional()
    {
        const string sketch = """
                              #define DHT_PIN 7
                              DHT dht(DHT_PIN, DHT11);
                              void setup() { pinMode(13, OUTPUT); }
                              void loop() {
                                float temperature = dht.readTemperature();
                                if (temperature >= 30) {
                                  digitalWrite(13, HIGH);
                                } else {
                                  digitalWrite(13, LOW);
                                }
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        var rule = Assert.Single(program.ConditionalOutputs);
        Assert.Equal(ArduinoInputKind.TemperatureCelsius, rule.Condition.Kind);
        Assert.Equal("D7", rule.Condition.Pin);
        Assert.True(rule.Evaluate(30));
    }

    [Fact]
    public void Analyze_RejectsSensorConditionThatCannotBeSimulatedReliably()
    {
        const string sketch = """
                              void setup() { pinMode(13, OUTPUT); }
                              void loop() {
                                int light = analogRead(A0);
                                if ((light * 2) > 600) {
                                  digitalWrite(13, HIGH);
                                } else {
                                  digitalWrite(13, LOW);
                                }
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.False(program.IsValid);
        Assert.Equal("Simplify the sensor if/else", program.Diagnostic);
    }

    [Fact]
    public void Analyze_RecognizesCommonHcSr04DistanceConversion()
    {
        const string sketch = """
                              void setup() { pinMode(13, OUTPUT); }
                              void loop() {
                                long duration = pulseIn(2, HIGH);
                                float distance = duration * 0.0343 / 2;
                                if (distance < 20) {
                                  digitalWrite(13, HIGH);
                                } else {
                                  digitalWrite(13, LOW);
                                }
                              }
                              """;

        var program = ArduinoSketchProgram.Analyze(sketch);

        Assert.True(program.IsValid);
        var rule = Assert.Single(program.ConditionalOutputs);
        Assert.Equal(ArduinoInputKind.DistanceCentimeters, rule.Condition.Kind);
        Assert.True(rule.Evaluate(19.9));
        Assert.False(rule.Evaluate(20));
    }
}
