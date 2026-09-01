namespace SimForge.Core;

public readonly record struct AnalogSensorReading(
    double Voltage,
    int AdcValue,
    int AdcMaximum,
    double SourceResistanceOhms);

public readonly record struct UltrasonicSensorReading(
    double DistanceCentimeters,
    double EchoDurationMicroseconds,
    double SpeedOfSoundMetersPerSecond,
    bool IsInRange);

public readonly record struct Dht11SensorReading(
    double TemperatureCelsius,
    double HumidityPercent,
    double MinimumSampleIntervalSeconds,
    double TemperatureAccuracyCelsius);

public static class SensorSimulation
{
    public const double DefaultSupplyVoltage = 5;
    public const int DefaultAdcMaximum = 1023;

    public static AnalogSensorReading ReadPhotoresistor(
        double ambientLightPercent,
        double supplyVoltage = DefaultSupplyVoltage,
        double fixedResistanceOhms = 10_000,
        int adcMaximum = DefaultAdcMaximum)
    {
        ValidateAnalogArguments(supplyVoltage, fixedResistanceOhms, adcMaximum);
        var normalizedLight = Math.Clamp(ambientLightPercent, 0, 100) / 100d;

        // A practical LDR spans roughly 1 MΩ in darkness to 1 kΩ in strong light.
        // Interpolating logarithmically produces the non-linear response seen in real dividers.
        var ldrResistance = Math.Pow(10, 6 - (3 * normalizedLight));
        var voltage = supplyVoltage * fixedResistanceOhms / (ldrResistance + fixedResistanceOhms);
        return CreateAnalogReading(voltage, supplyVoltage, adcMaximum, ldrResistance);
    }

    public static AnalogSensorReading ReadPotentiometer(
        double wiperPositionPercent,
        double supplyVoltage = DefaultSupplyVoltage,
        double totalResistanceOhms = 10_000,
        int adcMaximum = DefaultAdcMaximum)
    {
        ValidateAnalogArguments(supplyVoltage, totalResistanceOhms, adcMaximum);
        var normalizedPosition = Math.Clamp(wiperPositionPercent, 0, 100) / 100d;
        var voltage = supplyVoltage * normalizedPosition;
        var sourceResistance = totalResistanceOhms * normalizedPosition * (1 - normalizedPosition);
        return CreateAnalogReading(voltage, supplyVoltage, adcMaximum, sourceResistance);
    }

    public static UltrasonicSensorReading ReadHcSr04(
        double targetDistanceCentimeters,
        double ambientTemperatureCelsius = 20)
    {
        var speedOfSound = 331.3 + (0.606 * ambientTemperatureCelsius);
        var inRange = targetDistanceCentimeters is >= 2 and <= 400;
        var clampedDistance = Math.Clamp(targetDistanceCentimeters, 0, 1_000);
        var roundTripMeters = clampedDistance * 2 / 100d;
        var echoDurationMicroseconds = roundTripMeters / speedOfSound * 1_000_000d;
        return new UltrasonicSensorReading(
            targetDistanceCentimeters,
            echoDurationMicroseconds,
            speedOfSound,
            inRange);
    }

    public static double DistanceFromEchoMicroseconds(
        double echoDurationMicroseconds,
        double ambientTemperatureCelsius = 20)
    {
        if (echoDurationMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(echoDurationMicroseconds));

        var speedOfSound = 331.3 + (0.606 * ambientTemperatureCelsius);
        return echoDurationMicroseconds / 1_000_000d * speedOfSound / 2 * 100;
    }

    public static Dht11SensorReading ReadDht11(double temperatureCelsius, double humidityPercent = 55)
    {
        var clampedTemperature = Math.Clamp(temperatureCelsius, 0, 50);
        var clampedHumidity = Math.Clamp(humidityPercent, 20, 90);
        return new Dht11SensorReading(
            Math.Round(clampedTemperature, MidpointRounding.AwayFromZero),
            Math.Round(clampedHumidity, MidpointRounding.AwayFromZero),
            2,
            2);
    }

    private static AnalogSensorReading CreateAnalogReading(
        double voltage,
        double supplyVoltage,
        int adcMaximum,
        double sourceResistanceOhms)
    {
        var adcValue = (int)Math.Round(voltage / supplyVoltage * adcMaximum, MidpointRounding.AwayFromZero);
        return new AnalogSensorReading(voltage, Math.Clamp(adcValue, 0, adcMaximum), adcMaximum, sourceResistanceOhms);
    }

    private static void ValidateAnalogArguments(double supplyVoltage, double resistanceOhms, int adcMaximum)
    {
        if (supplyVoltage <= 0)
            throw new ArgumentOutOfRangeException(nameof(supplyVoltage));
        if (resistanceOhms <= 0)
            throw new ArgumentOutOfRangeException(nameof(resistanceOhms));
        if (adcMaximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(adcMaximum));
    }
}
