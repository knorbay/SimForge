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
        var code = RemoveCommentsAndLiterals(source ?? string.Empty);
        var bracesValid = HasBalancedBraces(code);
        var hasSetup = Regex.IsMatch(code, @"\bvoid\s+setup\s*\(", RegexOptions.IgnoreCase);
        var hasLoop = Regex.IsMatch(code, @"\bvoid\s+loop\s*\(", RegexOptions.IgnoreCase);
        var hasDigitalWriteCall = Regex.IsMatch(code, @"\bdigitalWrite\s*\(", RegexOptions.IgnoreCase);
        var writeMatches = Regex.Matches(
            code,
            @"\bdigitalWrite\s*\(\s*(\d+)\s*,\s*(HIGH|LOW)\s*\)",
            RegexOptions.IgnoreCase);

        var parsedWrites = new List<(int Pin, string Level)>();
        var hasInvalidPinNumber = false;
        foreach (Match match in writeMatches)
        {
            if (!int.TryParse(match.Groups[1].Value, out var pinNumber))
            {
                hasInvalidPinNumber = true;
                continue;
            }

            parsedWrites.Add((pinNumber, match.Groups[2].Value.ToUpperInvariant()));
        }

        var outputs = new Dictionary<int, DigitalOutputMode>();
        foreach (var pinGroup in parsedWrites.GroupBy(write => write.Pin))
        {
            var levels = pinGroup.Select(write => write.Level).ToList();
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

        var isValid = bracesValid && hasSetup && hasLoop && outputs.Count > 0 && !hasInvalidPinNumber;
        var diagnostic = isValid
            ? "Sketch ready"
            : !bracesValid
                ? "Check braces"
                : !hasSetup
                    ? "Missing setup()"
                    : !hasLoop
                        ? "Missing loop()"
                        : hasInvalidPinNumber
                            ? "Pin number is too large"
                        : hasDigitalWriteCall
                            ? "Use a numeric output pin"
                            : "No output write";

        return new ArduinoSketchProgram(
            isValid,
            diagnostic,
            intervalSeconds,
            new ReadOnlyDictionary<int, DigitalOutputMode>(outputs));
    }

    private static string RemoveCommentsAndLiterals(string source)
    {
        var result = new char[source.Length];
        var state = LexicalState.Code;
        var escaped = false;

        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (state == LexicalState.Code)
            {
                if (current == '/' && next == '/')
                {
                    state = LexicalState.LineComment;
                    result[index] = result[index + 1] = ' ';
                    index++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    state = LexicalState.BlockComment;
                    result[index] = result[index + 1] = ' ';
                    index++;
                    continue;
                }

                if (current is '"' or '\'')
                {
                    state = current == '"' ? LexicalState.String : LexicalState.Character;
                    result[index] = ' ';
                    escaped = false;
                    continue;
                }

                result[index] = current;
                continue;
            }

            if (state == LexicalState.LineComment)
            {
                if (current == '\n')
                {
                    state = LexicalState.Code;
                    result[index] = '\n';
                }
                else
                {
                    result[index] = ' ';
                }
                continue;
            }

            if (state == LexicalState.BlockComment)
            {
                if (current == '*' && next == '/')
                {
                    result[index] = result[index + 1] = ' ';
                    index++;
                    state = LexicalState.Code;
                }
                else
                {
                    result[index] = current == '\n' ? '\n' : ' ';
                }
                continue;
            }

            result[index] = current == '\n' ? '\n' : ' ';
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if ((state == LexicalState.String && current == '"') ||
                     (state == LexicalState.Character && current == '\''))
            {
                state = LexicalState.Code;
            }
        }

        return new string(result);
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

    private enum LexicalState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        Character
    }
}
