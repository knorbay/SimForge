namespace SimForge.Core;

public abstract class Node
{
    private readonly List<NodePin> _pins = new();
    private readonly Dictionary<string, double> _parameters = new();

    protected Node(string name, NodeKind kind)
    {
        Id = Guid.NewGuid();
        Name = name;
        Kind = kind;
    }

    public Guid Id { get; }
    public string Name { get; set; }
    public NodeKind Kind { get; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool IsEnabled { get; set; } = true;
    public double ComponentValue { get; set; } = 50.0;
    public bool SwitchState { get; set; } = false;

    public IReadOnlyList<NodePin> Pins => _pins;
    public IReadOnlyDictionary<string, double> Parameters => _parameters;

    public virtual bool AllowsCurrentPass() => true;

    public NodePin GetPin(string name) =>
        _pins.FirstOrDefault(pin => string.Equals(pin.Name, name, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"'{Name}' düğümünde '{name}' pini bulunamadı.");

    public bool TryGetParameter(string name, out double value) =>
        _parameters.TryGetValue(name, out value);

    public virtual void Initialize(SimulationContext context)
    {
    }

    public abstract void Step(SimulationContext context, double deltaTimeSeconds);

    public virtual void Reset()
    {
    }

    public virtual IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return "Bir node adı boş olamaz.";
    }

    public NodePin AddTerminal(string name, PinDirection direction, PinSignalType signalType) =>
        AddPin(name, direction, signalType);

    protected NodePin AddPin(string name, PinDirection direction, PinSignalType signalType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pin adı boş olamaz.", nameof(name));

        if (_pins.Any(pin => string.Equals(pin.Name, name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"'{Name}' düğümünde '{name}' adlı bir pin zaten var.");

        var pin = new NodePin(this, name, direction, signalType);
        _pins.Add(pin);
        return pin;
    }

    protected void SetParameter(string name, double value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parametre adı boş olamaz.", nameof(name));

        _parameters[name] = value;
    }
}

public class MicrocontrollerNode : Node
{
    public MicrocontrollerNode(string name) : base(name, NodeKind.Microcontroller)
    {
        AddTerminal("D13", PinDirection.Output, PinSignalType.Digital);
        AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
    }

    public override void Step(SimulationContext context, double deltaTimeSeconds)
    {
    }
}

public class SwitchNode : Node
{
    public SwitchNode(string name) : base(name, NodeKind.Electronic)
    {
        AddTerminal("A", PinDirection.Passive, PinSignalType.Analog);
        AddTerminal("B", PinDirection.Passive, PinSignalType.Analog);
    }

    public override bool AllowsCurrentPass() => SwitchState;

    public override void Step(SimulationContext context, double deltaTimeSeconds)
    {
    }
}

public class SensorNode : Node
{
    public double Threshold { get; set; } = 30.0;

    public bool TriggersAboveThreshold { get; set; } = true;

    public string Unit { get; set; } = "";

    public SensorNode(string name, double threshold = 30.0) : base(name, NodeKind.Sensor)
    {
        Threshold = threshold;
        AddTerminal("VCC", PinDirection.Input, PinSignalType.Power);
        AddTerminal("OUT", PinDirection.Output, PinSignalType.Analog);
        AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
    }

    public override bool AllowsCurrentPass() =>
        TriggersAboveThreshold ? ComponentValue >= Threshold : ComponentValue <= Threshold;

    public override void Step(SimulationContext context, double deltaTimeSeconds)
    {
    }
}

public sealed class GroundNode : Node
{
    public GroundNode(string name) : base(name, NodeKind.Electronic)
    {
        AddTerminal("GND", PinDirection.Passive, PinSignalType.Ground);
    }

    public override void Step(SimulationContext context, double deltaTimeSeconds)
    {
    }
}

public class LedNode : Node
{
    public LedNode(string name) : base(name, NodeKind.Actuator)
    {
        AddTerminal("Anot", PinDirection.Input, PinSignalType.Analog);
        AddTerminal("Katot", PinDirection.Passive, PinSignalType.Ground);
    }

    public override void Step(SimulationContext context, double deltaTimeSeconds)
    {
    }
}

public class PassiveNode : Node
{
    public bool IsCurrentLimiting { get; set; }

    public PassiveNode(string name) : base(name, NodeKind.Electronic)
    {
        AddTerminal("A", PinDirection.Passive, PinSignalType.Analog);
        AddTerminal("B", PinDirection.Passive, PinSignalType.Analog);
    }

    public override void Step(SimulationContext context, double deltaTimeSeconds)
    {
    }
}

public enum NodeKind
{
    Electronic,
    Microcontroller,
    Sensor,
    Actuator,
    Physics
}

public enum PinDirection
{
    Input,
    Output,
    Bidirectional,
    Passive
}

public enum PinSignalType
{
    Digital,
    Analog,
    Power,
    Ground,
    Mechanical
}

public sealed class NodePin
{
    internal NodePin(Node owner, string name, PinDirection direction, PinSignalType signalType)
    {
        Owner = owner;
        Name = name;
        Direction = direction;
        SignalType = signalType;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public Node Owner { get; }
    public string Name { get; }
    public PinDirection Direction { get; }
    public PinSignalType SignalType { get; }

    public double Value { get; internal set; }
}

public sealed class SimulationContext
{
    public SimulationContext(double timeSeconds)
    {
        TimeSeconds = timeSeconds;
    }

    public double TimeSeconds { get; }
}