using System.Diagnostics;
using Avalonia.Threading;

namespace Chrono.Net.Avalonia;

/// <summary>
/// Drives a simulation in (approximately) real time from the UI thread: each animation frame it advances the
/// physics by the elapsed wall time (capped), then raises <see cref="Frame"/> so views can refresh.
/// </summary>
public sealed class SimulationHost : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private double _lastWall;
    private double _accumulator;

    /// <param name="step">Advances the simulation by one fixed step of <see cref="TimeStep"/> seconds.</param>
    public SimulationHost(Action<double> step, double timeStep)
    {
        StepAction = step;
        TimeStep = timeStep;
        // Note: the (interval, priority, callback) constructor starts the timer immediately — use the explicit form.
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
    }

    public Action<double> StepAction { get; set; }
    public double TimeStep { get; set; }
    /// <summary>Simulation speed relative to real time.</summary>
    public double Speed { get; set; } = 1;
    /// <summary>Maximum wall time spent stepping per frame, so the UI stays responsive (s).</summary>
    public double MaxStepBudget { get; set; } = 0.035;
    public bool IsRunning => _timer.IsEnabled;
    /// <summary>Measured simulated-time / wall-time ratio.</summary>
    public double RealTimeFactor { get; private set; }
    public double StepsPerFrame { get; private set; }

    /// <summary>Raised after physics advanced, once per UI frame.</summary>
    public event Action? Frame;

    public void Start()
    {
        _clock.Restart();
        _lastWall = 0;
        _accumulator = 0;
        _timer.Start();
    }

    public void Pause() => _timer.Stop();

    /// <summary>Advances exactly one step (when paused).</summary>
    public void StepOnce()
    {
        StepAction(TimeStep);
        Frame?.Invoke();
    }

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double wallDt = Math.Min(now - _lastWall, 0.1);
        _lastWall = now;
        _accumulator += wallDt * Speed;

        var budget = Stopwatch.StartNew();
        int steps = 0;
        double simulated = 0;
        while (_accumulator >= TimeStep && budget.Elapsed.TotalSeconds < MaxStepBudget)
        {
            StepAction(TimeStep);
            _accumulator -= TimeStep;
            simulated += TimeStep;
            steps++;
        }
        // Drop backlog we could not afford, instead of spiralling.
        if (_accumulator > TimeStep * 4) _accumulator = 0;
        StepsPerFrame = StepsPerFrame * 0.9 + steps * 0.1;
        if (wallDt > 0) RealTimeFactor = RealTimeFactor * 0.9 + simulated / wallDt * 0.1;
        Frame?.Invoke();
    }

    public void Dispose() => _timer.Stop();
}
