using System.Text;
using GossNet.Observatory;
using GossNet.Observatory.Bench;
using GossNet.Observatory.Cluster;
using GossNet.Observatory.Tui;

Console.OutputEncoding = Encoding.UTF8;

var command = CommandLine.Parse(args);

if (command.HasFlag("help") || command.HasFlag("h"))
{
    PrintUsage();
    return 0;
}

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return command.HasFlag("bench")
        ? await RunBenchAsync(command, cancellation.Token)
        : await RunTuiAsync(command, cancellation.Token);
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine(command.HasFlag("debug") ? ex.ToString() : $"error: {ex.Message}");
    return 1;
}

static async Task<int> RunTuiAsync(CommandLine command, CancellationToken cancellationToken)
{
    ObservatoryApp.EnsureConsoleIsBigEnough();

    var options = new ClusterOptions
    {
        NodeCount = command.Int("nodes", 8),
        BasePort = command.Int("base-port", 19100),
        Topology = command.Topologies("topology", [TopologyKind.Ring])[0],
        Degree = command.Int("degree", 2),
        Seed = command.Int("seed", 1),
        PayloadBytes = command.Int("payload", 0),
        MessageTtlSeconds = command.Int("ttl", 60),
        SubscriberQueueCapacity = command.Int("queue", 1024)
    };

    await using var harness = ClusterHarness.Create(options);

    harness.Conditions.DropPerMille = (int)Math.Round(command.Double("loss", 0) * 1000);
    harness.Conditions.LatencyMs = command.Int("latency", 0);
    harness.Conditions.JitterMs = harness.Conditions.LatencyMs / 2;

    harness.Start();

    await new ObservatoryApp(harness, options).RunAsync(command.HasFlag("auto"), cancellationToken);

    return 0;
}

static async Task<int> RunBenchAsync(CommandLine command, CancellationToken cancellationToken)
{
    var options = new BenchOptions
    {
        NodeCounts = command.Ints("nodes", [5, 10, 25]),
        LossRates = command.Doubles("loss", [0, 0.05]),
        Topologies = command.Topologies("topology", [TopologyKind.Mesh, TopologyKind.Ring]),
        Messages = command.Int("messages", 100),
        BasePort = command.Int("base-port", 19100),
        Degree = command.Int("degree", 2),
        Seed = command.Int("seed", 1),
        PayloadBytes = command.Int("payload", 0),
        OutputPath = command.String("out")
    };

    return await BenchRunner.RunAsync(options, cancellationToken);
}

static void PrintUsage() => Console.WriteLine(
    """
    GossNet Observatory — watch a gossip protocol flood a cluster.

    Runs a real GossNet.Protocol cluster over loopback UDP with every node's transport
    instrumented, so each message's propagation path, cost and convergence are visible.

    USAGE
      gossnet-observatory [options]            interactive view
      gossnet-observatory --bench [options]    headless measurement sweep

    CLUSTER
      --nodes N            nodes to run (default 8; comma-separated list in --bench)
      --topology KIND      mesh | ring | krandom | grid (default ring;
                           comma-separated list in --bench, default mesh,ring)
      --degree K           random chords per node for krandom (default 2)
      --seed S             topology seed, for reproducible runs (default 1)
      --base-port P        first UDP port; node i binds P+i (default 19100)
      --payload BYTES      filler per message (default 0)
      --ttl SECONDS        how long a node remembers a message id (default 60)
      --queue N            per-subscriber queue bound (default 1024)

    NETWORK
      --loss RATE          packet loss as a fraction, e.g. 0.05
                           (comma-separated list in --bench, default 0,0.05)
      --latency MS         one-way delay; jitter is set to half of it (default 0)
      --auto               start with continuous injection already running

    BENCH
      --messages N         measured messages per combination (default 100)
      --out PATH           CSV destination (default bench-<timestamp>.csv)

    KEYS (interactive)
      space   inject a message at a random live node
      1-9     inject at a specific node
      arrows  move the selection
      k / r   kill / revive the selected node
      p       split the cluster in half, or heal it
      d / D   decrease / increase packet loss
      l / L   decrease / increase latency
      a       toggle continuous injection
      t / f   pin an earlier message / follow the latest again
      q       quit

    A killed node is stopped, not disposed: its socket stays bound, so datagrams sent
    while it is down queue up and drain when it is revived.
    """);
