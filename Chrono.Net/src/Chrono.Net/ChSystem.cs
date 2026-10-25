using System.Buffers;
using System.Runtime.InteropServices;
using Chrono.Net.Fea;
using Chrono.Net.Interop;

namespace Chrono.Net;

/// <summary>
/// A physical system: owns bodies, links and FEA meshes, and advances them in time.
/// </summary>
/// <example>
/// <code>
/// using var sys = new ChSystem();
/// var ground = sys.Add(ChBody.CreateBox(new(10, 1, 10), 1000).At(0, -0.5, 0).Fixed());
/// var ball = sys.Add(ChBody.CreateSphere(0.25, 500).At(0, 3, 0));
/// for (int i = 0; i &lt; 1000; i++) sys.Step(0.002);
/// </code>
/// </example>
public sealed unsafe class ChSystem : IDisposable
{
    private readonly List<ChBody> _bodies = [];
    private readonly List<ChLink> _links = [];
    private readonly List<FeaMesh> _meshes = [];
    private readonly List<IDisposable> _owned = [];
    private BodyState[] _stateBuffer = [];
    private bool _disposed;

    internal SystemHandle Handle { get; }

    /// <summary>Creates a system using the given contact formulation (gravity -9.81 along Y by default).</summary>
    public ChSystem(ContactMethod method = ContactMethod.Nsc)
    {
        ChronoEngine.EnsureAvailable();
        Handle = NativeMethods.Check(NativeMethods.chn_system_create((int)method));
        Gravity = new Vec3(0, -9.81, 0);
    }

    /// <summary>Wraps a system created natively (e.g. by a vehicle model); internal bodies are discovered.</summary>
    internal ChSystem(SystemHandle handle)
    {
        Handle = NativeMethods.Check(handle);
        SyncBodiesFromNative();
    }

    public ContactMethod ContactMethod => (ContactMethod)NativeMethods.chn_system_contact_method(Handle);

    public Vec3 Gravity
    {
        get => NativeMethods.chn_system_get_gravity(Handle);
        set => NativeMethods.chn_system_set_gravity(Handle, value);
    }

    /// <summary>Simulation time (s).</summary>
    public double Time
    {
        get => NativeMethods.chn_system_get_time(Handle);
        set => NativeMethods.chn_system_set_time(Handle, value);
    }

    /// <summary>Wall-clock time spent in the last step (s).</summary>
    public double LastStepWallTime => NativeMethods.chn_system_get_step_timer(Handle);

    public int ContactCount => NativeMethods.chn_system_num_contacts(Handle);

    public IReadOnlyList<ChBody> Bodies => _bodies;
    public IReadOnlyList<ChLink> Links => _links;
    public IReadOnlyList<FeaMesh> Meshes => _meshes;

    /// <summary>Raised before each step (from <see cref="Step(double)"/>); use it for controllers and applied loads.</summary>
    public event Action<ChSystem, double>? BeforeStep;

    /// <summary>Raised after each step.</summary>
    public event Action<ChSystem, double>? AfterStep;

    // ------------------------------------------------------------------ configuration

    public ChSystem SetSolver(SolverType type, int maxIterations = 0, double tolerance = 0)
    {
        NativeMethods.Check(NativeMethods.chn_system_set_solver(Handle, (int)type, maxIterations, tolerance));
        return this;
    }

    public ChSystem SetTimestepper(TimestepperType type)
    {
        NativeMethods.Check(NativeMethods.chn_system_set_timestepper(Handle, (int)type));
        return this;
    }

    public ChSystem SetCollisionSystem(CollisionSystemType type)
    {
        NativeMethods.Check(NativeMethods.chn_system_set_collision_system(Handle, (int)type));
        return this;
    }

    /// <summary>Number of threads for Chrono, collision detection and Eigen (0 = leave unchanged).</summary>
    public ChSystem SetThreads(int chrono, int collision = 0, int eigen = 0)
    {
        NativeMethods.chn_system_set_num_threads(Handle, chrono, collision, eigen);
        return this;
    }

    /// <summary>NSC: maximum speed used to push penetrating bodies apart (m/s).</summary>
    public double MaxPenetrationRecoverySpeed { set => NativeMethods.chn_system_set_max_penetration_recovery_speed(Handle, value); }

    // ------------------------------------------------------------------ content

    public ChBody Add(ChBody body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (body.System is not null) throw new InvalidOperationException("Body already belongs to a system.");
        NativeMethods.Check(NativeMethods.chn_system_add_body(Handle, body.Handle));
        body.System = this;
        _bodies.Add(body);
        return body;
    }

