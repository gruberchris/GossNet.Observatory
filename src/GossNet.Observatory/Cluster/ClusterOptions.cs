namespace GossNet.Observatory.Cluster;

/// <summary>
/// How a cluster is built.
/// </summary>
internal sealed record ClusterOptions
{
    /// <summary>Gets the number of nodes.</summary>
    public int NodeCount { get; init; } = 8;

    /// <summary>
    /// Gets the first UDP port; node <c>i</c> listens on <c>BasePort + i</c>.
    /// </summary>
    public int BasePort { get; init; } = 19100;

    /// <summary>Gets the neighbour graph shape.</summary>
    public TopologyKind Topology { get; init; } = TopologyKind.Ring;

    /// <summary>Gets the random chords per node for <see cref="TopologyKind.KRandom"/>.</summary>
    public int Degree { get; init; } = 2;

    /// <summary>Gets the seed for topology randomness, so a run can be reproduced.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Gets the filler bytes added to each message.</summary>
    public int PayloadBytes { get; init; }

    /// <summary>Gets how long a node remembers a message id for de-duplication.</summary>
    public int MessageTtlSeconds { get; init; } = 60;

    /// <summary>Gets the per-subscriber queue bound.</summary>
    public int SubscriberQueueCapacity { get; init; } = 1024;

    /// <summary>Gets how long after injection a message counts as finished spreading.</summary>
    public TimeSpan SettleWindow { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets how many messages' propagation histories are retained.
    /// </summary>
    /// <remarks>
    /// The interactive view only ever shows one at a time, but the benchmark aggregates
    /// across a whole run and needs every message it injected to still be there.
    /// </remarks>
    public int TrackerCapacity { get; init; } = 50;

    /// <summary>
    /// Gets the address every node binds and advertises.
    /// </summary>
    /// <remarks>
    /// Deliberately the literal loopback address rather than "localhost": the protocol
    /// compares host strings when excluding itself from its own neighbour list and when
    /// checking which nodes a message has already notified, and "localhost" would also
    /// resolve to <c>::1</c>, so the two forms would not match.
    /// </remarks>
    public const string Host = "127.0.0.1";
}
