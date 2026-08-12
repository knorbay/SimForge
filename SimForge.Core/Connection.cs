namespace SimForge.Core;

public sealed class Connection
{
    public Connection(NodePin from, NodePin to)
    {
        if (ReferenceEquals(from, to))
            throw new ArgumentException("Bir pin kendisine bağlanamaz.", nameof(to));

        if (ReferenceEquals(from.Owner, to.Owner))
            throw new ArgumentException("Aynı Node üzerindeki pinler doğrudan bağlanamaz.", nameof(to));

        if (!AreCompatible(from.SignalType, to.SignalType))
            throw new ArgumentException(
                $"'{from.SignalType}' ve '{to.SignalType}' pinleri doğrudan bağlanamaz.",
                nameof(to));

        Id = Guid.NewGuid();
        From = from;
        To = to;
    }

    public Guid Id { get; }
    public NodePin From { get; }
    public NodePin To { get; }

    public bool IsConnectedTo(NodePin pin) => ReferenceEquals(From, pin) || ReferenceEquals(To, pin);

    private static bool AreCompatible(PinSignalType first, PinSignalType second) =>
        (first == PinSignalType.Mechanical) == (second == PinSignalType.Mechanical);
}
