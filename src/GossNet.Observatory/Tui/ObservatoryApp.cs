using GossNet.Observatory.Cluster;
using GossNet.Observatory.Telemetry;
using GossNet.Observatory.Tui.Panels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GossNet.Observatory.Tui;

/// <summary>
/// The interactive observatory: renders the cluster and turns keystrokes into things
/// happening to it.
/// </summary>
internal sealed class ObservatoryApp(ClusterHarness harness, ClusterOptions options)
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(66);
    private static readonly TimeSpan AutoInjectInterval = TimeSpan.FromMilliseconds(300);

    private const int DropStepPerMille = 25;
    private const int LatencyStepMs = 5;

    private readonly ViewState _view = new();

    /// <summary>Runs until the operator quits.</summary>
    /// <param name="autoInject">Start with continuous injection already running.</param>
    /// <param name="cancellationToken">Stops the render loop.</param>
    public async Task RunAsync(bool autoInject, CancellationToken cancellationToken)
    {
        _view.AutoInject = autoInject;

        using var loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = loopCancellation.Token;

        _view.Log.Add($"[green]Cluster up[/]: {harness.Nodes.Count} nodes, {options.Topology.ToString().ToLowerInvariant()} topology, ports {options.BasePort}-{options.BasePort + harness.Nodes.Count - 1}");

        var keys = Task.Run(() => ReadKeysAsync(token), CancellationToken.None);
        var auto = Task.Run(() => AutoInjectAsync(token), CancellationToken.None);

        var layout = BuildLayout();

        await AnsiConsole.Live(layout)
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (!_view.Quit && !token.IsCancellationRequested)
                {
                    // Console size is read fresh each frame, so resizing the terminal
                    // reflows the view rather than requiring a restart.
                    if (IsConsoleBigEnough())
                    {
                        try
                        {
                            Render(layout);
                            ctx.Refresh();
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // The window was resized between measuring and drawing, so
                            // this frame's geometry is already stale. The next one will
                            // be measured against the new size.
                        }
                    }

                    await Task.Delay(FrameInterval, token).ConfigureAwait(false);
                }
            })
            .ConfigureAwait(false);

        await loopCancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(keys, auto).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on quit.
        }
    }

    /// <summary>Smallest console the layout can be drawn in without clipping a panel border off.</summary>
    private const int MinimumWidth = 80;

    private const int MinimumHeight = 22;

    /// <summary>
    /// Fails early and legibly on a console too small to draw in.
    /// </summary>
    /// <remarks>
    /// Spectre allocates a layout's rows before a panel gets to say it needs three of
    /// them for its border, so an undersized console surfaces deep inside the renderer
    /// as "Non-negative number required (Parameter 'index')" rather than as anything an
    /// operator could act on.
    /// </remarks>
    public static void EnsureConsoleIsBigEnough()
    {
        var profile = AnsiConsole.Profile;

        if (!IsConsoleBigEnough())
        {
            throw new InvalidOperationException(
                $"The observatory needs a console of at least {MinimumWidth}x{MinimumHeight}; " +
                $"this one is {profile.Width}x{profile.Height}. Resize the terminal, or use --bench.");
        }
    }

    /// <summary>
    /// Whether the console can currently hold the layout.
    /// </summary>
    /// <remarks>
    /// Checked every frame, not just at startup: shrinking the window mid-run would
    /// otherwise leave a region with negative height and take the app down. While the
    /// window is too small the view simply holds its last frame and resumes on resize.
    /// </remarks>
    private static bool IsConsoleBigEnough() =>
        AnsiConsole.Profile.Width >= MinimumWidth && AnsiConsole.Profile.Height >= MinimumHeight;

    /// <summary>
    /// Printable width inside the propagation panel: half the console, less the panel's
    /// borders and padding.
    /// </summary>
    /// <remarks>
    /// The floor is what <see cref="MinimumWidth"/> yields, since the view does not draw
    /// at all below that. Nothing narrower ever reaches the summary composer.
    /// </remarks>
    private const int NarrowestPropagationWidth = (MinimumWidth / 2) - 4;

    private static int PropagationWidth() =>
        Math.Max(NarrowestPropagationWidth, (AnsiConsole.Profile.Width / 2) - 4);

    /// <summary>
    /// Rows are proportional rather than fixed so the view adapts to the terminal.
    /// Fixed sizes totalling more than the console height leave a region with negative
    /// space, which the renderer cannot express.
    /// </summary>
    /// <remarks>
    /// The propagation tree gets the whole lower-left column. It is the one panel whose
    /// content grows with the cluster — a deep tree needs a line per node — while the
    /// histogram and the log are short and fixed, so they stack beside it rather than
    /// stealing rows from it.
    /// </remarks>
    private static Layout BuildLayout() =>
        new Layout("root").SplitRows(
            new Layout("header").Size(3),
            new Layout("top").Ratio(3).MinimumSize(5).SplitColumns(
                new Layout("nodes"),
                new Layout("stats")),
            new Layout("main").Ratio(7).MinimumSize(10).SplitColumns(
                new Layout("tree"),
                new Layout("side").SplitRows(
                    new Layout("histogram"),
                    new Layout("log"))),
            new Layout("keys").Size(3));

    private void Render(Layout layout)
    {
        var trackedId = _view.FollowLatest ? harness.Tracker.LatestId() : _view.TrackedId;
        var snapshot = trackedId is null ? null : harness.Tracker.Snapshot(trackedId.Value);

        layout["header"].Update(HeaderPanel.Build(harness, options));
        layout["nodes"].Update(NodeGridPanel.Build(harness, _view, snapshot));
        layout["stats"].Update(StatsPanel.Build(harness, _view));
        layout["tree"].Update(PropagationPanel.Build(harness, snapshot, PropagationWidth()));
        layout["histogram"].Update(HistogramPanel.Build(harness.Metrics));
        layout["log"].Update(EventLogPanel.Build(_view.Log));
        layout["keys"].Update(KeysPanel());
    }

    private IRenderable KeysPanel()
    {
        var follow = _view.FollowLatest ? "[green]follow[/]" : "[yellow]pinned[/]";
        var auto = _view.AutoInject ? "[green]on[/]" : "[grey]off[/]";

        var keys = string.Join("  [grey]|[/]  ",
        [
            "[bold]space[/] inject",
            "[bold]1-9[/] inject at n",
            "[bold]←→[/] select",
            "[bold]k/r[/] kill/revive",
            "[bold]p[/] partition",
            "[bold]d/D[/] loss",
            "[bold]l/L[/] latency",
            $"[bold]a[/] auto {auto}",
            $"[bold]t/f[/] track {follow}",
            "[bold]q[/] quit"
        ]);

        return new Panel(new Markup(keys))
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private async Task AutoInjectAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(AutoInjectInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_view.AutoInject)
                {
                    await InjectRandomAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task ReadKeysAsync(CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    // Polling rather than a blocking ReadKey, which would keep the
                    // process alive after a quit until one more key was pressed.
                    await Task.Delay(15, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await HandleKeyAsync(Console.ReadKey(intercept: true), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        switch (key.Key)
        {
            case ConsoleKey.Spacebar:
                await InjectRandomAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.LeftArrow:
                _view.SelectedIndex = (_view.SelectedIndex - 1 + harness.Nodes.Count) % harness.Nodes.Count;
                return;

            case ConsoleKey.RightArrow:
                _view.SelectedIndex = (_view.SelectedIndex + 1) % harness.Nodes.Count;
                return;

            case ConsoleKey.Escape:
                _view.Quit = true;
                return;

            default:
                break;
        }

        if (key.Key is >= ConsoleKey.D1 and <= ConsoleKey.D9)
        {
            var index = key.Key - ConsoleKey.D1;

            if (index < harness.Nodes.Count)
            {
                await InjectAsync(index, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        switch (key.KeyChar)
        {
            case 'q':
                _view.Quit = true;
                break;

            case 'k':
                await KillSelectedAsync().ConfigureAwait(false);
                break;

            case 'r':
                ReviveSelected();
                break;

            case 'p':
                TogglePartition();
                break;

            case 'd':
                AdjustLoss(-DropStepPerMille);
                break;

            case 'D':
                AdjustLoss(DropStepPerMille);
                break;

            case 'l':
                AdjustLatency(-LatencyStepMs);
                break;

            case 'L':
                AdjustLatency(LatencyStepMs);
                break;

            case 'a':
                _view.AutoInject = !_view.AutoInject;
                _view.Log.Add(_view.AutoInject ? "Auto-inject [green]on[/]" : "Auto-inject [grey]off[/]");
                break;

            case 'f':
                _view.FollowLatest = true;
                _view.Log.Add("Tracking [green]latest[/] message");
                break;

            case 't':
                TrackPrevious();
                break;

            default:
                break;
        }
    }

    private async Task InjectRandomAsync(CancellationToken cancellationToken)
    {
        var alive = harness.Nodes.Where(node => node.IsAlive).ToArray();

        if (alive.Length == 0)
        {
            _view.Log.Add("[red]Every node is down; nothing to inject from.[/]");
            return;
        }

        await InjectAsync(alive[Random.Shared.Next(alive.Length)].Index, cancellationToken).ConfigureAwait(false);
    }

    private async Task InjectAsync(int index, CancellationToken cancellationToken)
    {
        var handle = harness.Nodes[index];

        if (!handle.IsAlive)
        {
            _view.Log.Add($"[yellow]{handle.Name} is down[/]; cannot inject from it.");
            return;
        }

        try
        {
            await harness.InjectAsync(index, cancellationToken).ConfigureAwait(false);
            _view.FollowLatest = true;
            _view.Log.Add($"Injected at [bold]{handle.Name}[/]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _view.Log.Add($"[red]Inject failed[/]: {Markup.Escape(ex.Message)}");
        }
    }

    private async Task KillSelectedAsync()
    {
        var handle = harness.Nodes[_view.SelectedIndex];

        if (!handle.IsAlive)
        {
            return;
        }

        await harness.KillAsync(_view.SelectedIndex).ConfigureAwait(false);
        _view.Log.Add($"[red]Killed[/] {handle.Name} — its socket stays bound, so datagrams queue up for it");
    }

    private void ReviveSelected()
    {
        var handle = harness.Nodes[_view.SelectedIndex];

        if (handle.IsAlive)
        {
            return;
        }

        harness.Revive(_view.SelectedIndex);
        _view.Log.Add($"[green]Revived[/] {handle.Name} — draining whatever queued while it was down");
    }

    private void TogglePartition()
    {
        if (harness.Conditions.IsPartitioned)
        {
            harness.Heal();
            _view.Log.Add("[green]Healed[/] the partition");
            return;
        }

        harness.Partition();
        _view.Log.Add($"[red]Partitioned[/] the cluster into two halves of {harness.Nodes.Count / 2} and {harness.Nodes.Count - (harness.Nodes.Count / 2)}");
    }

    private void AdjustLoss(int deltaPerMille)
    {
        harness.Conditions.DropPerMille = Math.Clamp(harness.Conditions.DropPerMille + deltaPerMille, 0, 1000);
        _view.Log.Add($"Packet loss now [bold]{harness.Conditions.DropPerMille / 10d:0.#}%[/]");
    }

    private void AdjustLatency(int deltaMs)
    {
        harness.Conditions.LatencyMs = Math.Clamp(harness.Conditions.LatencyMs + deltaMs, 0, 2000);
        harness.Conditions.JitterMs = harness.Conditions.LatencyMs / 2;
        _view.Log.Add($"Latency now [bold]{harness.Conditions.LatencyMs}ms[/] +[bold]{harness.Conditions.JitterMs}ms[/] jitter");
    }

    private void TrackPrevious()
    {
        var ids = harness.Tracker.RecentIds();

        if (ids.Count == 0)
        {
            return;
        }

        var current = _view.FollowLatest ? ids[^1] : _view.TrackedId ?? ids[^1];
        var position = ids.ToList().IndexOf(current);
        var next = position <= 0 ? ids.Count - 1 : position - 1;

        _view.FollowLatest = false;
        _view.TrackedId = ids[next];

        var snapshot = harness.Tracker.Snapshot(ids[next]);
        _view.Log.Add($"Tracking message [bold]#{snapshot?.Seq}[/] (pinned — press [bold]f[/] to follow the latest again)");
    }
}
