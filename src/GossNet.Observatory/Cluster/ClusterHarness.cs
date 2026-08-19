using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using GossNet.Observatory.Telemetry;
using GossNet.Observatory.Transport;
using GossNet.Protocol;

namespace GossNet.Observatory.Cluster;

/// <summary>
/// Builds and drives a cluster of gossip nodes on loopback UDP, with every node's
/// transport instrumented.
/// </summary>
internal sealed class ClusterHarness : IAsyncDisposable
{
    private readonly DelayedSendScheduler _scheduler;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<Task> _pumps = [];
    private readonly string _payload;

    private int _sequence;
    private int _disposed;

    private ClusterHarness(
        ClusterOptions options,
        IReadOnlyList<NodeHandle> nodes,
        NetworkConditions conditions,
        NetworkMonitor monitor,
        DelayedSendScheduler scheduler)
    {
        _scheduler = scheduler;
        _payload = new string('x', Math.Max(0, options.PayloadBytes));

        Nodes = nodes;
        Conditions = conditions;
        Monitor = monitor;
    }

    /// <summary>Gets the nodes, in index order.</summary>
    public IReadOnlyList<NodeHandle> Nodes { get; }

    /// <summary>Gets the mutable simulated network shared by every node's transport.</summary>
    public NetworkConditions Conditions { get; }

    /// <summary>Gets the telemetry sink.</summary>
    public NetworkMonitor Monitor { get; }

    /// <summary>Gets the counters.</summary>
    public Metrics Metrics => Monitor.Metrics;

    /// <summary>Gets the propagation tracker.</summary>
    public MessageTracker Tracker => Monitor.Tracker;

    /// <summary>Gets the number of nodes whose receive loop is running.</summary>
    public int AliveCount => Nodes.Count(node => node.IsAlive);

    /// <summary>Gets the nodes keyed by port, which is how telemetry identifies them.</summary>
    public IReadOnlyDictionary<int, NodeHandle> ByPort => field ??= Nodes.ToDictionary(node => node.Port);

    /// <summary>
    /// Wires up a cluster. Nodes are constructed but not started.
    /// </summary>
    /// <exception cref="InvalidOperationException">One of the required UDP ports is in use.</exception>
    public static ClusterHarness Create(ClusterOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.NodeCount, 2);

        var ports = Enumerable.Range(0, options.NodeCount).Select(i => options.BasePort + i).ToArray();

        EnsurePortsAvailable(ports);

        var adjacency = Topology.Build(options.Topology, options.NodeCount, options.Degree, options.Seed);

        var conditions = new NetworkConditions();
        var monitor = new NetworkMonitor(new MessageTracker(options.TrackerCapacity), new Metrics(ports), options.SettleWindow);
        var scheduler = new DelayedSendScheduler();

        var width = options.NodeCount >= 100 ? 3 : 2;
        var nodes = new List<NodeHandle>(options.NodeCount);

        for (var i = 0; i < options.NodeCount; i++)
        {
            var port = ports[i];

            var configuration = new GossNetConfiguration
            {
                Hostname = ClusterOptions.Host,
                Port = port,
                NodeDiscovery = NodeDiscovery.StaticList,
                StaticNodes = [.. adjacency[i].Select(neighbour => new GossNetNodeHostEntry
                {
                    Hostname = ClusterOptions.Host,
                    Port = ports[neighbour]
                })],
                MessageTtlSeconds = options.MessageTtlSeconds,
                SubscriberQueueCapacity = options.SubscriberQueueCapacity
            };

            var transport = new ObservingUdpClient(new UdpClientAdapter(port), port, conditions, monitor, scheduler);
            var node = new GossNetNode<ObservatoryMessage>(configuration, udpClient: transport);

            nodes.Add(new NodeHandle
            {
                Index = i,
                Name = $"n{(i + 1).ToString().PadLeft(width, '0')}",
                Port = port,
                Node = node,
                Subscription = node.Subscribe(),
                Neighbours = adjacency[i]
            });
        }

