using GossNet.Observatory.Cluster;
using GossNet.Observatory.Telemetry;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui.Panels;

/// <summary>
/// The path one message actually took through the cluster.
/// </summary>
/// <remarks>
/// Each node hangs off whoever it first heard the message from. A flat tree means the
/// origin reached everyone directly — the signature of a full mesh. A deep tree means
/// the message was relayed, which is gossip doing what it exists to do.
/// </remarks>
internal static class PropagationPanel
{
    /// <summary>Builds the propagation tree.</summary>
    public static IRenderable Build(ClusterHarness harness, MessageSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new Panel(new Markup("[grey]No message tracked yet. Press [bold]space[/] to inject one.[/]"))
                .Header("[bold]PROPAGATION[/]")
                .Border(BoxBorder.Rounded)
                .Expand();
        }

        var originName = Name(harness, snapshot.OriginPort);
        var tree = new Tree($"[bold yellow]{originName}[/] [grey](origin)[/]");

        AddChildren(tree, harness, snapshot, snapshot.OriginPort, depth: 0);

        var reached = snapshot.NodesReached;
        var others = Math.Max(1, harness.AliveCount - 1);
        var coverage = 100d * reached / others;

        var coverageMarkup = reached >= others
            ? $"[green]{coverage:0}%[/]"
            : $"[yellow]{coverage:0}%[/]";

        var header =
            $"[bold]PROPAGATION[/]  [grey]#{snapshot.Seq}[/]  " +
            $"reached [bold]{reached}[/]/{others} ({coverageMarkup})  " +
            $"in [bold]{snapshot.Convergence.TotalMilliseconds:0.0}ms[/]  " +
            $"cost [bold]{snapshot.DatagramsSent}[/] datagrams " +
            $"([grey]amp {snapshot.Amplification:0.0}x, dup {snapshot.DuplicateRatio:0.0}x[/])";

        return new Panel(tree)
            .Header(header)
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static void AddChildren(IHasTreeNodes parent, ClusterHarness harness, MessageSnapshot snapshot, int port, int depth)
    {
        // Defensive: a corrupt parent chain would otherwise recurse forever.
        if (depth > 64 || !snapshot.Children.TryGetValue(port, out var children))
        {
            return;
        }

        foreach (var childPort in children)
        {
            var hop = snapshot.Hops.FirstOrDefault(h => h.Port == childPort);
            var name = Name(harness, childPort);
            var style = hop.Accepted ? "green" : "grey";
            var suffix = hop.Accepted ? string.Empty : " [grey dim](duplicate)[/]";

            var node = parent.AddNode($"[{style}]{name}[/] [grey]+{hop.Delay.TotalMilliseconds:0.0}ms[/]{suffix}");

            AddChildren(node, harness, snapshot, childPort, depth + 1);
        }
    }

    private static string Name(ClusterHarness harness, int port) =>
        harness.ByPort.TryGetValue(port, out var handle) ? handle.Name : $":{port}";
}
