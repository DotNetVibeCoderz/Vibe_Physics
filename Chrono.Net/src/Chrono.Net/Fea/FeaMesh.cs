using Chrono.Net.Interop;

namespace Chrono.Net.Fea;

/// <summary>Rectangular beam section with linear elastic material (Euler-Bernoulli beams).</summary>
/// <param name="WidthY">Section width along the beam Y axis (m).</param>
/// <param name="WidthZ">Section width along the beam Z axis (m).</param>
/// <param name="YoungModulus">E (Pa).</param>
/// <param name="ShearModulus">G (Pa).</param>
/// <param name="Density">kg/m³.</param>
/// <param name="RayleighDamping">Stiffness-proportional damping coefficient.</param>
public readonly record struct BeamSection(double WidthY, double WidthZ, double YoungModulus, double ShearModulus,
    double Density = 7800, double RayleighDamping = 0)
{
    /// <summary>Structural steel, rectangular section.</summary>
    public static BeamSection Steel(double widthY, double widthZ) => new(widthY, widthZ, 210e9, 81e9, 7800, 0.0005);

    /// <summary>Aluminium alloy, rectangular section.</summary>
    public static BeamSection Aluminium(double widthY, double widthZ) => new(widthY, widthZ, 70e9, 26e9, 2700, 0.0005);
}

/// <summary>Circular cable section (ANCF cable elements: axial + bending stiffness, no torsion).</summary>
public readonly record struct CableSection(double Diameter, double YoungModulus, double Density = 1000, double RayleighDamping = 0)
{
    public static CableSection SteelWire(double diameter) => new(diameter, 200e9, 7800, 0.001);
    public static CableSection Rubber(double diameter) => new(diameter, 0.01e9, 1100, 0.002);
}

/// <summary>
/// Finite element mesh (Chrono::FEA). Supports Euler beams and ANCF cables; nodes can be fixed, loaded or attached to
/// rigid bodies. Add it to a <see cref="ChSystem"/> — FEA works best with SMC or with a MINRES/SparseQR solver and an
/// implicit time stepper.
/// </summary>
public sealed unsafe class FeaMesh : IDisposable
{
    private Vec3[] _nodeBuffer = [];

    internal MeshHandle Handle { get; }
    public ChSystem? System { get; internal set; }

    public FeaMesh()
    {
        ChronoEngine.EnsureAvailable();
        Handle = NativeMethods.Check(NativeMethods.chn_mesh_create());
    }

    public int NodeCount => NativeMethods.chn_mesh_num_nodes(Handle);
    public int ElementCount => NativeMethods.chn_mesh_num_elements(Handle);

    /// <summary>When disabled, gravity is not applied to the mesh nodes.</summary>
    public bool AutomaticGravity { set => NativeMethods.chn_mesh_set_automatic_gravity(Handle, value ? 1 : 0); }

    /// <summary>Builds <paramref name="elements"/> Euler beam elements from A to B. Returns the node index range.</summary>
    public Range BuildEulerBeam(Vec3 a, Vec3 b, int elements, BeamSection section, Vec3? up = null)
    {
        var ydir = up ?? (Math.Abs(Vec3.Dot((b - a).Normalized(), Vec3.UnitY)) > 0.99 ? Vec3.UnitX : Vec3.UnitY);
        int first = NativeMethods.chn_mesh_build_beam_euler(Handle, a, b, ydir, elements, section.WidthY, section.WidthZ,
            section.YoungModulus, section.ShearModulus, section.Density, section.RayleighDamping);
        NativeMethods.Check(first);
        return first..NodeCount;
    }

    /// <summary>Builds <paramref name="elements"/> ANCF cable elements from A to B. Returns the node index range.</summary>
    public Range BuildCable(Vec3 a, Vec3 b, int elements, CableSection section)
    {
        int first = NativeMethods.chn_mesh_build_cable_ancf(Handle, a, b, elements, section.Diameter, section.YoungModulus,
            section.Density, section.RayleighDamping);
        NativeMethods.Check(first);
        return first..NodeCount;
    }

    public void FixNode(int node, bool isFixed = true) => NativeMethods.Check(NativeMethods.chn_mesh_node_set_fixed(Handle, node, isFixed ? 1 : 0));

    public void SetNodeForce(int node, Vec3 force) => NativeMethods.Check(NativeMethods.chn_mesh_node_set_force(Handle, node, force));

    /// <summary>Rigidly attaches a node to a body (the mesh must already be in <see cref="System"/>).</summary>
    public void AttachNode(int node, ChBody body)
    {
        var sys = System ?? throw new InvalidOperationException("Add the mesh to a system first.");
        NativeMethods.Check(NativeMethods.chn_mesh_node_attach(sys.Handle, Handle, node, body.Handle));
    }

    public int ReadNodePositions(Span<Vec3> destination)
    {
        fixed (Vec3* p = destination) return NativeMethods.chn_mesh_get_node_positions(Handle, p, destination.Length);
    }

    /// <summary>Node positions in a reusable buffer (valid until the next call).</summary>
    public ReadOnlySpan<Vec3> GetNodePositions()
    {
        int n = NodeCount;
        if (_nodeBuffer.Length < n) _nodeBuffer = new Vec3[n];
        ReadNodePositions(_nodeBuffer);
        return _nodeBuffer.AsSpan(0, n);
    }

    public Vec3 GetNodePosition(int node) => GetNodePositions()[node];

    /// <summary>Pairs of node indices for each 2-node element (beams, cables), for line rendering.</summary>
    public (int A, int B)[] GetLineElements()
    {
        int n = NativeMethods.chn_mesh_get_line_elements(Handle, null, 0);
        NativeMethods.Check(n);
        var raw = new int[n * 2];
        fixed (int* p = raw) NativeMethods.chn_mesh_get_line_elements(Handle, p, n);
        var pairs = new (int, int)[n];
        for (int i = 0; i < n; i++) pairs[i] = (raw[2 * i], raw[2 * i + 1]);
        return pairs;
    }

    public void Dispose() => Handle.Dispose();
}
