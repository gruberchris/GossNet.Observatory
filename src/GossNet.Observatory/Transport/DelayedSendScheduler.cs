using System.Diagnostics;
using System.Threading.Channels;
using GossNet.Protocol;

namespace GossNet.Observatory.Transport;

/// <summary>
/// Performs simulated-latency sends off the caller's thread.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of how <c>GossNetNode.SocializeMessageAsync</c> works: the node
/// awaits its whole fan-out before a send — or, on the receive path, a forward — counts
/// as complete. (Before GossNet.Protocol 0.10.0 the sends were also serialized behind a
/// semaphore; they are parallel now, but still awaited.) Awaiting a simulated delay
/// inside <see cref="IUdpClient.SendAsync"/> would therefore stall every sender and the
/// node's processing loop for the full latency on each message, which looks exactly
/// like a library bug. The transport instead returns immediately and the real socket
/// write happens here.
/// </para>
/// <para>
/// Work is spread over a small pool rather than a single worker: one worker sleeping
/// until an item is due would hold up items behind it that are already due.
/// </para>
/// </remarks>
internal sealed class DelayedSendScheduler : IAsyncDisposable
{
    private readonly Channel<PendingSend> _queue = Channel.CreateUnbounded<PendingSend>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });

    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task[] _workers;

    private int _disposed;

    /// <summary>Initializes the scheduler and starts its workers.</summary>
    public DelayedSendScheduler()
    {
        var workerCount = Math.Clamp(Environment.ProcessorCount, 4, 16);

        _workers = new Task[workerCount];

        for (var i = 0; i < workerCount; i++)
        {
            _workers[i] = Task.Run(() => RunAsync(_cancellation.Token), CancellationToken.None);
        }
    }

    /// <summary>Gets the number of sends waiting to go out.</summary>
    public int Pending => _queue.Reader.CanCount ? _queue.Reader.Count : 0;

    /// <summary>
    /// Queues a datagram to be sent once its delay has elapsed.
    /// </summary>
    /// <param name="target">The real transport to write through.</param>
    /// <param name="datagram">
    /// A copy of the payload. The caller's buffer is not retained: the node reuses one
    /// array across all neighbours of a message, so holding a reference past the call
    /// would be a lifetime bet this class has no way to win.
    /// </param>
    /// <param name="hostname">Destination host.</param>
    /// <param name="port">Destination port.</param>
    /// <param name="delay">How long to wait before writing.</param>
    public void Enqueue(IUdpClient target, ReadOnlyMemory<byte> datagram, string hostname, int port, TimeSpan delay)
    {
        var dueAt = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);

        _queue.Writer.TryWrite(new PendingSend(target, datagram.ToArray(), hostname, port, dueAt));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var pending in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), pending.DueAt);

                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await pending.Target
                        .SendAsync(pending.Datagram, pending.Hostname, pending.Port, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A failed write is indistinguishable from a lost datagram, which is
                    // a legitimate outcome on this transport. This also covers the
                    // shutdown race where the node's socket is disposed while a delayed
                    // send is still queued against it.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();
        await _cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _cancellation.Dispose();
    }

    private readonly record struct PendingSend(IUdpClient Target, byte[] Datagram, string Hostname, int Port, long DueAt);
}
