using GossNet.Observatory.Cluster;
using GossNet.Observatory.Telemetry;

namespace GossNet.Observatory.Tests;

/// <summary>
/// End-to-end checks over real loopback sockets.
/// </summary>
/// <remarks>
/// These are the claims the observatory makes on screen — that a mesh floods directly,
/// that a ring relays, that killing a node cuts the ring, that a partition holds. They
/// are worth asserting rather than eyeballing. Each test uses its own port range so the
/// class is safe to run alongside anything else.
/// </remarks>
[TestClass]
public sealed class ClusterScenarioTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private static async Task<MessageSnapshot> InjectAndSettleAsync(ClusterHarness harness, int fromIndex)
    {
        var id = await harness.InjectAsync(fromIndex);

        await Task.Delay(Settle);
        await harness.Monitor.DrainAsync();

        var snapshot = harness.Tracker.Snapshot(id);

        Assert.IsNotNull(snapshot, "The injected message should still be tracked.");

        return snapshot;
    }

    private static ClusterHarness Build(int basePort, TopologyKind topology, int nodes = 6)
    {
        var harness = ClusterHarness.Create(new ClusterOptions
        {
            NodeCount = nodes,
            BasePort = basePort,
            Topology = topology
        });

        harness.Start();

        return harness;
    }

    [TestMethod]
    public async Task RingRelaysToEveryNode()
    {
        await using var harness = Build(19700, TopologyKind.Ring);

        var snapshot = await InjectAndSettleAsync(harness, 0);

        Assert.AreEqual(5, snapshot.NodesReached, "Every other node should receive the message.");

        // On a ring the far side can only be reached by relay, so the tree must have depth.
        var maxDepth = snapshot.Hops.Max(hop => Depth(snapshot, hop.Port));

        Assert.IsGreaterThan(1, maxDepth, "A ring should relay, not fan out directly.");
    }

    [TestMethod]
    public async Task MeshFloodsDirectlyFromTheOrigin()
    {
        await using var harness = Build(19720, TopologyKind.Mesh);

        var snapshot = await InjectAndSettleAsync(harness, 0);

        Assert.AreEqual(5, snapshot.NodesReached);

        // Everyone hears it from the origin first: the flat tree that makes a mesh's
        // O(N^2) datagram cost visible.
        foreach (var hop in snapshot.Hops)
        {
            Assert.AreEqual(snapshot.OriginPort, hop.ParentPort, $"{hop.Port} should have heard it from the origin.");
        }

        Assert.AreEqual(5d, snapshot.Amplification, "A 6-node mesh costs N-1 datagrams per node reached.");
    }

    [TestMethod]
    public async Task KillingANodeCutsTheRing()
    {
        await using var harness = Build(19740, TopologyKind.Ring);

        // Node 3 is opposite node 0 on a six-node ring; removing it leaves two arms.
        await harness.KillAsync(3);

        var snapshot = await InjectAndSettleAsync(harness, 0);

        Assert.AreEqual(4, snapshot.NodesReached,
            "The killed node still buffers datagrams but publishes nothing while stopped.");
        Assert.IsFalse(snapshot.Hops.Any(hop => hop.Accepted && hop.Port == harness.Nodes[3].Port));
    }

    [TestMethod]
    public async Task RevivingANodeRestoresFullCoverage()
    {
        await using var harness = Build(19760, TopologyKind.Ring);

        await harness.KillAsync(3);
        await InjectAndSettleAsync(harness, 0);

        harness.Revive(3);

        var afterRevive = await InjectAndSettleAsync(harness, 0);

        Assert.AreEqual(5, afterRevive.NodesReached, "A revived node should take part again.");
    }

    [TestMethod]
    public async Task PartitionStopsCrossTalk()
    {
        await using var harness = Build(19780, TopologyKind.Mesh);

        harness.Partition();

        var snapshot = await InjectAndSettleAsync(harness, 0);

        // Node 0 is in the first half of six, so it can reach only the other two there.
        Assert.AreEqual(2, snapshot.NodesReached, "A partition should confine the message to its own half.");
    }

    [TestMethod]
    public async Task HealingAPartitionRestoresCoverage()
    {
        await using var harness = Build(19800, TopologyKind.Mesh);

        harness.Partition();
        await InjectAndSettleAsync(harness, 0);

        harness.Heal();

        var afterHeal = await InjectAndSettleAsync(harness, 0);

        Assert.AreEqual(5, afterHeal.NodesReached);
    }

    [TestMethod]
    public async Task TotalLossReachesNobody()
    {
        await using var harness = Build(19820, TopologyKind.Mesh);

        harness.Conditions.DropPerMille = 1000;

        var snapshot = await InjectAndSettleAsync(harness, 0);

        Assert.AreEqual(0, snapshot.NodesReached);
        Assert.AreEqual(0, snapshot.DatagramsSent, "Dropped datagrams never reach the wire.");
        Assert.IsGreaterThan(0, snapshot.DatagramsDropped);
    }

    private static int Depth(MessageSnapshot snapshot, int port)
    {
        var depth = 0;
        var current = port;

        while (current != snapshot.OriginPort && depth <= 64)
        {
            var hop = snapshot.Hops.FirstOrDefault(h => h.Port == current);

            if (hop.Port == 0)
            {
                break;
            }

            current = hop.ParentPort;
            depth++;
        }

        return depth;
    }
}
