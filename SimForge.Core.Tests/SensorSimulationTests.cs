using SimForge.Core;
using Xunit;

namespace SimForge.Core.Tests;

public sealed class SensorSimulationTests
{
    [Fact]
    public void Photoresistor_UsesNonLinearVoltageDividerResponse()
    {
        var dark = SensorSimulation.ReadPhotoresistor(0);
        var middle = SensorSimulation.ReadPhotoresistor(50);
        var bright = SensorSimulation.ReadPhotoresistor(100);

        Assert.True(dark.Voltage < middle.Voltage);
        Assert.True(middle.Voltage < bright.Voltage);
        Assert.NotEqual((dark.AdcValue + bright.AdcValue) / 2, middle.AdcValue);
        Assert.InRange(bright.AdcValue, 900, 950);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 512)]
    [InlineData(100, 1023)]
    public void Potentiometer_MapsPositionToTenBitAdc(double position, int expectedAdc)
    {
        var reading = SensorSimulation.ReadPotentiometer(position);

        Assert.Equal(expectedAdc, reading.AdcValue);
    }

    [Fact]
    public void HcSr04_UsesTemperatureAdjustedRoundTripTime()
    {
        var reading = SensorSimulation.ReadHcSr04(100, 20);

        Assert.True(reading.IsInRange);
        Assert.InRange(reading.EchoDurationMicroseconds, 5_800, 5_850);
        Assert.InRange(SensorSimulation.DistanceFromEchoMicroseconds(reading.EchoDurationMicroseconds, 20), 99.99, 100.01);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(401)]
    public void HcSr04_MarksBlindZoneAndOutOfRangeTargets(double distance)
    {
        Assert.False(SensorSimulation.ReadHcSr04(distance).IsInRange);
    }

    [Fact]
    public void Dht11_ClampsAndQuantizesToDeviceResolution()
    {
        var reading = SensorSimulation.ReadDht11(30.6, 95);

        Assert.Equal(31, reading.TemperatureCelsius);
        Assert.Equal(90, reading.HumidityPercent);
        Assert.Equal(2, reading.MinimumSampleIntervalSeconds);
    }
}
