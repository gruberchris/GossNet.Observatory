using GossNet.Observatory.Telemetry;

namespace GossNet.Observatory.Tests;

[TestClass]
public sealed class MetricsTests
{
    private const int NodeA = 19100;
    private const int NodeB = 19101;

    [TestMethod]
    public void CountersLandOnTheRightNode()
    {
        var metrics = new Metrics([NodeA, NodeB]);

        metrics.Record(new TransportEvent(TransportEventKind.Sent, NodeA, NodeB, Guid.NewGuid(), 100, 1));
        metrics.Record(new TransportEvent(TransportEventKind.Received, NodeA, NodeB, Guid.NewGuid(), 100, 2));
        metrics.Record(new TransportEvent(TransportEventKind.Dropped, NodeA, NodeB, Guid.NewGuid(), 100, 3));
        metrics.Record(new TransportEvent(TransportEventKind.Accepted, NodeB, NodeB, Guid.NewGuid(), 0, 4));

        // Sends and drops are charged to the sender, receipts and accepts to the receiver.
        Assert.AreEqual(1, metrics.ByPort[NodeA].Sent);
        Assert.AreEqual(1, metrics.ByPort[NodeA].Dropped);
        Assert.AreEqual(0, metrics.ByPort[NodeA].Received);
        Assert.AreEqual(1, metrics.ByPort[NodeB].Received);
        Assert.AreEqual(1, metrics.ByPort[NodeB].Accepted);
    }

    [TestMethod]
    public void DeduplicatedIsWhatArrivedButWasNotAccepted()
    {
        var metrics = new Metrics([NodeA]);

        for (var i = 0; i < 5; i++)
        {
            metrics.Record(new TransportEvent(TransportEventKind.Received, NodeB, NodeA, Guid.NewGuid(), 100, i));
        }

        metrics.Record(new TransportEvent(TransportEventKind.Accepted, NodeA, NodeA, Guid.NewGuid(), 0, 6));

        Assert.AreEqual(4, metrics.ByPort[NodeA].Deduplicated);
    }

    [TestMethod]
    public void EventsForUnknownPortsAreIgnored()
    {
        var metrics = new Metrics([NodeA]);

        metrics.Record(new TransportEvent(TransportEventKind.Received, NodeA, 29999, Guid.NewGuid(), 100, 1));

        Assert.AreEqual(0, metrics.ByPort[NodeA].Received);
    }

    [TestMethod]
    public void PercentilesReflectTheSamples()
    {
        var metrics = new Metrics([NodeA]);

        for (var i = 1; i <= 100; i++)
        {
            metrics.RecordConvergence(TimeSpan.FromMilliseconds(i));
        }

        Assert.AreEqual(50d, metrics.Percentile(50));
        Assert.AreEqual(99d, metrics.Percentile(99));
        Assert.AreEqual(100, metrics.ConvergenceSampleCount);
    }

    [TestMethod]
    public void PercentileOfNothingIsZero()
    {
        var metrics = new Metrics([NodeA]);

        Assert.AreEqual(0d, metrics.Percentile(50));
    }

    [TestMethod]
    public void SamplesFallIntoTheRightHistogramBucket()
    {
        var metrics = new Metrics([NodeA]);

        metrics.RecordConvergence(TimeSpan.FromMilliseconds(0.4));
        metrics.RecordConvergence(TimeSpan.FromMilliseconds(1.5));
        metrics.RecordConvergence(TimeSpan.FromMilliseconds(500));

        var histogram = metrics.Histogram();

        Assert.AreEqual(1, histogram[0], "<1ms");
        Assert.AreEqual(1, histogram[1], "1-2ms");
        Assert.AreEqual(1, histogram[^1], "100ms+");
    }

    [TestMethod]
    public void TotalsDescribeWhatTheFloodCost()
    {
        var metrics = new Metrics([NodeA, NodeB]);

        // Eight datagrams on the wire, six arrived, two were useful.
        for (var i = 0; i < 8; i++)
        {
            metrics.Record(new TransportEvent(TransportEventKind.Sent, NodeA, NodeB, Guid.NewGuid(), 100, i));
        }

        for (var i = 0; i < 6; i++)
        {
            metrics.Record(new TransportEvent(TransportEventKind.Received, NodeA, NodeB, Guid.NewGuid(), 100, i));
        }

        metrics.Record(new TransportEvent(TransportEventKind.Accepted, NodeA, NodeA, Guid.NewGuid(), 0, 9));
        metrics.Record(new TransportEvent(TransportEventKind.Accepted, NodeB, NodeB, Guid.NewGuid(), 0, 9));

        var totals = metrics.Totals();

        Assert.AreEqual(8, totals.Sent);
        Assert.AreEqual(6, totals.Received);
        Assert.AreEqual(2, totals.Accepted);
        Assert.AreEqual(4d, totals.Amplification);
        Assert.AreEqual(2d, totals.DuplicateRatio);
    }
}
