using System.Globalization;
using System.Text;
using GossNet.Observatory.Cluster;

namespace GossNet.Observatory.Bench;

/// <summary>
/// What the benchmark sweeps over.
/// </summary>
internal sealed record BenchOptions
{
    /// <summary>Gets the cluster sizes to test.</summary>
    public required IReadOnlyList<int> NodeCounts { get; init; }

    /// <summary>Gets the packet-loss rates to test, as fractions.</summary>
    public required IReadOnlyList<double> LossRates { get; init; }

    /// <summary>Gets the topologies to test.</summary>
    public required IReadOnlyList<TopologyKind> Topologies { get; init; }

    /// <summary>Gets the number of measured messages per combination.</summary>
    public int Messages { get; init; } = 200;

    /// <summary>Gets the first port of the first run.</summary>
    public int BasePort { get; init; } = 19100;

    /// <summary>Gets the random chords per node for k-random topologies.</summary>
    public int Degree { get; init; } = 2;

    /// <summary>Gets the topology seed.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Gets the filler bytes per message.</summary>
    public int PayloadBytes { get; init; }

    /// <summary>Gets where to write the CSV, or null to only print it.</summary>
    public string? OutputPath { get; init; }
}

/// <summary>
/// One row of results.
/// </summary>
internal readonly record struct BenchResult(
    int Nodes,
    double Loss,
    TopologyKind Topology,
    double P50Ms,
    double P99Ms,
    double DuplicateRatio,
    double Amplification,
    double Coverage);

/// <summary>
/// Runs the cluster headless and reports what gossip actually costs.
/// </summary>
internal static class BenchRunner
{
    private static readonly TimeSpan InjectInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);
    private const int WarmupMessages = 3;

    /// <summary>Executes the sweep and writes the CSV.</summary>
    public static async Task<int> RunAsync(BenchOptions options, CancellationToken cancellationToken)
    {
        var results = new List<BenchResult>();
        var port = options.BasePort;

        foreach (var topology in options.Topologies)
        {
            foreach (var nodes in options.NodeCounts)
            {
                foreach (var loss in options.LossRates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Console.Error.WriteLine(
                        $"running {topology.ToString().ToLowerInvariant(),-8} nodes={nodes,-4} loss={loss:P0} ...");

                    results.Add(await RunOneAsync(options, topology, nodes, loss, port, cancellationToken).ConfigureAwait(false));

                    // Each run gets a fresh port range: a just-closed UDP socket can
                    // linger long enough to make an immediate rebind flaky.
                    port += nodes + 4;
                }
            }
        }

        var csv = ToCsv(results);

        Console.WriteLine();
        Console.WriteLine(csv);

        var path = options.OutputPath ?? $"bench-{DateTime.Now:yyyyMMdd-HHmm}.csv";

        await File.WriteAllTextAsync(path, csv, cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine($"wrote {path}");

        return 0;
    }

    private static async Task<BenchResult> RunOneAsync(
        BenchOptions options,
        TopologyKind topology,
        int nodes,
        double loss,
        int basePort,
        CancellationToken cancellationToken)
    {
        await using var harness = ClusterHarness.Create(new ClusterOptions
        {
            NodeCount = nodes,
            BasePort = basePort,
            Topology = topology,
            Degree = options.Degree,
            Seed = options.Seed,
            PayloadBytes = options.PayloadBytes,
            // Every measured message must still be in the window when the run ends.
            TrackerCapacity = options.Messages + WarmupMessages + 8
        });

        harness.Conditions.DropPerMille = (int)Math.Round(loss * 1000);
        harness.Start();

        // Warm up first: the first message through a cluster pays for JIT, socket setup
        // and the first DNS-free discovery call, none of which is what we are measuring.
        for (var i = 0; i < WarmupMessages; i++)
        {
            await harness.InjectAsync(i % nodes, cancellationToken).ConfigureAwait(false);
            await Task.Delay(InjectInterval, cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
        await harness.Monitor.DrainAsync(cancellationToken).ConfigureAwait(false);
        harness.Tracker.Clear();

        for (var i = 0; i < options.Messages; i++)
        {
            await harness.InjectAsync(Random.Shared.Next(nodes), cancellationToken).ConfigureAwait(false);
            await Task.Delay(InjectInterval, cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
        await harness.Monitor.DrainAsync(cancellationToken).ConfigureAwait(false);

        var snapshots = harness.Tracker.AllSnapshots();
        var reached = snapshots.Where(snapshot => snapshot.NodesReached > 0).ToArray();

        if (reached.Length == 0)
        {
            return new BenchResult(nodes, loss, topology, 0, 0, 0, 0, 0);
        }

        var convergence = reached.Select(snapshot => snapshot.Convergence.TotalMilliseconds).Order().ToArray();

        long totalReached = reached.Sum(snapshot => (long)snapshot.NodesReached);
        long totalSent = reached.Sum(snapshot => (long)snapshot.DatagramsSent);
        long totalReceived = reached.Sum(snapshot => (long)snapshot.DatagramsReceived);

        return new BenchResult(
            nodes,
            loss,
            topology,
            Percentile(convergence, 50),
            Percentile(convergence, 99),
            (double)(totalReceived - totalReached) / totalReached,
            (double)totalSent / totalReached,
            reached.Average(snapshot => (double)snapshot.NodesReached / (nodes - 1)));
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile / 100d * ordered.Length) - 1;

        return ordered[Math.Clamp(rank, 0, ordered.Length - 1)];
    }

    private static string ToCsv(IEnumerable<BenchResult> results)
    {
        var builder = new StringBuilder();

        builder.AppendLine("nodes,loss,topology,p50_ms,p99_ms,dup_ratio,amplification,coverage");

        foreach (var result in results)
        {
            builder.AppendLine(string.Join(',',
            [
                result.Nodes.ToString(CultureInfo.InvariantCulture),
                result.Loss.ToString("0.00", CultureInfo.InvariantCulture),
                result.Topology.ToString().ToLowerInvariant(),
                result.P50Ms.ToString("0.0", CultureInfo.InvariantCulture),
                result.P99Ms.ToString("0.0", CultureInfo.InvariantCulture),
                result.DuplicateRatio.ToString("0.00", CultureInfo.InvariantCulture),
                result.Amplification.ToString("0.00", CultureInfo.InvariantCulture),
                result.Coverage.ToString("0.000", CultureInfo.InvariantCulture)
            ]));
        }

        return builder.ToString().TrimEnd();
    }
}