        return new ClusterHarness(options, nodes, conditions, monitor, scheduler);
    }

    /// <summary>Starts telemetry, every node, and the per-node delivery pumps.</summary>
    public void Start()
    {
        Monitor.Start();

        foreach (var handle in Nodes)
        {
            handle.Node.Start();
            _pumps.Add(Task.Run(() => PumpAsync(handle, _cancellation.Token), CancellationToken.None));
        }
    }

    /// <summary>
    /// Injects a new message at one node and begins tracking its propagation.
    /// </summary>
    /// <returns>The protocol message id, which is also the tracking key.</returns>
    public async Task<Guid> InjectAsync(int index, CancellationToken cancellationToken = default)
    {
        var handle = Nodes[index];
        var sequence = Interlocked.Increment(ref _sequence);

        var message = new ObservatoryMessage
        {
            Origin = handle.Name,
            Seq = sequence,
            Payload = _payload
        };

        // The id is assigned when the message is constructed, so the tracker can be
        // primed before the first datagram leaves. Registering afterwards would race
        // the receive events it is meant to explain.
        Tracker.OnOrigin(message.Id, sequence, handle.Port, Stopwatch.GetTimestamp());

        await handle.Node.SendAsync(message, cancellationToken).ConfigureAwait(false);

        return message.Id;
    }

    /// <summary>Stops a node's receive loop, leaving its socket bound.</summary>
    public async Task KillAsync(int index)
    {
        var handle = Nodes[index];

        if (!handle.IsAlive)
        {
            return;
        }

        await handle.Node.StopAsync().ConfigureAwait(false);
        handle.IsAlive = false;
    }

    /// <summary>Restarts a stopped node, which then drains whatever queued while it was down.</summary>
    public void Revive(int index)
    {
        var handle = Nodes[index];

        if (handle.IsAlive)
        {
            return;
        }

        handle.Node.Start();
        handle.IsAlive = true;
    }

    /// <summary>Splits the cluster in two halves that cannot reach each other.</summary>
    public void Partition()
    {
        var half = Nodes.Count / 2;

        for (var i = 0; i < Nodes.Count; i++)
        {
            Conditions.SetPartition(Nodes[i].Port, i < half ? 0 : 1);
        }
    }

    /// <summary>Heals a partition.</summary>
    public void Heal() => Conditions.ClearPartitions();

    private async Task PumpAsync(NodeHandle handle, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in handle.Subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                handle.Delivered++;

                // A node publishes to subscribers only on a first sighting, so every
                // envelope here is one accepted message -- this is how the observatory
                // tells acceptance apart from the duplicates the dedup cache absorbs.
                Monitor.Publish(new TransportEvent(
                    TransportEventKind.Accepted,
                    handle.Port,
                    handle.Port,
                    envelope.Message.Id,
                    0,
                    Stopwatch.GetTimestamp()));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (ChannelClosedException)
        {
            // The node was disposed, which completes the subscription.
        }
    }

    private static void EnsurePortsAvailable(IReadOnlyList<int> ports)
    {
        var taken = new List<int>();

        foreach (var port in ports)
        {
            try
            {
                using var probe = new UdpClient(port);
            }
            catch (SocketException)
            {
                taken.Add(port);
            }
        }

        if (taken.Count > 0)
        {
            throw new InvalidOperationException(
                $"UDP port(s) already in use: {string.Join(", ", taken)}. " +
                "Pass --base-port to move the cluster somewhere else.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);

        // Before the nodes: a queued delayed send holds a reference to a node's socket.
        await _scheduler.DisposeAsync().ConfigureAwait(false);

        foreach (var handle in Nodes)
        {
            handle.Subscription.Dispose();
            await handle.Node.DisposeAsync().ConfigureAwait(false);
        }

        try
        {
            await Task.WhenAll(_pumps).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Pumps end by cancellation or channel completion; neither is a failure.
        }

        await Monitor.DisposeAsync().ConfigureAwait(false);

        _cancellation.Dispose();
    }
}
