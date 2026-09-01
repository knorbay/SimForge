using System.Collections.ObjectModel;

namespace SimForge.Core;

public enum GuidanceSeverity
{
    Info,
    Warning,
    Error
}

public sealed record CircuitGuidanceIssue(
    GuidanceSeverity Severity,
    string Title,
    string Instruction);

public sealed record CircuitAssistantInput(
    int ComponentCount,
    bool HasController,
    bool HasActuator,
    bool HasFunctionalCircuit,
    bool HasUnsafeLedPath,
    int IncompleteLedCount,
    IReadOnlyList<string> UnpoweredSensors,
    IReadOnlyList<string> UnwiredSensors,
    IReadOnlyList<string> MissingInputPins,
    IReadOnlyList<int> MissingOutputPins,
    bool SketchValid,
    string SketchDiagnostic,
    string? PreferredSensorName = null);

public sealed record CircuitAssistantReport(
    string Status,
    string Summary,
    IReadOnlyList<CircuitGuidanceIssue> Issues,
    string? CodeExample);

public static class CircuitAssistant
{
    public static CircuitAssistantReport Analyze(CircuitAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var issues = new List<CircuitGuidanceIssue>();

        if (input.ComponentCount == 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Info,
                "Start with the controller",
                "Add an Arduino, then add an LED or sensor from the component library."));
        }
        else if (!input.HasController)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Error,
                "Controller missing",
                "Add an Arduino-compatible board so the sketch has pins to read and drive."));
        }

        if (!input.SketchValid)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Error,
                "Sketch needs attention",
                ExplainSketchDiagnostic(input.SketchDiagnostic)));
        }

        if (input.HasUnsafeLedPath)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Error,
                "LED path is unsafe",
                "Place a resistor between the controller output and LED anode before running."));
        }

        if (input.UnpoweredSensors.Count > 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Warning,
                "Sensor power is incomplete",
                $"Connect VCC to 5V and GND to board GND for {JoinNames(input.UnpoweredSensors)}."));
        }

        if (input.UnwiredSensors.Count > 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Warning,
                "Sensor signal is not connected",
                $"Wire the data pin to the matching controller input for {JoinNames(input.UnwiredSensors)}."));
        }

        if (input.HasController && input.MissingInputPins.Count > 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Warning,
                "Code reads an empty pin",
                $"The sketch reads {string.Join(", ", input.MissingInputPins)}. Connect a compatible sensor output to that pin."));
        }

        if (input.HasController && input.MissingOutputPins.Count > 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Warning,
                "Code output is not used",
                $"The sketch drives {string.Join(", ", input.MissingOutputPins.Select(pin => $"D{pin}"))}. Connect that pin to an actuator path."));
        }

        if (input.IncompleteLedCount > 0 && !input.HasUnsafeLedPath)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Warning,
                "LED loop is incomplete",
                input.IncompleteLedCount == 1
                    ? "Connect output → resistor → LED anode, then LED cathode → GND."
                    : $"Complete the source, resistor, and ground path for {input.IncompleteLedCount} LEDs."));
        }

        if (input.ComponentCount > 0 && !input.HasActuator && input.UnpoweredSensors.Count == 0 &&
            input.UnwiredSensors.Count == 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Info,
                "No visible output yet",
                "Add an LED if you want to see the sketch response directly on the canvas."));
        }

        if (input.ComponentCount > 0 && !input.HasFunctionalCircuit && issues.Count == 0)
        {
            issues.Add(new CircuitGuidanceIssue(
                GuidanceSeverity.Warning,
                "Circuit is not complete",
                "Check that every signal path has a source, destination, and ground return."));
        }

        var readOnlyIssues = new ReadOnlyCollection<CircuitGuidanceIssue>(issues);
        if (issues.Count == 0)
        {
            return new CircuitAssistantReport(
                "READY",
                "Circuit and sketch agree. You can run the simulation.",
                readOnlyIssues,
                BuildCodeExample(input));
        }

        var errorCount = issues.Count(issue => issue.Severity == GuidanceSeverity.Error);
        var warningCount = issues.Count(issue => issue.Severity == GuidanceSeverity.Warning);
        var status = input.HasUnsafeLedPath
            ? "UNSAFE"
            : errorCount > 0
                ? "FIX"
                : input.ComponentCount == 0
                    ? "START"
                    : warningCount > 0
                        ? "CHECK"
                        : input.HasFunctionalCircuit
                            ? "READY"
                            : "START";
        var summary = errorCount > 0
            ? $"{errorCount} blocking issue{(errorCount == 1 ? string.Empty : "s")} found. Fix the first item before running."
            : warningCount > 0
                ? $"{warningCount} connection issue{(warningCount == 1 ? string.Empty : "s")} found."
                : input.ComponentCount == 0
                    ? "Follow the first step below to begin."
                    : input.HasFunctionalCircuit
                        ? "Circuit can run. The item below is an optional improvement."
                        : "Follow the first step below to begin.";
        return new CircuitAssistantReport(status, summary, readOnlyIssues, BuildCodeExample(input));
    }

    private static string ExplainSketchDiagnostic(string diagnostic)
    {
        if (diagnostic.Contains("setup", StringComparison.OrdinalIgnoreCase))
            return "Add void setup() and configure output pins with pinMode(pin, OUTPUT).";
        if (diagnostic.Contains("loop", StringComparison.OrdinalIgnoreCase))
            return "Add void loop(); SimForge repeats the statements inside it.";
        if (diagnostic.Contains("bracket", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("brace", StringComparison.OrdinalIgnoreCase))
            return "Match every opening (, [, or { with a closing ), ], or }.";
        if (diagnostic.Contains("not OUTPUT", StringComparison.OrdinalIgnoreCase))
            return "Change that pinMode to OUTPUT, or write to the pin configured as OUTPUT.";
        if (diagnostic.Contains("numeric or constant", StringComparison.OrdinalIgnoreCase))
            return "Use a number, LED_BUILTIN, #define, or const int for the output pin.";
        if (diagnostic.Contains("Simplify", StringComparison.OrdinalIgnoreCase))
            return "Use one sensor comparison and one digitalWrite in each if/else branch.";
        if (diagnostic.Contains("I/O", StringComparison.OrdinalIgnoreCase))
            return "Add digitalWrite, analogRead, digitalRead, pulseIn, or a DHT read inside loop().";
        return diagnostic;
    }

    private static string? BuildCodeExample(CircuitAssistantInput input)
    {
        if (!input.SketchValid)
        {
            return "void setup() {\n  pinMode(13, OUTPUT);\n}\n\nvoid loop() {\n  digitalWrite(13, HIGH);\n}";
        }

        return input.PreferredSensorName switch
        {
            "LDR Sensor" or "Potentiometer" =>
                "void setup() {\n  pinMode(13, OUTPUT);\n}\n\nvoid loop() {\n  int value = analogRead(A0);\n  if (value > 600) {\n    digitalWrite(13, HIGH);\n  } else {\n    digitalWrite(13, LOW);\n  }\n}",
            "HC-SR04 Distance" =>
                "const int trigPin = 7;\nconst int echoPin = 2;\n\nvoid setup() {\n  pinMode(trigPin, OUTPUT);\n  pinMode(echoPin, INPUT);\n  pinMode(13, OUTPUT);\n}\n\nvoid loop() {\n  digitalWrite(trigPin, LOW);\n  delayMicroseconds(2);\n  digitalWrite(trigPin, HIGH);\n  delayMicroseconds(10);\n  digitalWrite(trigPin, LOW);\n  long duration = pulseIn(echoPin, HIGH);\n  float distance = duration * 0.0343 / 2;\n  if (distance < 20) {\n    digitalWrite(13, HIGH);\n  } else {\n    digitalWrite(13, LOW);\n  }\n}",
            "DHT11 Temperature" =>
                "#include <DHT.h>\n#define DHTPIN 2\nDHT dht(DHTPIN, DHT11);\n\nvoid setup() {\n  dht.begin();\n  pinMode(13, OUTPUT);\n}\n\nvoid loop() {\n  float temperature = dht.readTemperature();\n  if (temperature >= 30) {\n    digitalWrite(13, HIGH);\n  } else {\n    digitalWrite(13, LOW);\n  }\n  delay(2000);\n}",
            _ => null
        };
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}"
    };
}
