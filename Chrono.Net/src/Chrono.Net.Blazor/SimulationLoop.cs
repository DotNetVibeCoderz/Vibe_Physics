using System.Diagnostics;

namespace Chrono.Net.Blazor;

/// <summary>
/// Server-side real-time loop for interactive Blazor: advances physics by wall time on a background timer and invokes
/// <see cref="Frame"/> (~30 Hz) so the page can push transforms to the viewport.
/// </summary>
public sealed class SimulationLoop : IAsyncDisposable
{
    private readonly Action<double> _step;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _task;

    public SimulationLoop(Action<double> step, double timeStep)
    {
        _step = step;
        TimeStep = timeStep;
    }

    public double TimeStep { get; }
    public double Speed { get; set; } = 1;
    public bool IsRunning => _task is { IsCompleted: false };
    public double RealTimeFactor { get; private set; }
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(33);

    /// <summary>Called after each batch of steps (on a thread-pool thread).</summary>
    public Func<Task>? Frame { get; set; }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _task = RunAsync(_cts.Token);
    }

    public async Task PauseAsync()
    {
        _cts?.Cancel();
        if (_task is not null) { try { await _task; } catch (OperationCanceledException) { } }
    }

    /// <summary>Single step when paused.</summary>
    public async Task StepOnceAsync()
    {
        await _gate.WaitAsync();
        try { _step(TimeStep); }
        finally { _gate.Release(); }
        if (Frame is { } f) await f();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(FrameInterval);
        var clock = Stopwatch.StartNew();
        double last = 0, acc = 0;
        while (await timer.WaitForNextTickAsync(ct))
        {
            double now = clock.Elapsed.TotalSeconds;
            double wall = Math.Min(now - last, 0.1);
            last = now;
            acc += wall * Speed;
            double simulated = 0;
            var budget = Stopwatch.StartNew();
            await _gate.WaitAsync(ct);
            try
            {
                while (acc >= TimeStep && budget.Elapsed.TotalSeconds < 0.03)
                {
                    _step(TimeStep);
                    acc -= TimeStep;
                    simulated += TimeStep;
                }
            }
            finally { _gate.Release(); }
            if (acc > TimeStep * 4) acc = 0;
            if (wall > 0) RealTimeFactor = RealTimeFactor * 0.9 + simulated / wall * 0.1;
            if (Frame is { } f) await f();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await PauseAsync();
        _cts?.Dispose();
        _gate.Dispose();
    }
}
