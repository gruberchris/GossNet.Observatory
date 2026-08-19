using System.Threading.Channels;

namespace GossNet.Observatory.Telemetry;

/// <summary>
/// Collects transport observations from every node and folds them into the metrics and
/// the propagation tracker.
/// </summary>
/// <remarks>
/// Events go through a channel rather than being applied inline so that nothing on a
/// node's send or receive path ever waits on analysis or on a lock the renderer holds.
/// A single consumer means the counters need no synchronization of their own.
/// </remarks>
internal sealed class NetworkMonitor(MessageTracker tracker, Metrics metrics, TimeSpan settleWindow) : IAsyncDisposable
{
    private readonly Channel<TransportEvent> _events = Channel.CreateUnbounded<TransportEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly CancellationTokenSource _cancellation = new();

    private Task? _pump;
    private Task? _sweeper;
    private int _disposed;

    /// <summary>Gets the propagation tracker being fed.</summary>
    public MessageTracker Tracker => tracker;

    /// <summary>Gets the counters being fed.</summary>
    public Metrics Metrics => metrics;

    /// <summary>Records an observation. Never blocks the caller.</summary>
    public void Publish(in TransportEvent transportEvent) => _events.Writer.TryWrite(transportEvent);

    /// <summary>Starts the ingest pump and the settle sweep.</summary>
    public void Start()
    {
        _pump ??= Task.Run(() => PumpAsync(_cancellation.Token), CancellationToken.None);
        _sweeper ??= Task.Run(() => SweepAsync(_cancellation.Token), CancellationToken.None);
    }

    /// <summary>
    /// Waits until every queued observation has been folded in.
    /// </summary>
    /// <remarks>Used by the benchmark, which must not read counters mid-flight.</remarks>
    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (_events.Reader.CanCount && _events.Reader.Count > 0)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        // One more turn so the item the pump had already dequeued is applied.
        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sweeps settled messages into the convergence distribution immediately.</summary>
    public void FlushSettled(TimeSpan settleWindowOverride)
    {
        foreach (var snapshot in tracker.TakeSettled(settleWindowOverride))
        {
            if (snapshot.NodesReached > 0)
            {
                metrics.RecordConvergence(snapshot.Convergence);
            }
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var transportEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                metrics.Record(transportEvent);
                tracker.OnEvent(transportEvent);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                FlushSettled(settleWindow);
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

        _events.Writer.TryComplete();
        await _cancellation.CancelAsync().ConfigureAwait(false);

        foreach (var task in new[] { _pump, _sweeper })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _cancellation.Dispose();
    }
}