    public T Add<T>(T link) where T : ChLink
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.Check(NativeMethods.chn_system_add_link(Handle, link.Handle));
        link.System = this;
        _links.Add(link);
        if (link is ChMotor { Function: { } f }) _owned.Add(f);
        return link;
    }

    public FeaMesh Add(FeaMesh mesh)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.Check(NativeMethods.chn_system_add_mesh(Handle, mesh.Handle));
        mesh.System = this;
        _meshes.Add(mesh);
        return mesh;
    }

    /// <summary>Takes ownership of a disposable (material, function...) so it is disposed with the system.</summary>
    public T Own<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    public void Remove(ChBody body)
    {
        if (!_bodies.Remove(body)) return;
        NativeMethods.Check(NativeMethods.chn_system_remove_body(Handle, body.Handle));
        body.System = null;
    }

    public void Remove(ChLink link)
    {
        if (!_links.Remove(link)) return;
        NativeMethods.Check(NativeMethods.chn_system_remove_link(Handle, link.Handle));
        link.System = null;
    }

    public ChBody? FindBody(string name) => _bodies.Find(b => b.Name == name);

    /// <summary>Re-reads the native body list (bodies created internally, e.g. by vehicle models).</summary>
    public void SyncBodiesFromNative()
    {
        int n = NativeMethods.chn_system_num_bodies(Handle);
        for (int i = _bodies.Count; i < n; i++)
        {
            var b = new ChBody(NativeMethods.chn_system_get_body(Handle, i)) { System = this };
            _bodies.Add(b);
        }
    }

    // ------------------------------------------------------------------ simulation

    /// <summary>Advances the system by one step of <paramref name="dt"/> seconds.</summary>
    public void Step(double dt)
    {
        BeforeStep?.Invoke(this, dt);
        NativeMethods.Check(NativeMethods.chn_system_step(Handle, dt));
        AfterStep?.Invoke(this, dt);
    }

    /// <summary>
    /// Advances <paramref name="count"/> steps. When no step events are subscribed the loop runs entirely in native
    /// code (a single P/Invoke transition).
    /// </summary>
    public void Step(double dt, int count)
    {
        if (BeforeStep is null && AfterStep is null)
        {
            NativeMethods.Check(NativeMethods.chn_system_step_many(Handle, dt, count));
            return;
        }
        for (int i = 0; i < count; i++) Step(dt);
    }

    /// <summary>Advances until <see cref="Time"/> reaches <paramref name="endTime"/>.</summary>
    public void RunUntil(double endTime, double dt)
    {
        int steps = (int)System.Math.Ceiling((endTime - Time) / dt - 1e-9);
        if (steps > 0) Step(dt, steps);
    }

    public bool DoStaticLinear() => NativeMethods.chn_system_do_static_linear(Handle) == 0;
    public bool DoStaticNonlinear(int steps = 10) => NativeMethods.chn_system_do_static_nonlinear(Handle, steps) == 0;

    /// <summary>Moves bodies so that all constraints are satisfied (position, velocity and acceleration level).</summary>
    public bool DoAssembly() => NativeMethods.chn_system_do_assembly(Handle) == 0;

    // ------------------------------------------------------------------ bulk queries (allocation-free)

    /// <summary>Copies the states of all bodies (system order = <see cref="Bodies"/> order) into <paramref name="destination"/>.</summary>
    /// <returns>Number of bodies in the system (may exceed the span length).</returns>
    public int ReadBodyStates(Span<BodyState> destination)
    {
        fixed (BodyState* p = destination)
            return NativeMethods.chn_system_get_body_states(Handle, p, destination.Length);
    }

    /// <summary>States of all bodies in a buffer reused between calls (valid until the next call).</summary>
    public ReadOnlySpan<BodyState> GetBodyStates()
    {
        int n = NativeMethods.chn_system_num_bodies(Handle);
        if (_stateBuffer.Length < n) _stateBuffer = new BodyState[System.Math.Max(n, _stateBuffer.Length * 2)];
        int count = ReadBodyStates(_stateBuffer);
        return _stateBuffer.AsSpan(0, count);
    }

    /// <summary>Copies active contacts into <paramref name="destination"/>; returns the total number of contacts.</summary>
    public int ReadContacts(Span<ContactInfo> destination)
    {
        fixed (ContactInfo* p = destination)
        {
            int n = NativeMethods.chn_system_get_contacts(Handle, p, destination.Length);
            NativeMethods.Check(n);
            return n;
        }
    }

    public ContactInfo[] GetContacts()
    {
        int n = ReadContacts([]);
        var result = new ContactInfo[n];
        ReadContacts(result);
        return result;
    }

    // ------------------------------------------------------------------ state snapshots

    /// <summary>Captures positions, velocities and time — restore later with <see cref="RestoreState"/>.</summary>
    public SystemSnapshot SaveState()
    {
        NativeMethods.Check(NativeMethods.chn_system_state_size(Handle, out int np, out int nv));
        var pos = new double[np];
        var vel = new double[nv];
        double t;
        fixed (double* p = pos)
        fixed (double* v = vel)
            NativeMethods.Check(NativeMethods.chn_system_state_get(Handle, p, np, v, nv, out t));
        return new SystemSnapshot(pos, vel, t);
    }

    /// <summary>Restores a snapshot taken from this system with the same topology (e.g. RL episode reset).</summary>
    public void RestoreState(SystemSnapshot snapshot)
    {
        fixed (double* p = snapshot.Positions)
        fixed (double* v = snapshot.Velocities)
            NativeMethods.Check(NativeMethods.chn_system_state_set(Handle, p, snapshot.Positions.Length, v, snapshot.Velocities.Length, snapshot.Time));
    }

    /// <summary>Total kinetic energy of all non-fixed bodies (J).</summary>
    public double KineticEnergy
    {
        get
        {
            double e = 0;
            foreach (var b in _bodies) if (!b.IsFixed) e += b.KineticEnergy;
            return e;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var l in _links) l.Dispose();
        foreach (var m in _meshes) m.Dispose();
        foreach (var b in _bodies) b.Dispose();
        foreach (var o in _owned) o.Dispose();
        _links.Clear();
        _bodies.Clear();
        _meshes.Clear();
        _owned.Clear();
        Handle.Dispose();
    }
}

/// <summary>Immutable copy of a system's generalized state.</summary>
public sealed record SystemSnapshot(double[] Positions, double[] Velocities, double Time);
