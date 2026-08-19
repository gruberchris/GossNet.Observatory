namespace GossNet.Observatory.Tui;

/// <summary>
/// A bounded, newest-last list of things worth telling the operator about.
/// </summary>
internal sealed class EventLog(int capacity = 200)
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _lines = new();

    /// <summary>Appends a line, evicting the oldest when full.</summary>
    public void Add(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue($"[grey]{DateTime.Now:HH:mm:ss.ff}[/] {line}");

            while (_lines.Count > capacity)
            {
                _lines.Dequeue();
            }
        }
    }

    /// <summary>Gets the most recent lines, newest last.</summary>
    public IReadOnlyList<string> Tail(int count)
    {
        lock (_gate)
        {
            return [.. _lines.TakeLast(count)];
        }
    }
}

/// <summary>
/// What the operator is currently looking at. Mutated by the key handler, read by the
/// renderer.
/// </summary>
internal sealed class ViewState
{
    /// <summary>Gets or sets the node the operator has selected for kill/revive/inject.</summary>
    public int SelectedIndex { get; set; }

    /// <summary>Gets or sets the message whose propagation tree is on screen.</summary>
    public Guid? TrackedId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tree should jump to each newly
    /// injected message. Cleared once the operator pins a specific one.
    /// </summary>
    public bool FollowLatest { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether messages are injected continuously, which
    /// is what fills the convergence distribution with enough samples to be meaningful.
    /// </summary>
    public bool AutoInject { get; set; }

    /// <summary>Gets the activity log.</summary>
    public EventLog Log { get; } = new();

    /// <summary>Gets or sets a value indicating whether the render loop should stop.</summary>
    public bool Quit { get; set; }
}
