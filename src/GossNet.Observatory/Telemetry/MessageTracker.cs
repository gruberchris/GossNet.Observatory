using System.Diagnostics;

namespace GossNet.Observatory.Telemetry;

/// <summary>
/// One node's place in a message's propagation.
/// </summary>
/// <param name="Port">The node.</param>
/// <param name="ParentPort">The node it first heard the message from.</param>
/// <param name="Delay">How long after the origin sent that first arrival was.</param>
/// <param name="Accepted">Whether the node published the message rather than discarding it as a duplicate.</param>
internal readonly record struct HopNode(int Port, int ParentPort, TimeSpan Delay, bool Accepted);

/// <summary>
/// An immutable view of one message's propagation, safe to render off the ingest thread.
/// </summary>
internal sealed record MessageSnapshot(
    Guid Id,
    int Seq,
    int OriginPort,
    IReadOnlyList<HopNode> Hops,
    IReadOnlyDictionary<int, IReadOnlyList<int>> Children,
    int NodesReached,
    int DatagramsSent,
    int DatagramsDropped,
    int DatagramsReceived,
    TimeSpan Convergence)
{
    /// <summary>Gets duplicate datagrams received per useful delivery.</summary>
    public double DuplicateRatio => NodesReached == 0 ? 0 : (double)(DatagramsReceived - NodesReached) / NodesReached;

    /// <summary>Gets datagrams put on the wire per node actually reached.</summary>
    public double Amplification => NodesReached == 0 ? 0 : (double)DatagramsSent / NodesReached;
}

