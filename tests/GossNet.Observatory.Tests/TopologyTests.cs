using GossNet.Observatory.Cluster;

namespace GossNet.Observatory.Tests;

[TestClass]
public sealed class TopologyTests
{
    [TestMethod]
    [DataRow(nameof(TopologyKind.Mesh))]
    [DataRow(nameof(TopologyKind.Ring))]
    [DataRow(nameof(TopologyKind.KRandom))]
    [DataRow(nameof(TopologyKind.Grid))]
    public void EveryTopologyIsSymmetric(string name)
    {
        var kind = Enum.Parse<TopologyKind>(name);
        var adjacency = Topology.Build(kind, 12);

        for (var i = 0; i < adjacency.Count; i++)
        {
            foreach (var neighbour in adjacency[i])
            {
                Assert.IsTrue(
                    adjacency[neighbour].Contains(i),
                    $"{kind}: {i} lists {neighbour} but {neighbour} does not list {i}. " +
                    "Asymmetric wiring makes a node unreachable while still looking alive.");
            }
        }
    }

    [TestMethod]
    [DataRow(nameof(TopologyKind.Mesh))]
    [DataRow(nameof(TopologyKind.Ring))]
    [DataRow(nameof(TopologyKind.KRandom))]
    [DataRow(nameof(TopologyKind.Grid))]
    public void NoNodeIsItsOwnNeighbour(string name)
    {
        var adjacency = Topology.Build(Enum.Parse<TopologyKind>(name), 12);

        for (var i = 0; i < adjacency.Count; i++)
        {
            CollectionAssert.DoesNotContain(adjacency[i].ToList(), i);
        }
    }

    [TestMethod]
    [DataRow(nameof(TopologyKind.Mesh))]
    [DataRow(nameof(TopologyKind.Ring))]
    [DataRow(nameof(TopologyKind.KRandom))]
    [DataRow(nameof(TopologyKind.Grid))]
    public void EveryTopologyIsConnected(string name)
    {
        var kind = Enum.Parse<TopologyKind>(name);
        var adjacency = Topology.Build(kind, 17);

        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);

        while (queue.Count > 0)
        {
            foreach (var neighbour in adjacency[queue.Dequeue()])
            {
                if (seen.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        Assert.AreEqual(adjacency.Count, seen.Count, $"{kind} left {adjacency.Count - seen.Count} node(s) unreachable.");
    }

    [TestMethod]
    public void MeshGivesEveryNodeEveryOtherNode()
    {
        var adjacency = Topology.Build(TopologyKind.Mesh, 6);

        foreach (var neighbours in adjacency)
        {
            Assert.AreEqual(5, neighbours.Count);
        }
    }

    [TestMethod]
    public void RingGivesEveryNodeExactlyTwoNeighbours()
    {
        var adjacency = Topology.Build(TopologyKind.Ring, 6);

        foreach (var neighbours in adjacency)
        {
            Assert.AreEqual(2, neighbours.Count);
        }
    }

    [TestMethod]
    public void KRandomAddsChordsOnTopOfTheRing()
    {
        var ring = Topology.Build(TopologyKind.Ring, 20);
        var chorded = Topology.Build(TopologyKind.KRandom, 20, degree: 2);

        var ringEdges = ring.Sum(neighbours => neighbours.Count);
        var chordedEdges = chorded.Sum(neighbours => neighbours.Count);

        Assert.IsGreaterThan(ringEdges, chordedEdges, "k-random should be denser than the bare ring.");
    }

    [TestMethod]
    public void SameSeedProducesTheSameGraph()
    {
        var first = Topology.Build(TopologyKind.KRandom, 20, degree: 3, seed: 42);
        var second = Topology.Build(TopologyKind.KRandom, 20, degree: 3, seed: 42);

        for (var i = 0; i < first.Count; i++)
        {
            CollectionAssert.AreEqual(first[i].ToList(), second[i].ToList());
        }
    }

    [TestMethod]
    public void TwoNodesIsTheSmallestClusterAllowed()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Topology.Build(TopologyKind.Ring, 1));

        var adjacency = Topology.Build(TopologyKind.Ring, 2);

        Assert.AreEqual(1, adjacency[0].Count);
        Assert.AreEqual(1, adjacency[1].Count);
    }
}
