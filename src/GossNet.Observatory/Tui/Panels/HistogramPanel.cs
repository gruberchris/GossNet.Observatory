using GossNet.Observatory.Telemetry;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui.Panels;

/// <summary>
/// Distribution of how long messages took to reach every node that got them.
/// </summary>
internal static class HistogramPanel
{
    private const int BarWidth = 16;

    /// <summary>Builds the convergence histogram.</summary>
    public static IRenderable Build(Metrics metrics)
    {
        var counts = metrics.Histogram();
        var labels = Metrics.HistogramLabels;
        var max = counts.Length == 0 ? 0 : counts.Max();

        if (max == 0)
        {
            return new Panel(new Markup("[grey]No messages measured yet.[/]"))
                .Header("[bold]CONVERGENCE[/]")
                .Border(BoxBorder.Rounded)
                .Expand();
        }

        var lines = new List<string>(labels.Count);

        for (var i = 0; i < labels.Count; i++)
        {
            var filled = (int)Math.Round((double)counts[i] / max * BarWidth);
            var bar = new string('█', filled).PadRight(BarWidth, '·');
            var colour = i <= 2 ? "green" : i <= 4 ? "yellow" : "red";

            lines.Add($"[grey]{labels[i],7}[/] [{colour}]{bar}[/] {counts[i]}");
        }

        return new Panel(new Markup(string.Join("\n", lines)))
            .Header("[bold]CONVERGENCE[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }
}
