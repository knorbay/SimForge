namespace SimForge.Core;

public sealed class Connection
{
    public Connection(NodePin from, NodePin to)
    {
        if (ReferenceEquals(from, to))
            throw new ArgumentException("A pin cannot be connected to itself.", nameof(to));

        if (ReferenceEquals(from.Owner, to.Owner))
            throw new ArgumentException("Pins on the same node cannot be connected directly.", nameof(to));

        if (!AreCompatible(from, to))
            throw new ArgumentException(
                $"Pins '{from.Owner.Name}.{from.Name}' and '{to.Owner.Name}.{to.Name}' are not compatible.",
                nameof(to));

        Id = Guid.NewGuid();
        From = from;
        To = to;
    }

    public Guid Id { get; }
    public NodePin From { get; }
    public NodePin To { get; }

    public bool IsConnectedTo(NodePin pin) => ReferenceEquals(From, pin) || ReferenceEquals(To, pin);

    public static bool AreCompatible(NodePin first, NodePin second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.SignalType == PinSignalType.Mechanical || second.SignalType == PinSignalType.Mechanical)
            return first.SignalType == PinSignalType.Mechanical && second.SignalType == PinSignalType.Mechanical;

        if (first.Direction == PinDirection.Output && second.Direction == PinDirection.Output)
            return false;

        if (first.Direction == PinDirection.Input && second.Direction == PinDirection.Input)
            return false;

        if (first.SignalType == PinSignalType.Electrical || second.SignalType == PinSignalType.Electrical)
            return true;

        return first.SignalType == second.SignalType;
    }
}
