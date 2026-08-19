using GossNet.Observatory.Cluster;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui.Panels;

/// <summary>
/// The one-line summary of what the cluster is and what is being done to it.
/// </summary>
internal static class HeaderPanel
{
    /// <summary>Builds the header.</summary>
    public static IRenderable Build(ClusterHarness harness, ClusterOptions options)
    {
        var totals = harness.Metrics.Totals();
        var conditions = harness.Conditions;

        var loss = conditions.DropPerMille == 0
            ? "[green]0%[/]"
            : $"[yellow]{conditions.DropPerMille / 10d:0.#}%[/]";

        var latency = conditions.LatencyMs == 0 && conditions.JitterMs == 0
            ? "[green]0ms[/]"
            : $"[yellow]{conditions.LatencyMs}+{conditions.JitterMs}ms[/]";

        var split = conditions.IsPartitioned ? "[red]SPLIT[/]" : "[green]whole[/]";
        var alive = harness.AliveCount;
        var aliveText = alive == harness.Nodes.Count ? $"[green]{alive}[/]" : $"[red]{alive}[/]";

        var left = string.Join("  [grey]|[/]  ",
        [
            $"[bold]{options.Topology.ToString().ToLowerInvariant()}[/] x{harness.Nodes.Count}",
            $"alive {aliveText}/{harness.Nodes.Count}",
            $"loss {loss}",
            $"lat {latency}",
            $"net {split}"
        ]);

        var right = string.Join("  [grey]|[/]  ",
        [
            $"msgs [bold]{harness.Metrics.ConvergenceSampleCount}[/]",
            $"p50 [bold]{harness.Metrics.Percentile(50):0.0}ms[/]",
            $"p99 [bold]{harness.Metrics.Percentile(99):0.0}ms[/]",
            $"dup [bold]{totals.DuplicateRatio:0.00}x[/]",
            $"amp [bold]{totals.Amplification:0.00}x[/]"
        ]);

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap().RightAligned())
            .AddRow(new Markup(left), new Markup(right));

        return new Panel(grid)
            .Header("[bold]GossNet Observatory[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }
}
