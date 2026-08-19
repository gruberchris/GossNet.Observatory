namespace GossNet.Observatory.Telemetry;

/// <summary>
/// Running counters for one node.
/// </summary>
/// <remarks>
/// Written only by the single ingest pump and read by the renderer, so reads may lag by
/// a frame. That is the right trade for a display: locking every counter would put a
/// contended lock on the datagram path.
/// </remarks>
internal sealed class NodeStats
{
    /// <summary>Gets or sets datagrams the node's socket handed up.</summary>
    public long Received { get; set; }

    /// <summary>Gets or sets messages the node published to subscribers — first sightings only.</summary>
    public long Accepted { get; set; }

    /// <summary>Gets or sets datagrams the node put on the wire.</summary>
    public long Sent { get; set; }

    /// <summary>Gets or sets datagrams the simulated network discarded on this node's behalf.</summary>
    public long Dropped { get; set; }

    /// <summary>Gets or sets the timestamp of the most recent accept, used to pulse the node's cell.</summary>
    public long LastAcceptTimestamp { get; set; }

    /// <summary>Gets datagrams received and then discarded as duplicates.</summary>
    public long Deduplicated => Math.Max(0, Received - Accepted);
}

/// <summary>
/// Cluster-wide and per-node counters, plus the convergence distribution.
/// </summary>
internal sealed class Metrics
{
    private const int MaxSamples = 4096;

    /// <summary>Upper bounds, in milliseconds, of the convergence histogram buckets.</summary>
    private static readonly double[] BucketBounds = [1, 2, 5, 10, 25, 50, 100, double.PositiveInfinity];

    private static readonly string[] BucketLabels = ["<1ms", "1-2", "2-5", "5-10", "10-25", "25-50", "50-100", "100ms+"];

    private readonly Dictionary<int, NodeStats> _byPort;
    private readonly Lock _sampleGate = new();
    private readonly List<double> _convergenceMs = [];
    private readonly long[] _buckets = new long[BucketBounds.Length];

    /// <summary>Initializes counters for a fixed set of node ports.</summary>
    public Metrics(IEnumerable<int> ports) =>
        _byPort = ports.ToDictionary(port => port, _ => new NodeStats());

    /// <summary>Gets the per-node counters.</summary>
    public IReadOnlyDictionary<int, NodeStats> ByPort => _byPort;

    /// <summary>Gets the number of messages that have been measured.</summary>
    public int ConvergenceSampleCount
    {
        get
        {
            lock (_sampleGate)
            {
                return _convergenceMs.Count;
            }
        }
    }

    /// <summary>Gets the histogram bucket labels.</summary>
    public static IReadOnlyList<string> HistogramLabels => BucketLabels;

    /// <summary>Folds a transport observation into the counters.</summary>
    public void Record(in TransportEvent transportEvent)
    {
        switch (transportEvent.Kind)
        {
            case TransportEventKind.Sent:
                if (_byPort.TryGetValue(transportEvent.FromPort, out var sender))
                {
                    sender.Sent++;
                }

                break;

            case TransportEventKind.Dropped:
                if (_byPort.TryGetValue(transportEvent.FromPort, out var dropper))
                {
                    dropper.Dropped++;
                }

                break;

            case TransportEventKind.Received:
                if (_byPort.TryGetValue(transportEvent.AtPort, out var receiver))
                {
                    receiver.Received++;
                }

                break;

            case TransportEventKind.Accepted:
                if (_byPort.TryGetValue(transportEvent.AtPort, out var acceptor))
                {
                    acceptor.Accepted++;
                    acceptor.LastAcceptTimestamp = transportEvent.Timestamp;
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Adds one message's convergence time to the distribution.</summary>
    public void RecordConvergence(TimeSpan convergence)
    {
        var milliseconds = convergence.TotalMilliseconds;

        lock (_sampleGate)
        {
            if (_convergenceMs.Count == MaxSamples)
            {
                // Drop the oldest half rather than growing without bound; percentiles
                // then describe recent behaviour, which is what a live view wants.
                _convergenceMs.RemoveRange(0, MaxSamples / 2);
            }

            _convergenceMs.Add(milliseconds);

            for (var i = 0; i < BucketBounds.Length; i++)
            {
                if (milliseconds < BucketBounds[i])
                {
                    _buckets[i]++;
                    break;
                }
            }
        }
    }

    /// <summary>Gets a copy of the histogram counts, aligned with <see cref="HistogramLabels"/>.</summary>
    public long[] Histogram()
    {
        lock (_sampleGate)
        {
            return [.. _buckets];
        }
    }

    /// <summary>Gets a convergence percentile in milliseconds, or zero when nothing is measured yet.</summary>
    public double Percentile(double percentile)
    {
        lock (_sampleGate)
        {
            if (_convergenceMs.Count == 0)
            {
                return 0;
            }

            var ordered = _convergenceMs.Order().ToArray();
            var rank = (int)Math.Ceiling(percentile / 100d * ordered.Length) - 1;

            return ordered[Math.Clamp(rank, 0, ordered.Length - 1)];
        }
    }

    /// <summary>Gets cluster-wide totals.</summary>
    public ClusterTotals Totals()
    {
        long received = 0, accepted = 0, sent = 0, dropped = 0;

        foreach (var stats in _byPort.Values)
        {
            received += stats.Received;
            accepted += stats.Accepted;
            sent += stats.Sent;
            dropped += stats.Dropped;
        }

        return new ClusterTotals(received, accepted, sent, dropped);
    }
}

/// <summary>
/// Cluster-wide datagram totals.
/// </summary>
/// <param name="Received">Datagrams delivered to sockets.</param>
/// <param name="Accepted">First sightings published to subscribers.</param>
/// <param name="Sent">Datagrams put on the wire.</param>
/// <param name="Dropped">Datagrams discarded by the simulated network.</param>
internal readonly record struct ClusterTotals(long Received, long Accepted, long Sent, long Dropped)
{
    /// <summary>Gets wasted duplicate datagrams per useful delivery.</summary>
    public double DuplicateRatio => Accepted == 0 ? 0 : (double)(Received - Accepted) / Accepted;

    /// <summary>Gets datagrams put on the wire per useful delivery.</summary>
    public double Amplification => Accepted == 0 ? 0 : (double)Sent / Accepted;
}
