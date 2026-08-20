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
    /// <param name="harness">The cluster being observed.</param>
    /// <param name="snapshot">The message to draw, or null when nothing is tracked yet.</param>
    /// <param name="availableWidth">
    /// Printable width inside the panel. Recomputed every frame from the live console
    /// size, so the summary reflows as the terminal is resized.
    /// </param>
    public static IRenderable Build(ClusterHarness harness, MessageSnapshot? snapshot, int availableWidth)
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

        var others = Math.Max(1, harness.AliveCount - 1);

        return new Panel(new Rows(
                new Markup(ComposeSummary(snapshot, others, availableWidth)),
                new Text(string.Empty),
                tree))
            .Header($"[bold]PROPAGATION[/] [grey]#{snapshot.Seq}[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    /// <summary>
    /// Renders the summary on one line when it fits, otherwise on two.
    /// </summary>
    /// <remarks>
    /// The panel is half the console wide, so whether this fits depends entirely on the
    /// terminal. Measuring rather than assuming means a wide window gets the compact
    /// single line and a narrow one still reads correctly instead of wrapping mid-word.
    /// Markup tags are excluded from the measurement — they occupy no columns.
    /// </remarks>
    /// <param name="snapshot">The message being described.</param>
    /// <param name="others">Live nodes besides the origin, the denominator for coverage.</param>
    /// <param name="availableWidth">Printable width inside the panel.</param>
    internal static string ComposeSummary(MessageSnapshot snapshot, int others, int availableWidth)
    {
        var reached = snapshot.NodesReached;
        var coverage = 100d * reached / others;
        var milliseconds = snapshot.Convergence.TotalMilliseconds;

        var plainStatus = $"reached {reached}/{others} ({coverage:0}%) in {milliseconds:0.0}ms";
        var plainCost = $"{snapshot.DatagramsSent} datagrams · amp {snapshot.Amplification:0.0}x · dup {snapshot.DuplicateRatio:0.0}x";

        var status =
            $"reached [bold]{reached}[/]/{others} " +
            $"([{(reached >= others ? "green" : "yellow")}]{coverage:0}%[/]) " +
            $"in [bold]{milliseconds:0.0}ms[/]";

        var cost =
            $"[bold]{snapshot.DatagramsSent}[/] datagrams [grey]·[/] " +
            $"amp [bold]{snapshot.Amplification:0.0}x[/] [grey]·[/] " +
            $"dup [bold]{snapshot.DuplicateRatio:0.0}x[/]";

        const string Separator = " · ";

        return plainStatus.Length + Separator.Length + plainCost.Length <= availableWidth
            ? $"{status} [grey]·[/] {cost}"
            : $"{status}\n{cost}";
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
