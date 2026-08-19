namespace GossNet.Observatory.Telemetry;

/// <summary>What happened to a single datagram.</summary>
internal enum TransportEventKind
{
    /// <summary>Handed to the socket (possibly after a simulated delay).</summary>
    Sent,

    /// <summary>Discarded by the simulated network before reaching the socket.</summary>
    Dropped,

    /// <summary>Arrived at a node's socket. Says nothing about whether the node accepted it.</summary>
    Received,

    /// <summary>A node published the message to its subscribers — a first sighting, not a duplicate.</summary>
    Accepted
}

/// <summary>
/// One observation from the instrumented transport, or from a node's subscription.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="FromPort">Sending node's port. Equals <paramref name="AtPort"/> for <see cref="TransportEventKind.Accepted"/>.</param>
/// <param name="AtPort">The node the event is recorded against.</param>
/// <param name="MessageId">Protocol message id, or <see cref="Guid.Empty"/> if it could not be read.</param>
/// <param name="Bytes">Datagram size.</param>
/// <param name="Timestamp">Monotonic <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
internal readonly record struct TransportEvent(
    TransportEventKind Kind,
    int FromPort,
    int AtPort,
    Guid MessageId,
    int Bytes,
    long Timestamp);
