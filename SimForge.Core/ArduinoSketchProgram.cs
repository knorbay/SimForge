using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace SimForge.Core;

public enum DigitalOutputMode
{
    Blink,
    High,
    Low
}

public sealed class ArduinoSketchProgram
{
    private ArduinoSketchProgram(
        bool isValid,
        string diagnostic,
        double intervalSeconds,
        IReadOnlyDictionary<int, DigitalOutputMode> outputs)
    {
        IsValid = isValid;
        Diagnostic = diagnostic;
        IntervalSeconds = intervalSeconds;
        Outputs = outputs;
    }

    public bool IsValid { get; }
    public string Diagnostic { get; }
    public double IntervalSeconds { get; }
    public IReadOnlyDictionary<int, DigitalOutputMode> Outputs { get; }

    public static ArduinoSketchProgram Analyze(string? source)
    {
        var code = source ?? string.Empty;
        var bracesValid = HasBalancedBraces(code);
        var hasSetup = Regex.IsMatch(code, @"\bvoid\s+setup\s*\(", RegexOptions.IgnoreCase);
        var hasLoop = Regex.IsMatch(code, @"\bvoid\s+loop\s*\(", RegexOptions.IgnoreCase);
        var hasDigitalWriteCall = Regex.IsMatch(code, @"\bdigitalWrite\s*\(", RegexOptions.IgnoreCase);
        var writeMatches = Regex.Matches(
            code,
            @"\bdigitalWrite\s*\(\s*(\d+)\s*,\s*(HIGH|LOW)\s*\)",
            RegexOptions.IgnoreCase);

        var outputs = new Dictionary<int, DigitalOutputMode>();
        foreach (var pinGroup in writeMatches
                     .Cast<Match>()
                     .GroupBy(match => int.Parse(match.Groups[1].Value)))
        {
            var levels = pinGroup.Select(match => match.Groups[2].Value.ToUpperInvariant()).ToList();
            var hasHigh = levels.Contains("HIGH");
            var hasLow = levels.Contains("LOW");
            outputs[pinGroup.Key] = hasHigh && hasLow
                ? DigitalOutputMode.Blink
                : hasHigh
                    ? DigitalOutputMode.High
                    : DigitalOutputMode.Low;
        }

        var delayMatch = Regex.Match(code, @"\bdelay\s*\(\s*(\d+)", RegexOptions.IgnoreCase);
        var intervalSeconds = delayMatch.Success && double.TryParse(delayMatch.Groups[1].Value, out var delayMilliseconds)
            ? Math.Clamp(delayMilliseconds / 1000d, 0.05, 10)
            : 1;

        var isValid = bracesValid && hasSetup && hasLoop && outputs.Count > 0;
        var diagnostic = isValid
            ? "Sketch ready"
            : !bracesValid
                ? "Check braces"
                : !hasSetup
                    ? "Missing setup()"
                    : !hasLoop
                        ? "Missing loop()"
                        : hasDigitalWriteCall
                            ? "Use a numeric output pin"
                            : "No output write";

        return new ArduinoSketchProgram(
            isValid,
            diagnostic,
            intervalSeconds,
            new ReadOnlyDictionary<int, DigitalOutputMode>(outputs));
    }

    private static bool HasBalancedBraces(string code)
    {
        var balance = 0;
        foreach (var character in code)
        {
            if (character == '{')
                balance++;
            else if (character == '}')
                balance--;

            if (balance < 0)
                return false;
        }

        return balance == 0;
    }
}
