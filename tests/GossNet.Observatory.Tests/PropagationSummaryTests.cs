using GossNet.Observatory.Telemetry;
using GossNet.Observatory.Tui.Panels;

namespace GossNet.Observatory.Tests;

/// <summary>
/// The propagation summary reflows to whatever width the panel currently has, so a wide
/// terminal gets one compact line and a narrow one gets two readable ones.
/// </summary>
[TestClass]
public sealed class PropagationSummaryTests
{
    private static MessageSnapshot Snapshot() => new(
        Guid.NewGuid(),
        Seq: 32,
        OriginPort: 19100,
        Hops: [],
        Children: new Dictionary<int, IReadOnlyList<int>>(),
        NodesReached: 11,
        DatagramsSent: 13,
        DatagramsDropped: 0,
        DatagramsReceived: 15,
        Convergence: TimeSpan.FromMilliseconds(4.3));

    [TestMethod]
    public void WideEnoughPanelsGetOneLine()
    {
        var summary = PropagationSummaryFor(availableWidth: 120);

        Assert.DoesNotContain("\n", summary, "A wide panel should not be split across two lines.");
    }

    [TestMethod]
    public void NarrowPanelsGetTwoLines()
    {
        var summary = PropagationSummaryFor(availableWidth: 36);

        Assert.Contains("\n", summary);
        Assert.AreEqual(2, summary.Split('\n').Length);
    }

    [TestMethod]
    public void TheSplitHappensExactlyWhereTheTextStopsFitting()
    {
        // "reached 11/11 (100%) in 4.3ms" + " · " + "13 datagrams · amp 1.2x · dup 0.2x"
        const int Required = 29 + 3 + 34;

        Assert.DoesNotContain("\n", PropagationSummaryFor(Required));
        Assert.Contains("\n", PropagationSummaryFor(Required - 1));
    }

    [TestMethod]
    public void NeitherLineEverExceedsTheAvailableWidth()
    {
        // 36 is the narrowest panel the app can produce: it refuses to draw below an
        // 80-column console, and the panel gets half of that less borders and padding.
        foreach (var width in new[] { 36, 40, 48, 66, 80, 120, 200 })
        {
            foreach (var line in PropagationSummaryFor(width).Split('\n'))
            {
                Assert.IsLessThanOrEqualTo(
                    width,
                    PlainLength(line),
                    $"A line overflowed a {width}-column panel and would wrap mid-word.");
            }
        }
    }

    [TestMethod]
    public void CoverageIsGreenOnlyWhenEveryoneWasReached()
    {
        var full = PropagationSummaryFor(120, others: 11);
        var partial = PropagationSummaryFor(120, others: 20);

        Assert.Contains("[green]100%[/]", full);
        Assert.Contains("[yellow]55%[/]", partial);
    }

    private static string PropagationSummaryFor(int availableWidth, int others = 11) =>
        PropagationPanel.ComposeSummary(Snapshot(), others, availableWidth);

    /// <summary>Length as printed, with Spectre markup tags removed.</summary>
    private static int PlainLength(string markup)
    {
        var length = 0;
        var inTag = false;

        foreach (var character in markup)
        {
            if (character == '[')
            {
                inTag = true;
            }
            else if (inTag && character == ']')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                length++;
            }
        }

        return length;
    }
}
