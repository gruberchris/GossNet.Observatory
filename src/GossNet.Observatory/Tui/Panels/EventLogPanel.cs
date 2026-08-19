using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui.Panels;

/// <summary>
/// What the operator has done to the cluster, and what the cluster did about it.
/// </summary>
internal static class EventLogPanel
{
    private const int VisibleLines = 8;

    /// <summary>Builds the activity log.</summary>
    public static IRenderable Build(EventLog log)
    {
        var lines = log.Tail(VisibleLines);

        var content = lines.Count == 0
            ? "[grey]Waiting.[/]"
            : string.Join("\n", lines);

        return new Panel(new Markup(content))
            .Header("[bold]ACTIVITY[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }
}
