using GossNet.Observatory.Transport;

namespace GossNet.Observatory.Tests;

[TestClass]
public sealed class NetworkConditionsTests
{
    [TestMethod]
    public void EverythingIsReachableUntilThereIsAPartition()
    {
        var conditions = new NetworkConditions();

        Assert.IsFalse(conditions.IsPartitioned);
        Assert.IsTrue(conditions.CanReach(19100, 19105));
    }

    [TestMethod]
    public void PartitionsBlockCrossTraffic()
    {
        var conditions = new NetworkConditions();

        conditions.SetPartition(19100, 0);
        conditions.SetPartition(19101, 0);
        conditions.SetPartition(19102, 1);

        Assert.IsTrue(conditions.IsPartitioned);
        Assert.IsTrue(conditions.CanReach(19100, 19101));
        Assert.IsFalse(conditions.CanReach(19100, 19102));
        Assert.IsFalse(conditions.CanReach(19102, 19101));
    }

    [TestMethod]
    public void HealingRestoresReachability()
    {
        var conditions = new NetworkConditions();

        conditions.SetPartition(19100, 0);
        conditions.SetPartition(19102, 1);
        conditions.ClearPartitions();

        Assert.IsFalse(conditions.IsPartitioned);
        Assert.IsTrue(conditions.CanReach(19100, 19102));
    }

    [TestMethod]
    public void UnassignedNodesFallIntoTheFirstPartition()
    {
        var conditions = new NetworkConditions();

        conditions.SetPartition(19102, 1);

        // A partial assignment should still read as a split, not isolate every node
        // that has not been named.
        Assert.IsTrue(conditions.CanReach(19100, 19101));
        Assert.IsFalse(conditions.CanReach(19100, 19102));
    }

    [TestMethod]
    public void NothingDropsAtZeroLoss()
    {
        var conditions = new NetworkConditions();

        for (var i = 0; i < 1000; i++)
        {
            Assert.IsFalse(conditions.ShouldDrop());
        }
    }

    [TestMethod]
    public void EverythingDropsAtTotalLoss()
    {
        var conditions = new NetworkConditions { DropPerMille = 1000 };

        for (var i = 0; i < 1000; i++)
        {
            Assert.IsTrue(conditions.ShouldDrop());
        }
    }

    [TestMethod]
    public void LossRateIsRoughlyWhatWasAskedFor()
    {
        var conditions = new NetworkConditions { DropPerMille = 200 };

        var dropped = Enumerable.Range(0, 20_000).Count(_ => conditions.ShouldDrop());

        Assert.IsGreaterThan(3_000, dropped);
        Assert.IsLessThan(5_000, dropped);
    }

    [TestMethod]
    public void NoDelayIsReportedWhenLatencyAndJitterAreZero()
    {
        var conditions = new NetworkConditions();

        Assert.IsFalse(conditions.HasDelay);
        Assert.AreEqual(TimeSpan.Zero, conditions.NextDelay());
    }

    [TestMethod]
    public void DelayStaysWithinLatencyPlusJitter()
    {
        var conditions = new NetworkConditions { LatencyMs = 10, JitterMs = 5 };

        Assert.IsTrue(conditions.HasDelay);

        for (var i = 0; i < 500; i++)
        {
            var delay = conditions.NextDelay().TotalMilliseconds;

            Assert.IsGreaterThanOrEqualTo(10d, delay);
            Assert.IsLessThanOrEqualTo(15d, delay);
        }
    }
}
