using SimForge.Core;
using Xunit;

namespace SimForge.Core.Tests;

public sealed class GraphTests
{
    [Fact]
    public void Connect_AddsCompatibleElectricalPins()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var led = new LedNode("Status LED");
        graph.AddNode(controller);
        graph.AddNode(led);

        var connection = graph.Connect(controller.GetPin("D13"), led.GetPin("Anode"));

        Assert.Single(graph.Connections);
        Assert.Same(controller.GetPin("D13"), connection.From);
        Assert.Same(led.GetPin("Anode"), connection.To);
    }

    [Fact]
    public void Connect_RejectsAnalogSensorOutputToDigitalControllerInput()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var sensor = new SensorNode("Sensor");
        graph.AddNode(controller);
        graph.AddNode(sensor);

        Assert.Throws<ArgumentException>(() =>
            graph.Connect(sensor.GetPin("OUT"), controller.GetPin("D2")));
    }

    [Fact]
    public void Connect_AllowsDigitalOutputToDigitalInput()
    {
        var graph = new Graph();
        var first = new MicrocontrollerNode("First");
        var second = new MicrocontrollerNode("Second");
        graph.AddNode(first);
        graph.AddNode(second);

        graph.Connect(first.GetPin("D13"), second.GetPin("D2"));

        Assert.Single(graph.Connections);
    }

    [Fact]
    public void Connect_RejectsPowerOutputToDigitalInput()
    {
        var graph = new Graph();
        var first = new MicrocontrollerNode("First");
        var second = new MicrocontrollerNode("Second");
        graph.AddNode(first);
        graph.AddNode(second);

        Assert.Throws<ArgumentException>(() =>
            graph.Connect(first.GetPin("5V"), second.GetPin("D2")));
    }

    [Fact]
    public void Connect_AllowsPassiveElectricalTerminalToGround()
    {
        var graph = new Graph();
        var resistor = new PassiveNode("Resistor");
        var ground = new GroundNode("Ground");
        graph.AddNode(resistor);
        graph.AddNode(ground);

        graph.Connect(resistor.GetPin("B"), ground.GetPin("GND"));

        Assert.Single(graph.Connections);
    }

    [Fact]
    public void Connect_RejectsSignalToGround()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var ground = new GroundNode("Ground");
        graph.AddNode(controller);
        graph.AddNode(ground);

        var error = Assert.Throws<ArgumentException>(() =>
            graph.Connect(controller.GetPin("D13"), ground.GetPin("GND")));

        Assert.Contains("not compatible", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connect_AllowsGroundToGround()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var ground = new GroundNode("Ground");
        graph.AddNode(controller);
        graph.AddNode(ground);

        graph.Connect(controller.GetPin("GND"), ground.GetPin("GND"));

        Assert.Single(graph.Connections);
    }

    [Fact]
    public void Connect_RejectsTwoOutputs()
    {
        var graph = new Graph();
        var first = new MicrocontrollerNode("First");
        var second = new MicrocontrollerNode("Second");
        graph.AddNode(first);
        graph.AddNode(second);

        Assert.Throws<ArgumentException>(() =>
            graph.Connect(first.GetPin("D13"), second.GetPin("D13")));
    }

    [Fact]
    public void Connect_RejectsPinsOutsideGraph()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var led = new LedNode("Status LED");
        graph.AddNode(controller);

        var error = Assert.Throws<InvalidOperationException>(() =>
            graph.Connect(controller.GetPin("D13"), led.GetPin("Anode")));

        Assert.Equal("Both pins must belong to nodes in this graph.", error.Message);
    }

    [Fact]
    public void Connect_RejectsDuplicateConnection()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var led = new LedNode("Status LED");
        graph.AddNode(controller);
        graph.AddNode(led);
        graph.Connect(controller.GetPin("D13"), led.GetPin("Anode"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            graph.Connect(led.GetPin("Anode"), controller.GetPin("D13")));

        Assert.Equal("These two pins are already connected.", error.Message);
    }

    [Fact]
    public void Connect_RejectsPinsOnSameNode()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        graph.AddNode(controller);

        Assert.Throws<ArgumentException>(() =>
            graph.Connect(controller.GetPin("D13"), controller.GetPin("GND")));
    }

    [Fact]
    public void RemoveNode_AlsoRemovesAttachedConnections()
    {
        var graph = new Graph();
        var controller = new MicrocontrollerNode("Controller");
        var led = new LedNode("Status LED");
        graph.AddNode(controller);
        graph.AddNode(led);
        graph.Connect(controller.GetPin("D13"), led.GetPin("Anode"));

        var removed = graph.RemoveNode(led);

        Assert.True(removed);
        Assert.DoesNotContain(led, graph.Nodes);
        Assert.Empty(graph.Connections);
    }

    [Fact]
    public void LedPins_UseEnglishNames()
    {
        var led = new LedNode("Status LED");

        Assert.Equal(["Anode", "Cathode"], led.Pins.Select(pin => pin.Name));
    }

    [Fact]
    public void Validate_ReturnsEnglishMessageForEmptyName()
    {
        var led = new LedNode(string.Empty);

        Assert.Equal("A node name cannot be empty.", Assert.Single(led.Validate()));
    }
}
