using GossNet.Observatory.Cluster;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui.Panels;

/// <summary>
/// Per-node counters. The columns that matter are <c>dup</c> — duplicates the protocol's
/// message cache absorbed — and <c>q</c>, the subscriber queue depth that reveals a slow
/// consumer before messages start being dropped.
/// </summary>
internal static class StatsPanel
{
    private const int MaxRows = 12;

    /// <summary>Builds the stats table.</summary>
    public static IRenderable Build(ClusterHarness harness, ViewState view)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn(new TableColumn("[grey]node[/]"))
            .AddColumn(new TableColumn("[grey]rcv[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]acc[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]dup[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]sent[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]drop[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]q[/]").RightAligned());

        foreach (var handle in Window(harness.Nodes, view.SelectedIndex))
        {
            var stats = harness.Metrics.ByPort[handle.Port];
            var name = handle.Index == view.SelectedIndex ? $"[bold]{handle.Name}[/]" : handle.Name;

            if (!handle.IsAlive)
            {
                name = $"[red]{handle.Name}[/]";
            }

            var queue = stats.Deduplicated > 0 ? $"{handle.QueueDepth}" : "0";

            table.AddRow(
                name,
                stats.Received.ToString(),
                stats.Accepted.ToString(),
                stats.Deduplicated > 0 ? $"[yellow]{stats.Deduplicated}[/]" : "0",
                stats.Sent.ToString(),
                stats.Dropped > 0 ? $"[red]{stats.Dropped}[/]" : "0",
                queue);
        }

        if (harness.Nodes.Count > MaxRows)
        {
            table.Caption($"[grey]showing {MaxRows} of {harness.Nodes.Count} — use ← → to scroll[/]");
        }

        return new Panel(table)
            .Header("[bold]STATS[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    /// <summary>
    /// Keeps the selected node on screen when the cluster is larger than the panel.
    /// </summary>
    private static IEnumerable<NodeHandle> Window(IReadOnlyList<NodeHandle> nodes, int selected)
    {
        if (nodes.Count <= MaxRows)
        {
            return nodes;
        }

        var start = Math.Clamp(selected - (MaxRows / 2), 0, nodes.Count - MaxRows);

        return nodes.Skip(start).Take(MaxRows);
    }
}