/// <summary>
/// Reconstructs how each message spread.
/// </summary>
/// <remarks>
/// <para>
/// The parent of a node is whoever it first heard the message from, which comes from the
/// transport's receive events, not from the message itself: <c>NotifiedNodes</c> is a set
/// of who has seen a message, not a record of who told whom.
/// </para>
/// <para>
/// Only the most recent messages are retained. A long-running session would otherwise
/// accumulate one entry per message forever.
/// </para>
/// </remarks>
internal sealed class MessageTracker(int capacity = 50)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Queue<Guid> _order = new();

    /// <summary>
    /// Begins tracking a message about to be injected.
    /// </summary>
    public void OnOrigin(Guid id, int seq, int originPort, long timestamp)
    {
        lock (_gate)
        {
            if (!_entries.TryAdd(id, new Entry(seq, originPort, timestamp)))
            {
                return;
            }

            _order.Enqueue(id);

            while (_order.Count > capacity)
            {
                _entries.Remove(_order.Dequeue());
            }
        }
    }

    /// <summary>
    /// Folds a transport observation into the message it belongs to.
    /// </summary>
    /// <remarks>
    /// Events for messages that are no longer tracked are ignored: they have already
    /// aged out of the window the observatory displays.
    /// </remarks>
    public void OnEvent(in TransportEvent transportEvent)
    {
        if (transportEvent.MessageId == Guid.Empty)
        {
            return;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(transportEvent.MessageId, out var entry))
            {
                return;
            }

            switch (transportEvent.Kind)
            {
                case TransportEventKind.Sent:
                    entry.DatagramsSent++;
                    break;

                case TransportEventKind.Dropped:
                    entry.DatagramsDropped++;
                    break;

                case TransportEventKind.Received:
                    entry.DatagramsReceived++;

                    // Only the first arrival defines the edge; later copies are the
                    // duplicates the protocol exists to suppress.
                    if (!entry.FirstReceive.ContainsKey(transportEvent.AtPort))
                    {
                        entry.FirstReceive[transportEvent.AtPort] = (transportEvent.FromPort, transportEvent.Timestamp);
                    }

                    break;

                case TransportEventKind.Accepted:
                    if (entry.Accepted.Add(transportEvent.AtPort))
                    {
                        entry.LastAcceptTimestamp = transportEvent.Timestamp;
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Gets the tracked message ids, newest last.</summary>
    public IReadOnlyList<Guid> RecentIds()
    {
        lock (_gate)
        {
            return [.. _order];
        }
    }

    /// <summary>Gets the most recently injected message, if any.</summary>
    public Guid? LatestId()
    {
        lock (_gate)
        {
            return _order.Count == 0 ? null : _order.Last();
        }
    }

    /// <summary>Builds an immutable view of one message.</summary>
    public MessageSnapshot? Snapshot(Guid id)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(id, out var entry) ? Build(id, entry) : null;
        }
    }

    /// <summary>
    /// Returns messages that have stopped spreading, once each.
    /// </summary>
    /// <param name="settleWindow">
    /// How long after injection a message is considered done. Gossip has no completion
    /// signal, so "finished" can only ever mean "nothing new for a while".
    /// </param>
    public IReadOnlyList<MessageSnapshot> TakeSettled(TimeSpan settleWindow)
    {
        List<MessageSnapshot>? settled = null;
        var now = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            foreach (var id in _order)
            {
                var entry = _entries[id];

                if (entry.Finalized || Stopwatch.GetElapsedTime(entry.StartTimestamp, now) < settleWindow)
                {
                    continue;
                }

                entry.Finalized = true;
                settled ??= [];
                settled.Add(Build(id, entry));
            }
        }

        return (IReadOnlyList<MessageSnapshot>?)settled ?? [];
    }

    /// <summary>Builds immutable views of every tracked message.</summary>
    public IReadOnlyList<MessageSnapshot> AllSnapshots()
    {
        lock (_gate)
        {
            return [.. _order.Select(id => Build(id, _entries[id]))];
        }
    }

    /// <summary>Discards all tracked messages.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    private static MessageSnapshot Build(Guid id, Entry entry)
    {
        var hops = new List<HopNode>(entry.FirstReceive.Count);
        var children = new Dictionary<int, List<int>>();

        foreach (var (port, (parentPort, timestamp)) in entry.FirstReceive)
        {
            hops.Add(new HopNode(
                port,
                parentPort,
                Stopwatch.GetElapsedTime(entry.StartTimestamp, timestamp),
                entry.Accepted.Contains(port)));

            // A node whose parent is not itself in the tree (its first-receive event was
            // lost to eviction, say) would otherwise vanish from the render; hanging it
            // off the origin keeps the tree total.
            var parent = entry.FirstReceive.ContainsKey(parentPort) || parentPort == entry.OriginPort
                ? parentPort
                : entry.OriginPort;

            if (!children.TryGetValue(parent, out var list))
            {
                children[parent] = list = [];
            }

            list.Add(port);
        }

        hops.Sort(static (a, b) => a.Delay.CompareTo(b.Delay));

        foreach (var list in children.Values)
        {
            list.Sort();
        }

        var convergence = entry.LastAcceptTimestamp == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(entry.StartTimestamp, entry.LastAcceptTimestamp);

        return new MessageSnapshot(
            id,
            entry.Seq,
            entry.OriginPort,
            hops,
            children.ToDictionary(pair => pair.Key, IReadOnlyList<int> (pair) => pair.Value),
            entry.Accepted.Count,
            entry.DatagramsSent,
            entry.DatagramsDropped,
            entry.DatagramsReceived,
            convergence);
    }

    private sealed class Entry(int seq, int originPort, long startTimestamp)
    {
        public int Seq { get; } = seq;

        public int OriginPort { get; } = originPort;

        public long StartTimestamp { get; } = startTimestamp;

        public Dictionary<int, (int FromPort, long Timestamp)> FirstReceive { get; } = [];

        public HashSet<int> Accepted { get; } = [];

        public long LastAcceptTimestamp { get; set; }

        /// <summary>Set once the message has been handed to <see cref="Metrics"/>, so it is measured once.</summary>
        public bool Finalized { get; set; }

        public int DatagramsSent { get; set; }

        public int DatagramsDropped { get; set; }

        public int DatagramsReceived { get; set; }
    }
}
