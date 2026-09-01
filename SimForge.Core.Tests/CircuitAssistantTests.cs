using SimForge.Core;
using Xunit;

namespace SimForge.Core.Tests;

public sealed class CircuitAssistantTests
{
    [Fact]
    public void Analyze_EmptyWorkspaceExplainsTheFirstStep()
    {
        var report = CircuitAssistant.Analyze(Input(
            componentCount: 0,
            hasController: false,
            missingInputPins: ["A0"],
            missingOutputPins: [13]));

        Assert.Equal("START", report.Status);
        Assert.Equal("Start with the controller", Assert.Single(report.Issues).Title);
    }

    [Fact]
    public void Analyze_UnsafeLedIsBlockingAndActionable()
    {
        var report = CircuitAssistant.Analyze(Input(hasUnsafeLedPath: true, incompleteLedCount: 1));

        Assert.Equal("UNSAFE", report.Status);
        var issue = Assert.Single(report.Issues, item => item.Title == "LED path is unsafe");
        Assert.Contains("resistor", issue.Instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_ReportsPowerSignalAndSketchPinGaps()
    {
        var report = CircuitAssistant.Analyze(Input(
            unpoweredSensors: ["LDR Sensor"],
            unwiredSensors: ["DHT11 Temperature"],
            missingInputPins: ["A0"],
            missingOutputPins: [13]));

        Assert.Equal("CHECK", report.Status);
        Assert.Contains(report.Issues, issue => issue.Title == "Sensor power is incomplete");
        Assert.Contains(report.Issues, issue => issue.Instruction.Contains("A0", StringComparison.Ordinal));
        Assert.Contains(report.Issues, issue => issue.Instruction.Contains("D13", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_InvalidSketchProvidesBeginnerExample()
    {
        var report = CircuitAssistant.Analyze(Input(
            sketchValid: false,
            sketchDiagnostic: "Missing setup()"));

        Assert.Equal("FIX", report.Status);
        Assert.Contains("void setup()", report.CodeExample);
        Assert.Contains("pinMode", Assert.Single(report.Issues, issue => issue.Title == "Sketch needs attention").Instruction);
    }

    [Fact]
    public void Analyze_ReadySensorCircuitProvidesRelevantCode()
    {
        var report = CircuitAssistant.Analyze(Input(
            hasActuator: true,
            hasFunctionalCircuit: true,
            preferredSensorName: "HC-SR04 Distance"));

        Assert.Equal("READY", report.Status);
        Assert.Empty(report.Issues);
        Assert.Contains("pulseIn", report.CodeExample);
        Assert.Contains("distance", report.CodeExample);
    }

    [Fact]
    public void Analyze_ComplexConditionExplainsSupportedShape()
    {
        var report = CircuitAssistant.Analyze(Input(
            sketchValid: false,
            sketchDiagnostic: "Simplify the sensor if/else"));

        var issue = Assert.Single(report.Issues, item => item.Title == "Sketch needs attention");
        Assert.Contains("one sensor comparison", issue.Instruction);
    }

    [Theory]
    [InlineData("LDR Sensor")]
    [InlineData("Potentiometer")]
    [InlineData("HC-SR04 Distance")]
    [InlineData("DHT11 Temperature")]
    public void Analyze_BeginnerSensorExampleIsAcceptedBySketchAnalyzer(string sensorName)
    {
        var report = CircuitAssistant.Analyze(Input(preferredSensorName: sensorName));

        Assert.NotNull(report.CodeExample);
        var sketch = ArduinoSketchProgram.Analyze(report.CodeExample);
        Assert.True(sketch.IsValid, sketch.Diagnostic);
        Assert.NotEmpty(sketch.InputPins);
        Assert.NotEmpty(sketch.ConditionalOutputs);
    }

    private static CircuitAssistantInput Input(
        int componentCount = 3,
        bool hasController = true,
        bool hasActuator = true,
        bool hasFunctionalCircuit = true,
        bool hasUnsafeLedPath = false,
        int incompleteLedCount = 0,
        IReadOnlyList<string>? unpoweredSensors = null,
        IReadOnlyList<string>? unwiredSensors = null,
        IReadOnlyList<string>? missingInputPins = null,
        IReadOnlyList<int>? missingOutputPins = null,
        bool sketchValid = true,
        string sketchDiagnostic = "Sketch ready",
        string? preferredSensorName = null) => new(
        componentCount,
        hasController,
        hasActuator,
        hasFunctionalCircuit,
        hasUnsafeLedPath,
        incompleteLedCount,
        unpoweredSensors ?? [],
        unwiredSensors ?? [],
        missingInputPins ?? [],
        missingOutputPins ?? [],
        sketchValid,
        sketchDiagnostic,
        preferredSensorName);
}
