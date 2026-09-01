using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SimForge.Core;

public enum DigitalOutputMode
{
    Blink,
    High,
    Low,
    Conditional
}

public enum ArduinoInputKind
{
    Analog,
    Digital,
    PulseDurationMicroseconds,
    DistanceCentimeters,
    TemperatureCelsius,
    RelativeHumidity
}

public enum ArduinoComparisonOperator
{
    LessThan,
    LessThanOrEqual,
    Equal,
    NotEqual,
    GreaterThanOrEqual,
    GreaterThan
}

public sealed record ArduinoInputCondition(
    ArduinoInputKind Kind,
    string Pin,
    ArduinoComparisonOperator Operator,
    double Threshold)
{
    public bool Evaluate(double inputValue) => Operator switch
    {
        ArduinoComparisonOperator.LessThan => inputValue < Threshold,
        ArduinoComparisonOperator.LessThanOrEqual => inputValue <= Threshold,
        ArduinoComparisonOperator.Equal => Math.Abs(inputValue - Threshold) < 0.000001,
        ArduinoComparisonOperator.NotEqual => Math.Abs(inputValue - Threshold) >= 0.000001,
        ArduinoComparisonOperator.GreaterThanOrEqual => inputValue >= Threshold,
        ArduinoComparisonOperator.GreaterThan => inputValue > Threshold,
        _ => false
    };

    public string ToDisplayString() => $"{Pin} {OperatorToText(Operator)} {Threshold:0.##}";

    private static string OperatorToText(ArduinoComparisonOperator comparison) => comparison switch
    {
        ArduinoComparisonOperator.LessThan => "<",
        ArduinoComparisonOperator.LessThanOrEqual => "<=",
        ArduinoComparisonOperator.Equal => "==",
        ArduinoComparisonOperator.NotEqual => "!=",
        ArduinoComparisonOperator.GreaterThanOrEqual => ">=",
        ArduinoComparisonOperator.GreaterThan => ">",
        _ => "?"
    };
}

public sealed record ConditionalOutputRule(
    int OutputPin,
    ArduinoInputCondition Condition,
    bool TrueState,
    bool FalseState)
{
    public bool Evaluate(double inputValue) => Condition.Evaluate(inputValue) ? TrueState : FalseState;
}

public sealed record DigitalOutputProfile(
    DigitalOutputMode Mode,
    bool InitialState,
    double HighDurationSeconds,
    double LowDurationSeconds);

public sealed class ArduinoSketchProgram
{
    private static readonly Regex DigitalWriteRegex = new(
        @"\bdigitalWrite\s*\(\s*(?<pin>[^,()]+)\s*,\s*(?<level>[^()]+?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DelayRegex = new(
        @"\b(?<kind>delay|delayMicroseconds)\s*\(\s*(?<value>[^()]+?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private ArduinoSketchProgram(
        bool isValid,
        string diagnostic,
        double intervalSeconds,
        IReadOnlyDictionary<int, DigitalOutputMode> outputs,
        IReadOnlyDictionary<int, DigitalOutputProfile> outputProfiles,
        IReadOnlyList<string> inputPins,
        IReadOnlyList<ConditionalOutputRule> conditionalOutputs)
    {
        IsValid = isValid;
        Diagnostic = diagnostic;
        IntervalSeconds = intervalSeconds;
        Outputs = outputs;
        OutputProfiles = outputProfiles;
        InputPins = inputPins;
        ConditionalOutputs = conditionalOutputs;
    }

    public bool IsValid { get; }
    public string Diagnostic { get; }
    public double IntervalSeconds { get; }
    public IReadOnlyDictionary<int, DigitalOutputMode> Outputs { get; }
    public IReadOnlyDictionary<int, DigitalOutputProfile> OutputProfiles { get; }
    public IReadOnlyList<string> InputPins { get; }
    public IReadOnlyList<ConditionalOutputRule> ConditionalOutputs { get; }

