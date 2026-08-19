using GossNet.Protocol;

namespace GossNet.Observatory.Cluster;

/// <summary>
/// One node in the observed cluster, together with the bits the observatory needs to
/// address, display and control it.
/// </summary>
internal sealed class NodeHandle
{
    /// <summary>Gets the zero-based index of the node within the cluster.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the display name, e.g. <c>n03</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the UDP port the node listens on. Doubles as its identity in telemetry.</summary>
    public required int Port { get; init; }

    /// <summary>Gets the node itself.</summary>
    public required GossNetNode<ObservatoryMessage> Node { get; init; }

    /// <summary>Gets the observatory's subscription to the node.</summary>
    public required IGossNetSubscription<ObservatoryMessage> Subscription { get; init; }

    /// <summary>Gets the neighbour indices this node was wired to.</summary>
    public required IReadOnlyList<int> Neighbours { get; init; }

    /// <summary>Gets or sets a value indicating whether the node's receive loop is running.</summary>
    /// <remarks>
    /// A "killed" node is stopped, not disposed: its socket stays bound, so datagrams
    /// sent while it is down are still queued by the OS and drain when it restarts.
    /// </remarks>
    public bool IsAlive { get; set; } = true;

    /// <summary>Gets the number of messages the observatory has read from this node.</summary>
    public long Delivered { get; set; }

    /// <summary>Gets the current depth of this node's subscriber queue, when the channel reports it.</summary>
    public int QueueDepth => Subscription.Reader.CanCount ? Subscription.Reader.Count : 0;
}
