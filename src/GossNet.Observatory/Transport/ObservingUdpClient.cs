using System.Diagnostics;
using System.Net.Sockets;
using GossNet.Observatory.Telemetry;
using GossNet.Protocol;

namespace GossNet.Observatory.Transport;

/// <summary>
/// Wraps a node's real transport to report every datagram and to apply the simulated
/// network conditions.
/// </summary>
/// <remarks>
/// This is the whole reason the observatory needs no changes to GossNet.Protocol:
/// <c>GossNetNode</c> accepts an <see cref="IUdpClient"/>, and both directions of the
/// wire pass through here. The receive side is what makes hop attribution exact — a
/// datagram's source port is the sending node's listening port, because that is the
/// socket it was bound to.
/// </remarks>
internal sealed class ObservingUdpClient(
    IUdpClient inner,
    int port,
    NetworkConditions conditions,
    NetworkMonitor monitor,
    DelayedSendScheduler scheduler) : IUdpClient
{
    /// <inheritdoc />
    public bool EnableBroadcast
    {
        get => inner.EnableBroadcast;

        // GossNetNode sets this during construction; it must reach the real socket.
        set => inner.EnableBroadcast = value;
    }

    /// <inheritdoc />
    public async ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        var result = await inner.ReceiveAsync(cancellationToken).ConfigureAwait(false);

        monitor.Publish(new TransportEvent(
            TransportEventKind.Received,
            result.RemoteEndPoint.Port,
            port,
            MessageIdReader.TryRead(result.Buffer),
            result.Buffer.Length,
            Stopwatch.GetTimestamp()));

        return result;
    }

    /// <inheritdoc />
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> datagram, string hostname, int destinationPort, CancellationToken cancellationToken)
    {
        var messageId = MessageIdReader.TryRead(datagram.Span);
        var timestamp = Stopwatch.GetTimestamp();

        if (!conditions.CanReach(port, destinationPort) || conditions.ShouldDrop())
        {
            monitor.Publish(new TransportEvent(
                TransportEventKind.Dropped, port, destinationPort, messageId, datagram.Length, timestamp));

            // Report success. UDP gives a sender no delivery signal, and the node only
            // counts a neighbour as notified when the send returns a positive length --
            // reporting the drop here would understate fan-out and misrepresent what a
            // real lossy network does to this protocol.
            return ValueTask.FromResult(datagram.Length);
        }

        monitor.Publish(new TransportEvent(
            TransportEventKind.Sent, port, destinationPort, messageId, datagram.Length, timestamp));

        if (!conditions.HasDelay)
        {
            return inner.SendAsync(datagram, hostname, destinationPort, cancellationToken);
        }

        // Never await a delay here: the caller holds the node's send gate. See
        // DelayedSendScheduler.
        scheduler.Enqueue(inner, datagram, hostname, destinationPort, conditions.NextDelay());

        return ValueTask.FromResult(datagram.Length);
    }

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();
}