    public static ArduinoSketchProgram Analyze(string? source)
    {
        var code = RemoveCommentsAndLiterals(source ?? string.Empty);
        var delimitersValid = HasBalancedDelimiters(code);
        var hasSetup = TryExtractFunctionBody(code, "setup", out _);
        var hasLoop = TryExtractFunctionBody(code, "loop", out var loopFunctionBody);
        var constants = ResolveIntegerConstants(code);
        var hasDigitalWriteCall = DigitalWriteRegex.IsMatch(code);

        var parsedWrites = new List<WriteOperation>();
        var hasInvalidPinNumber = false;
        var hasUnsupportedOutputPin = false;
        var hasUnsupportedOutputLevel = false;
        foreach (Match match in DigitalWriteRegex.Matches(code))
        {
            var pinResult = TryResolvePin(match.Groups["pin"].Value, constants);
            if (pinResult.Status == PinResolutionStatus.InvalidRange)
            {
                hasInvalidPinNumber = true;
                continue;
            }

            if (pinResult.Status != PinResolutionStatus.Success)
            {
                hasUnsupportedOutputPin = true;
                continue;
            }

            if (!TryResolveLevel(match.Groups["level"].Value, constants, out var isHigh))
            {
                hasUnsupportedOutputLevel = true;
                continue;
            }

            parsedWrites.Add(new WriteOperation(pinResult.PinNumber, isHigh));
        }

        var pinModes = ParsePinModes(code, constants);
        var incorrectlyConfiguredPin = parsedWrites
            .Select(write => write.PinNumber)
            .Distinct()
            .FirstOrDefault(pin => pinModes.TryGetValue(pin, out var mode) &&
                                   !string.Equals(mode, "OUTPUT", StringComparison.OrdinalIgnoreCase), -1);

        var outputs = new Dictionary<int, DigitalOutputMode>();
        foreach (var pinGroup in parsedWrites.GroupBy(write => write.PinNumber))
        {
            var hasHigh = pinGroup.Any(write => write.IsHigh);
            var hasLow = pinGroup.Any(write => !write.IsHigh);
            outputs[pinGroup.Key] = hasHigh && hasLow
                ? DigitalOutputMode.Blink
                : hasHigh
                    ? DigitalOutputMode.High
                    : DigitalOutputMode.Low;
        }

        var loopCode = hasLoop ? loopFunctionBody : code;
        var inputPins = ParseInputPins(code, constants);
        var conditionalOutputs = ParseConditionalOutputs(code, constants);
        foreach (var rule in conditionalOutputs)
            outputs[rule.OutputPin] = DigitalOutputMode.Conditional;

        var outputProfiles = BuildOutputProfiles(loopCode, constants, outputs);
        foreach (var rule in conditionalOutputs)
            outputProfiles[rule.OutputPin] = new DigitalOutputProfile(
                DigitalOutputMode.Conditional,
                rule.FalseState,
                1,
                1);
        var intervalSeconds = outputProfiles.Values
            .FirstOrDefault(profile => profile.Mode == DigitalOutputMode.Blink)?.HighDurationSeconds ??
                              ParseFirstDelaySeconds(loopCode, constants) ?? 1;

        var hasSupportedIo = outputs.Count > 0 || inputPins.Count > 0;
        var hasUnsupportedSensorCondition = inputPins.Count > 0 && outputs.Count > 0 &&
                                            Regex.IsMatch(loopCode, @"\bif\s*\(", RegexOptions.IgnoreCase) &&
                                            conditionalOutputs.Count == 0;
        var isValid = delimitersValid && hasSetup && hasLoop && hasSupportedIo &&
                      !hasInvalidPinNumber && !hasUnsupportedOutputPin && !hasUnsupportedOutputLevel &&
                      incorrectlyConfiguredPin < 0 && !hasUnsupportedSensorCondition;
        var diagnostic = isValid
            ? inputPins.Count > 0 && outputs.Count == 0 ? "Input sketch ready" : "Sketch ready"
            : !delimitersValid
                ? "Check brackets and braces"
                : !hasSetup
                    ? "Missing setup()"
                    : !hasLoop
                        ? "Missing loop()"
                        : hasInvalidPinNumber
                            ? "Pin number must be between 0 and 255"
                            : hasUnsupportedOutputPin
                                ? "Use a numeric or constant output pin"
                                : hasUnsupportedOutputLevel
                                    ? "Use HIGH, LOW, 1, or 0"
                                    : incorrectlyConfiguredPin >= 0
                                        ? $"Pin D{incorrectlyConfiguredPin} is not OUTPUT"
                                        : hasUnsupportedSensorCondition
                                            ? "Simplify the sensor if/else"
                                        : hasDigitalWriteCall
                                            ? "Unsupported output write"
                                            : "No supported Arduino I/O";

        return new ArduinoSketchProgram(
            isValid,
            diagnostic,
            intervalSeconds,
            new ReadOnlyDictionary<int, DigitalOutputMode>(outputs),
            new ReadOnlyDictionary<int, DigitalOutputProfile>(outputProfiles),
            new ReadOnlyCollection<string>(inputPins),
            new ReadOnlyCollection<ConditionalOutputRule>(conditionalOutputs));
    }

