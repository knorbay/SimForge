namespace SimForge.Core;

public class Graph
{
    public List<Node> Nodes { get; } = new();

    public List<Connection> Connections { get; } = new();

    public void AddNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Nodes.Contains(node))
            Nodes.Add(node);
    }

    public Connection Connect(NodePin from, NodePin to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (!Nodes.Contains(from.Owner) || !Nodes.Contains(to.Owner))
            throw new InvalidOperationException("Both pins must belong to nodes in this graph.");

        if (Connections.Any(connection =>
                (ReferenceEquals(connection.From, from) && ReferenceEquals(connection.To, to)) ||
                (ReferenceEquals(connection.From, to) && ReferenceEquals(connection.To, from))))
            throw new InvalidOperationException("These two pins are already connected.");

        var connection = new Connection(from, to);
        Connections.Add(connection);
        return connection;
    }

    public bool RemoveConnection(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return Connections.Remove(connection);
    }

    public bool RemoveNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Connections.RemoveAll(connection =>
            ReferenceEquals(connection.From.Owner, node) || ReferenceEquals(connection.To.Owner, node));
        return Nodes.Remove(node);
    }
}
