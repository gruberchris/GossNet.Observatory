using System.Diagnostics;
using GossNet.Observatory.Cluster;
using GossNet.Observatory.Telemetry;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui.Panels;

/// <summary>
/// The cluster at a glance: one cell per node, lit when it has just accepted a message.
/// </summary>
internal static class NodeGridPanel
{
    /// <summary>How long a node's cell stays lit after it accepts a message.</summary>
    private static readonly TimeSpan PulseWindow = TimeSpan.FromMilliseconds(450);

    private const int CellsPerRow = 6;

    /// <summary>Builds the node grid.</summary>
    public static IRenderable Build(ClusterHarness harness, ViewState view, MessageSnapshot? tracked)
    {
        var now = Stopwatch.GetTimestamp();
        var rows = new List<string>();
        var cells = new List<string>(CellsPerRow);

        foreach (var handle in harness.Nodes)
        {
            cells.Add(Cell(handle, harness.Metrics.ByPort[handle.Port], view, tracked, now));

            if (cells.Count == CellsPerRow)
            {
                rows.Add(string.Join("  ", cells));
                cells.Clear();
            }
        }

        if (cells.Count > 0)
        {
            rows.Add(string.Join("  ", cells));
        }

        var legend = "[grey]●[/] idle  [bold green]●[/] just accepted  [bold yellow]◉[/] origin  [red]✖[/] killed";

        return new Panel(new Markup(string.Join("\n", rows) + "\n\n" + legend))
            .Header("[bold]NODES[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static string Cell(
        NodeHandle handle,
        NodeStats stats,
        ViewState view,
        MessageSnapshot? tracked,
        long now)
    {
        string style;
        string glyph;

        if (!handle.IsAlive)
        {
            style = "red";
            glyph = "✖";
        }
        else if (tracked is not null && tracked.OriginPort == handle.Port)
        {
            style = "bold yellow";
            glyph = "◉";
        }
        else if (stats.LastAcceptTimestamp != 0 && Stopwatch.GetElapsedTime(stats.LastAcceptTimestamp, now) < PulseWindow)
        {
            style = "bold green";
            glyph = "●";
        }
        else
        {
            style = "grey";
            glyph = "●";
        }

        if (handle.Index == view.SelectedIndex)
        {
            style += " underline";
        }

        return $"[{style}]{glyph}{handle.Name}[/]";
    }
}