    private static Dictionary<string, long> ResolveIntegerConstants(string code)
    {
        var constants = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["LED_BUILTIN"] = 13,
            ["HIGH"] = 1,
            ["LOW"] = 0,
            ["true"] = 1,
            ["false"] = 0
        };
        var candidates = new List<(string Name, string Value)>();

        foreach (Match match in Regex.Matches(
                     code,
                     @"(?m)^[ \t]*#[ \t]*define[ \t]+(?<name>[A-Za-z_]\w*)[ \t]+(?<value>[^\r\n]+?)[ \t]*\r?$"))
            candidates.Add((match.Groups["name"].Value, match.Groups["value"].Value.Trim()));

        foreach (Match match in Regex.Matches(
                     code,
                     @"\b(?:(?:static|const|constexpr)\s+)*(?:unsigned\s+)?(?:char|byte|short|int|long|uint8_t|uint16_t|uint32_t|size_t)\s+(?:(?:const|constexpr)\s+)*(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>[^,;]+)\s*;",
                     RegexOptions.CultureInvariant))
            candidates.Add((match.Groups["name"].Value, match.Groups["value"].Value.Trim()));

        for (var pass = 0; pass < candidates.Count + 1; pass++)
        {
            var changed = false;
            foreach (var (name, value) in candidates)
            {
                if (constants.ContainsKey(name) || !TryResolveInteger(value, constants, out var resolved))
                    continue;

                constants[name] = resolved;
                changed = true;
            }

            if (!changed)
                break;
        }

