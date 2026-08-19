using System.Diagnostics;
using GossNet.Observatory.Telemetry;

namespace GossNet.Observatory.Tests;

[TestClass]
public sealed class MessageTrackerTests
{
    private const int Origin = 19100;
    private const int NodeB = 19101;
    private const int NodeC = 19102;

    private static long Ticks(double millisecondsFromNow) =>
        Stopwatch.GetTimestamp() + (long)(millisecondsFromNow / 1000d * Stopwatch.Frequency);

    [TestMethod]
    public void ParentIsWhoeverTheNodeFirstHeardItFrom()
    {
        var tracker = new MessageTracker();
        var id = Guid.NewGuid();
        var start = Ticks(0);

        tracker.OnOrigin(id, seq: 1, Origin, start);

        tracker.OnEvent(Received(id, from: Origin, at: NodeB, Ticks(1)));
        tracker.OnEvent(Received(id, from: NodeB, at: NodeC, Ticks(2)));

        var snapshot = tracker.Snapshot(id);

        Assert.IsNotNull(snapshot);
        CollectionAssert.AreEqual(new[] { NodeB }, snapshot.Children[Origin].ToArray());
        CollectionAssert.AreEqual(new[] { NodeC }, snapshot.Children[NodeB].ToArray());
    }

    [TestMethod]
    public void LaterCopiesDoNotRewriteTheEdge()
    {
        var tracker = new MessageTracker();
        var id = Guid.NewGuid();

        tracker.OnOrigin(id, seq: 1, Origin, Ticks(0));

        tracker.OnEvent(Received(id, from: Origin, at: NodeB, Ticks(1)));

        // The duplicate that arrives second is exactly what the protocol's dedup cache
        // exists to absorb; it must not be mistaken for the edge.
        tracker.OnEvent(Received(id, from: NodeC, at: NodeB, Ticks(5)));

        var snapshot = tracker.Snapshot(id);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(Origin, snapshot.Hops.Single(hop => hop.Port == NodeB).ParentPort);
        Assert.AreEqual(2, snapshot.DatagramsReceived);
    }

    [TestMethod]
    public void OnlyAcceptedNodesCountAsReached()
    {
        var tracker = new MessageTracker();
        var id = Guid.NewGuid();

        tracker.OnOrigin(id, seq: 1, Origin, Ticks(0));

        tracker.OnEvent(Received(id, from: Origin, at: NodeB, Ticks(1)));
        tracker.OnEvent(Received(id, from: Origin, at: NodeC, Ticks(1)));
        tracker.OnEvent(Accepted(id, NodeB, Ticks(2)));

        var snapshot = tracker.Snapshot(id);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(1, snapshot.NodesReached);
        Assert.IsTrue(snapshot.Hops.Single(hop => hop.Port == NodeB).Accepted);
        Assert.IsFalse(snapshot.Hops.Single(hop => hop.Port == NodeC).Accepted);
    }

    [TestMethod]
    public void EventOrderDoesNotChangeTheTree()
    {
        var id = Guid.NewGuid();
        var start = Ticks(0);

        var events = new List<TransportEvent>
        {
            Received(id, Origin, NodeB, Ticks(1)),
            Received(id, NodeB, NodeC, Ticks(2)),
            Accepted(id, NodeB, Ticks(1.5)),
            Accepted(id, NodeC, Ticks(2.5))
        };

        // Events reach the pump in arrival order, which is not necessarily causal order.
        var shuffled = events.OrderBy(_ => Random.Shared.Next()).ToList();

        var tracker = new MessageTracker();
        tracker.OnOrigin(id, seq: 1, Origin, start);

        foreach (var transportEvent in shuffled)
        {
            tracker.OnEvent(transportEvent);
        }

        var snapshot = tracker.Snapshot(id);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(2, snapshot.NodesReached);
        CollectionAssert.AreEqual(new[] { NodeB }, snapshot.Children[Origin].ToArray());
        CollectionAssert.AreEqual(new[] { NodeC }, snapshot.Children[NodeB].ToArray());
    }

    [TestMethod]
    public void OldMessagesAreEvicted()
    {
        var tracker = new MessageTracker(capacity: 3);
        var ids = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            tracker.OnOrigin(id, i, Origin, Ticks(0));
        }

        Assert.AreEqual(3, tracker.RecentIds().Count);
        Assert.IsNull(tracker.Snapshot(ids[0]));
        Assert.IsNotNull(tracker.Snapshot(ids[4]));
    }

    [TestMethod]
    public void EventsForUntrackedMessagesAreIgnored()
    {
        var tracker = new MessageTracker();

        tracker.OnEvent(Received(Guid.NewGuid(), Origin, NodeB, Ticks(1)));

        Assert.AreEqual(0, tracker.RecentIds().Count);
    }

    [TestMethod]
    public void SettledMessagesAreReturnedOnce()
    {
        var tracker = new MessageTracker();
        var id = Guid.NewGuid();

        tracker.OnOrigin(id, seq: 1, Origin, Ticks(0));
        tracker.OnEvent(Accepted(id, NodeB, Ticks(1)));

        Assert.AreEqual(1, tracker.TakeSettled(TimeSpan.Zero).Count);
        Assert.AreEqual(0, tracker.TakeSettled(TimeSpan.Zero).Count, "A message must only be measured once.");
    }

    [TestMethod]
    public void UnsettledMessagesAreNotReturned()
    {
        var tracker = new MessageTracker();

        tracker.OnOrigin(Guid.NewGuid(), seq: 1, Origin, Ticks(0));

        Assert.AreEqual(0, tracker.TakeSettled(TimeSpan.FromMinutes(1)).Count);
    }

    [TestMethod]
    public void AmplificationAndDuplicateRatioComeFromTheDatagramCounts()
    {
        var tracker = new MessageTracker();
        var id = Guid.NewGuid();

        tracker.OnOrigin(id, seq: 1, Origin, Ticks(0));

        for (var i = 0; i < 8; i++)
        {
            tracker.OnEvent(new TransportEvent(TransportEventKind.Sent, Origin, NodeB, id, 100, Ticks(1)));
        }

        for (var i = 0; i < 6; i++)
        {
            tracker.OnEvent(Received(id, Origin, NodeB, Ticks(2)));
        }

        tracker.OnEvent(Accepted(id, NodeB, Ticks(3)));
        tracker.OnEvent(Accepted(id, NodeC, Ticks(3)));

        var snapshot = tracker.Snapshot(id);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(4d, snapshot.Amplification, "8 datagrams to reach 2 nodes.");
        Assert.AreEqual(2d, snapshot.DuplicateRatio, "6 received for 2 useful deliveries.");
    }

    private static TransportEvent Received(Guid id, int from, int at, long timestamp) =>
        new(TransportEventKind.Received, from, at, id, 120, timestamp);

    private static TransportEvent Accepted(Guid id, int at, long timestamp) =>
        new(TransportEventKind.Accepted, at, at, id, 0, timestamp);
}
