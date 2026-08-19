using System.Collections.Concurrent;

namespace GossNet.Observatory.Transport;

/// <summary>
/// The simulated network every node's transport consults. Mutable while the cluster runs.
/// </summary>
/// <remarks>
/// Loss is stored per-mille as an <see cref="int"/> so reads and writes are atomic
/// without locking a value that is touched on every datagram from every node.
/// </remarks>
internal sealed class NetworkConditions
{
    private readonly ConcurrentDictionary<int, int> _partitionByPort = new();

    /// <summary>Gets or sets the probability a datagram is discarded, in parts per thousand.</summary>
    public int DropPerMille { get; set; }

    /// <summary>Gets or sets the base one-way delay applied to every datagram, in milliseconds.</summary>
    public int LatencyMs { get; set; }

    /// <summary>Gets or sets the extra uniform random delay added on top of <see cref="LatencyMs"/>.</summary>
    public int JitterMs { get; set; }

    /// <summary>Gets a value indicating whether any datagram currently needs delaying.</summary>
    public bool HasDelay => LatencyMs > 0 || JitterMs > 0;

    /// <summary>
    /// Assigns a node to a partition. Nodes in different partitions cannot exchange datagrams.
    /// </summary>
    public void SetPartition(int port, int partition) => _partitionByPort[port] = partition;

    /// <summary>Puts every node back in the same partition.</summary>
    public void ClearPartitions() => _partitionByPort.Clear();

    /// <summary>Gets a value indicating whether the network is currently split.</summary>
    public bool IsPartitioned => !_partitionByPort.IsEmpty;

    /// <summary>
    /// Determines whether a datagram may cross from one node to another.
    /// </summary>
    public bool CanReach(int fromPort, int toPort)
    {
        if (_partitionByPort.IsEmpty)
        {
            return true;
        }

        // An unassigned node is treated as partition 0 so a partial assignment still
        // behaves like a split rather than isolating everything.
        var from = _partitionByPort.GetValueOrDefault(fromPort, 0);
        var to = _partitionByPort.GetValueOrDefault(toPort, 0);

        return from == to;
    }

    /// <summary>
    /// Determines whether this datagram should be discarded.
    /// </summary>
    public bool ShouldDrop()
    {
        var perMille = DropPerMille;

        return perMille > 0 && Random.Shared.Next(1000) < perMille;
    }

    /// <summary>
    /// Gets the delay to apply to the next datagram.
    /// </summary>
    public TimeSpan NextDelay()
    {
        var jitter = JitterMs > 0 ? Random.Shared.Next(JitterMs + 1) : 0;

        return TimeSpan.FromMilliseconds(LatencyMs + jitter);
    }
}