        return constants;
    }

    private static Dictionary<int, string> ParsePinModes(string code, IReadOnlyDictionary<string, long> constants)
    {
        var result = new Dictionary<int, string>();
        foreach (Match match in Regex.Matches(
                     code,
                     @"\bpinMode\s*\(\s*(?<pin>[^,()]+)\s*,\s*(?<mode>[A-Za-z_]\w*)\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var pin = TryResolvePin(match.Groups["pin"].Value, constants);
            if (pin.Status == PinResolutionStatus.Success)
                result[pin.PinNumber] = match.Groups["mode"].Value;
        }

        return result;
    }

    private static List<string> ParseInputPins(string code, IReadOnlyDictionary<string, long> constants)
    {
        var result = new List<string>();
        foreach (Match match in Regex.Matches(
                     code,
                     @"\b(?<operation>analogRead|digitalRead|pulseIn)\s*\(\s*(?<pin>[^,()]+)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var pinExpression = match.Groups["pin"].Value.Trim();
            var pin = TryResolvePin(pinExpression, constants);
            if (pin.Status != PinResolutionStatus.Success)
                continue;

            var operation = match.Groups["operation"].Value;
            var label = operation.Equals("analogRead", StringComparison.OrdinalIgnoreCase)
                ? NormalizeAnalogPinLabel(pinExpression, pin.PinNumber)
                : $"D{pin.PinNumber}";
            if (!result.Contains(label, StringComparer.OrdinalIgnoreCase))
                result.Add(label);
        }

        if (Regex.IsMatch(code, @"\b(?:readTemperature|readHumidity)\s*\(\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var dhtPin = ResolveDhtPin(code, constants);
            if (!result.Contains(dhtPin, StringComparer.OrdinalIgnoreCase))
                result.Add(dhtPin);
        }

        return result;
    }

    private static List<ConditionalOutputRule> ParseConditionalOutputs(
        string code,
        IReadOnlyDictionary<string, long> constants)
    {
        var dhtPin = ResolveDhtPin(code, constants);
        var inputVariables = new Dictionary<string, InputSource>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     code,
                     @"\b(?<variable>[A-Za-z_]\w*)\s*=\s*(?<input>(?:(?:[A-Za-z_]\w*)\s*\.\s*)?(?:analogRead|digitalRead|pulseIn|readTemperature|readHumidity)\s*\([^;]*?\))\s*;",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (TryParseInputSource(match.Groups["input"].Value, constants, dhtPin, out var source))
                inputVariables[match.Groups["variable"].Value] = source;
        }

        foreach (Match match in Regex.Matches(
                     code,
                     @"\b(?<variable>[A-Za-z_]\w*)\s*=\s*(?<source>[A-Za-z_]\w*)\s*\*\s*(?<factor>\d+(?:\.\d+)?)\s*/\s*(?<divisor>\d+(?:\.\d+)?)\s*;",
                     RegexOptions.CultureInvariant))
        {
            if (!inputVariables.TryGetValue(match.Groups["source"].Value, out var source) ||
                source.Kind != ArduinoInputKind.PulseDurationMicroseconds ||
                !double.TryParse(match.Groups["factor"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var factor) ||
                !double.TryParse(match.Groups["divisor"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var divisor) || divisor <= 0)
                continue;

            var centimetersPerMicrosecond = factor / divisor;
            if (centimetersPerMicrosecond is >= 0.015 and <= 0.02)
                inputVariables[match.Groups["variable"].Value] =
                    new InputSource(ArduinoInputKind.DistanceCentimeters, source.Pin);
        }

        var rules = new List<ConditionalOutputRule>();
        foreach (Match match in Regex.Matches(
                     code,
                     @"\bif\s*\(\s*(?<input>.+?)\s*(?<comparison>>=|<=|==|!=|>|<)\s*(?<threshold>[A-Za-z_]\w*|[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*\)\s*\{?\s*digitalWrite\s*\(\s*(?<truePin>[^,()]+)\s*,\s*(?<trueLevel>[^()]+?)\s*\)\s*;?\s*\}?\s*else\s*\{?\s*digitalWrite\s*\(\s*(?<falsePin>[^,()]+)\s*,\s*(?<falseLevel>[^()]+?)\s*\)\s*;?",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
        {
            var inputExpression = UnwrapParentheses(match.Groups["input"].Value);
            if (!TryParseInputSource(inputExpression, constants, dhtPin, out var inputSource) &&
                !inputVariables.TryGetValue(inputExpression.Trim(), out inputSource))
                continue;

            var truePin = TryResolvePin(match.Groups["truePin"].Value, constants);
            var falsePin = TryResolvePin(match.Groups["falsePin"].Value, constants);
            if (truePin.Status != PinResolutionStatus.Success || falsePin.Status != PinResolutionStatus.Success ||
                truePin.PinNumber != falsePin.PinNumber ||
                !TryResolveLevel(match.Groups["trueLevel"].Value, constants, out var trueState) ||
                !TryResolveLevel(match.Groups["falseLevel"].Value, constants, out var falseState) ||
                !TryResolveDouble(match.Groups["threshold"].Value, constants, out var threshold) ||
                !TryParseComparison(match.Groups["comparison"].Value, out var comparison))
                continue;

            rules.Add(new ConditionalOutputRule(
                truePin.PinNumber,
                new ArduinoInputCondition(inputSource.Kind, inputSource.Pin, comparison, threshold),
                trueState,
                falseState));
        }

        return rules;
    }

    private static bool TryParseInputSource(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        string dhtPin,
        out InputSource source)
    {
        var value = UnwrapParentheses(expression).Trim();
        var pinRead = Regex.Match(
            value,
            @"^(?<operation>analogRead|digitalRead|pulseIn)\s*\(\s*(?<pin>[^,()]+)(?:\s*,[^()]*)?\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (pinRead.Success)
        {
            var pinExpression = pinRead.Groups["pin"].Value;
            var pin = TryResolvePin(pinExpression, constants);
            if (pin.Status == PinResolutionStatus.Success)
            {
                var operation = pinRead.Groups["operation"].Value;
                if (operation.Equals("analogRead", StringComparison.OrdinalIgnoreCase))
                    source = new InputSource(ArduinoInputKind.Analog, NormalizeAnalogPinLabel(pinExpression, pin.PinNumber));
                else if (operation.Equals("pulseIn", StringComparison.OrdinalIgnoreCase))
                    source = new InputSource(ArduinoInputKind.PulseDurationMicroseconds, $"D{pin.PinNumber}");
                else
                    source = new InputSource(ArduinoInputKind.Digital, $"D{pin.PinNumber}");
                return true;
            }
        }

        if (Regex.IsMatch(value, @"(?:^|\.)readTemperature\s*\(\s*\)$", RegexOptions.IgnoreCase))
        {
            source = new InputSource(ArduinoInputKind.TemperatureCelsius, dhtPin);
            return true;
        }

        if (Regex.IsMatch(value, @"(?:^|\.)readHumidity\s*\(\s*\)$", RegexOptions.IgnoreCase))
        {
            source = new InputSource(ArduinoInputKind.RelativeHumidity, dhtPin);
            return true;
        }

        source = default;
        return false;
    }

    private static string ResolveDhtPin(string code, IReadOnlyDictionary<string, long> constants)
    {
        var match = Regex.Match(
            code,
            @"\bDHT\s+[A-Za-z_]\w*\s*\(\s*(?<pin>[^,()]+)\s*,",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return "D2";

        var pin = TryResolvePin(match.Groups["pin"].Value, constants);
        return pin.Status == PinResolutionStatus.Success ? $"D{pin.PinNumber}" : "D2";
    }

    private static bool TryResolveDouble(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        out double value)
    {
        var token = UnwrapParentheses(expression).Trim();
        if (constants.TryGetValue(token, out var integerValue))
        {
            value = integerValue;
            return true;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static bool TryParseComparison(string value, out ArduinoComparisonOperator comparison)
    {
        comparison = value switch
        {
            "<" => ArduinoComparisonOperator.LessThan,
            "<=" => ArduinoComparisonOperator.LessThanOrEqual,
            "==" => ArduinoComparisonOperator.Equal,
            "!=" => ArduinoComparisonOperator.NotEqual,
            ">=" => ArduinoComparisonOperator.GreaterThanOrEqual,
            ">" => ArduinoComparisonOperator.GreaterThan,
            _ => default
        };
        return value is "<" or "<=" or "==" or "!=" or ">=" or ">";
    }

    private static string NormalizeAnalogPinLabel(string expression, int pinNumber)
    {
        var match = Regex.Match(UnwrapParentheses(expression), @"^A(?<channel>\d+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"A{match.Groups["channel"].Value}";

        return pinNumber is >= 14 and <= 21 ? $"A{pinNumber - 14}" : $"A{pinNumber}";
    }

    private static Dictionary<int, DigitalOutputProfile> BuildOutputProfiles(
        string loopCode,
        IReadOnlyDictionary<string, long> constants,
        IReadOnlyDictionary<int, DigitalOutputMode> outputs)
    {
        var operations = new List<TimedOperation>();
        foreach (Match match in Regex.Matches(
                     loopCode,
                     @"\bdigitalWrite\s*\(\s*(?<pin>[^,()]+)\s*,\s*(?<level>[^()]+?)\s*\)|\b(?<kind>delay|delayMicroseconds)\s*\(\s*(?<value>[^()]+?)\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (match.Groups["pin"].Success)
            {
                var pin = TryResolvePin(match.Groups["pin"].Value, constants);
                if (pin.Status == PinResolutionStatus.Success &&
                    TryResolveLevel(match.Groups["level"].Value, constants, out var isHigh))
                    operations.Add(TimedOperation.Write(pin.PinNumber, isHigh));
                continue;
            }

            if (TryResolveInteger(match.Groups["value"].Value, constants, out var delayValue))
            {
                var divisor = match.Groups["kind"].Value.Equals("delayMicroseconds", StringComparison.OrdinalIgnoreCase)
                    ? 1_000_000d
                    : 1_000d;
                operations.Add(TimedOperation.Delay(Math.Max(0, delayValue / divisor)));
            }
        }

        var profiles = new Dictionary<int, DigitalOutputProfile>();
        foreach (var (pinNumber, mode) in outputs)
        {
            var pinWrites = operations.Where(operation => operation.PinNumber == pinNumber && operation.IsWrite).ToList();
            var initialState = pinWrites.FirstOrDefault()?.State ?? mode == DigitalOutputMode.High;
            if (mode != DigitalOutputMode.Blink)
            {
                profiles[pinNumber] = new DigitalOutputProfile(mode, initialState, 1, 1);
                continue;
            }

            var state = pinWrites.LastOrDefault()?.State ?? initialState;
            var highDuration = 0d;
            var lowDuration = 0d;
            foreach (var operation in operations)
            {
                if (operation.IsWrite && operation.PinNumber == pinNumber)
                {
                    state = operation.State;
                }
                else if (!operation.IsWrite)
                {
                    if (state)
                        highDuration += operation.DurationSeconds;
                    else
                        lowDuration += operation.DurationSeconds;
                }
            }

            highDuration = NormalizeSimulationDuration(highDuration > 0 ? highDuration : lowDuration);
            lowDuration = NormalizeSimulationDuration(lowDuration > 0 ? lowDuration : highDuration);
            profiles[pinNumber] = new DigitalOutputProfile(mode, initialState, highDuration, lowDuration);
        }

        return profiles;
    }

    private static double? ParseFirstDelaySeconds(string code, IReadOnlyDictionary<string, long> constants)
    {
        var match = DelayRegex.Match(code);
        if (!match.Success || !TryResolveInteger(match.Groups["value"].Value, constants, out var value))
            return null;

        var divisor = match.Groups["kind"].Value.Equals("delayMicroseconds", StringComparison.OrdinalIgnoreCase)
            ? 1_000_000d
            : 1_000d;
        return NormalizeSimulationDuration(value / divisor);
    }

    private static double NormalizeSimulationDuration(double seconds) => Math.Clamp(seconds, 0.05, 10);

    private static PinResolution TryResolvePin(string expression, IReadOnlyDictionary<string, long> constants)
    {
        if (!TryResolveInteger(expression, constants, out var value))
        {
            var token = UnwrapParentheses(expression).Trim();
            var looksNumeric = Regex.IsMatch(token, @"^[+-]?(?:\d|0[xXbB])");
            return new PinResolution(looksNumeric ? PinResolutionStatus.InvalidRange : PinResolutionStatus.Unsupported, 0);
        }

        return value is >= 0 and <= 255
            ? new PinResolution(PinResolutionStatus.Success, (int)value)
            : new PinResolution(PinResolutionStatus.InvalidRange, 0);
    }

    private static bool TryResolveInteger(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        out long value)
    {
        var token = UnwrapParentheses(expression).Trim();
        if (constants.TryGetValue(token, out value))
            return true;

        var digitalPin = Regex.Match(token, @"^D(?<pin>\d+)$", RegexOptions.IgnoreCase);
        if (digitalPin.Success)
            return long.TryParse(digitalPin.Groups["pin"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        var analogPin = Regex.Match(token, @"^A(?<pin>\d+)$", RegexOptions.IgnoreCase);
        if (analogPin.Success && long.TryParse(analogPin.Groups["pin"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var channel))
        {
            value = 14 + channel;
            return true;
        }

        token = Regex.Replace(token, @"(?i)(?:u|l)+$", string.Empty);
        var sign = 1L;
        if (token.StartsWith('-'))
        {
            sign = -1;
            token = token[1..];
        }
        else if (token.StartsWith('+'))
        {
            token = token[1..];
        }

        try
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = checked(sign * Convert.ToInt64(token[2..], 16));
                return true;
            }

            if (token.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                value = checked(sign * Convert.ToInt64(token[2..], 2));
                return true;
            }

            return long.TryParse($"{(sign < 0 ? "-" : string.Empty)}{token}", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            value = 0;
            return false;
        }
    }

    private static bool TryResolveLevel(
        string expression,
        IReadOnlyDictionary<string, long> constants,
        out bool isHigh)
    {
        var token = UnwrapParentheses(expression).Trim();
        if (token.Equals("HIGH", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            isHigh = true;
            return true;
        }

        if (token.Equals("LOW", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            isHigh = false;
            return true;
        }

        if (TryResolveInteger(token, constants, out var numericLevel) && numericLevel is 0 or 1)
        {
            isHigh = numericLevel == 1;
            return true;
        }

        isHigh = false;
        return false;
    }

    private static string UnwrapParentheses(string expression)
    {
        var result = expression.Trim();
        while (result.Length >= 2 && result[0] == '(' && result[^1] == ')' &&
               HasBalancedDelimiters(result[1..^1]))
            result = result[1..^1].Trim();
        return result;
    }

    private static bool TryExtractFunctionBody(string code, string functionName, out string body)
    {
        var functionMatch = Regex.Match(
            code,
            $@"\bvoid\s+{Regex.Escape(functionName)}\s*\([^)]*\)\s*\{{",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!functionMatch.Success)
        {
            body = string.Empty;
            return false;
        }

        var openBraceIndex = functionMatch.Index + functionMatch.Length - 1;
        var depth = 0;
        for (var index = openBraceIndex; index < code.Length; index++)
        {
            if (code[index] == '{')
                depth++;
            else if (code[index] == '}')
                depth--;

            if (depth == 0)
            {
                body = code[(openBraceIndex + 1)..index];
                return true;
            }
        }

        body = string.Empty;
        return false;
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

    private static bool HasBalancedDelimiters(string code)
    {
        var stack = new Stack<char>();
        foreach (var character in code)
        {
            if (character is '{' or '(' or '[')
            {
                stack.Push(character);
                continue;
            }

            if (character is not ('}' or ')' or ']'))
                continue;
            if (stack.Count == 0)
                return false;

            var opening = stack.Pop();
            if (opening == '{' && character != '}' || opening == '(' && character != ')' ||
                opening == '[' && character != ']')
                return false;
        }

        return stack.Count == 0;
    }

    private sealed record WriteOperation(int PinNumber, bool IsHigh);

    private readonly record struct InputSource(ArduinoInputKind Kind, string Pin);

    private sealed record TimedOperation(bool IsWrite, int PinNumber, bool State, double DurationSeconds)
    {
        public static TimedOperation Write(int pinNumber, bool state) => new(true, pinNumber, state, 0);
        public static TimedOperation Delay(double durationSeconds) => new(false, -1, false, durationSeconds);
    }

    private readonly record struct PinResolution(PinResolutionStatus Status, int PinNumber);

    private enum PinResolutionStatus
    {
        Success,
        Unsupported,
        InvalidRange
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
