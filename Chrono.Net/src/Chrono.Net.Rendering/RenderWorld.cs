using System.Numerics;
using Chrono.Net.Fea;

namespace Chrono.Net.Rendering;

/// <summary>A renderable instance: a mesh attached to a body (or static) with a local transform and a color.</summary>
public sealed class RenderItem
{
    public required RenderMesh Mesh { get; init; }
    /// <summary>Index into the system body list, or -1 for static geometry.</summary>
    public int BodyIndex { get; init; } = -1;
    /// <summary>Scale + placement relative to the body visual frame.</summary>
    public Matrix4x4 Local { get; init; } = Matrix4x4.Identity;
    public Vector3 Color { get; set; }
    /// <summary>World transform, refreshed by <see cref="RenderWorld.Update"/>.</summary>
    public Matrix4x4 World { get; set; } = Matrix4x4.Identity;
    public ShapeType Shape { get; init; }
    public Vector3 Dimensions { get; init; }
    public Frame LocalFrame { get; init; }
    public bool CastsShadow { get; set; } = true;
    public bool IsGround { get; set; }
    /// <summary>True when attached to a fixed body (candidate for ground detection).</summary>
    public bool IsStatic { get; init; }
}

/// <summary>A polyline rendered as a thick 3D line (FEA beams, cables, paths).</summary>
public sealed class RenderLine
{
    public Vector3[] Points { get; set; } = [];
    public Vector3 Color { get; set; } = new(0.95f, 0.55f, 0.15f);
    public float Width { get; set; } = 3f;
}

/// <summary>
/// Renderer-independent view of a <see cref="ChSystem"/>: one <see cref="RenderItem"/> per visual shape, with world
/// transforms refreshed from a bulk body-state read every frame (no per-body P/Invoke).
/// </summary>
public sealed class RenderWorld
{
    private readonly List<RenderItem> _items = [];
    private readonly List<(FeaMesh Mesh, (int A, int B)[] Elements, RenderLine[] Lines)> _fea = [];
    private BodyState[] _states = [];
    private int _bodyCount;

    public RenderWorld(ChSystem system, bool upIsZ = false)
    {
        System = system;
        UpIsZ = upIsZ;
        Rebuild();
    }

    public ChSystem System { get; }
    public bool UpIsZ { get; set; }
    public IReadOnlyList<RenderItem> Items => _items;
    public List<RenderLine> Lines { get; } = [];
    public List<ContactInfo> Contacts { get; } = [];
    public bool ShowContacts { get; set; }

    /// <summary>Extra static or dynamic items (terrain meshes, markers…).</summary>
    public List<RenderItem> ExtraItems { get; } = [];

    /// <summary>Recreates render items from the bodies' visual shapes (call after adding bodies).</summary>
    public void Rebuild()
    {
        _items.Clear();
        _fea.Clear();
        var bodies = System.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            bool fixedBody = body.IsFixed;
            foreach (var shape in body.GetVisualShapes())
            {
                var item = CreateItem(body, i, shape, fixedBody);
                if (item is not null) _items.Add(item);
            }
        }
        foreach (var mesh in System.Meshes)
        {
            var elements = mesh.GetLineElements();
            _fea.Add((mesh, elements, [new RenderLine { Width = 4f }]));
        }
        _bodyCount = bodies.Count;
        Update();
        ClassifyGround();
    }

    private static RenderItem? CreateItem(ChBody body, int index, VisualShape s, bool isFixed)
    {
        var d = s.Dimensions;
        RenderMesh mesh;
        Vector3 scale;
        switch (s.Type)
        {
            case ShapeType.Box: mesh = MeshFactory.Unit(ShapeType.Box); scale = d.ToVector3(); break;
            case ShapeType.Sphere: mesh = MeshFactory.Unit(ShapeType.Sphere); scale = new Vector3((float)d.X); break;
            case ShapeType.Ellipsoid: mesh = MeshFactory.Unit(ShapeType.Sphere); scale = d.ToVector3(); break;
            case ShapeType.Cylinder: mesh = MeshFactory.Unit(ShapeType.Cylinder); scale = new Vector3((float)d.X, (float)d.X, (float)d.Y); break;
            case ShapeType.Cone: mesh = MeshFactory.Unit(ShapeType.Cone); scale = new Vector3((float)d.X, (float)d.X, (float)d.Y); break;
            case ShapeType.Capsule: mesh = MeshFactory.Capsule((float)d.X, (float)d.Y); scale = Vector3.One; break;
            case ShapeType.Mesh:
                try { mesh = RenderMesh.FromTriangleMesh(body.GetVisualMesh(s.Index)); }
                catch (ChronoException) { return null; }
                scale = Vector3.One;
                break;
            default: return null;
        }
        var local = Matrix4x4.CreateScale(scale) * ToMatrix(s.Frame);
        return new RenderItem
        {
            Mesh = mesh,
            BodyIndex = index,
            Local = local,
            Color = new Vector3(s.R, s.G, s.B),
            Shape = s.Type,
            Dimensions = d.ToVector3(),
            LocalFrame = s.Frame,
            IsStatic = isFixed,
        };
    }

    /// <summary>
    /// Flags static boxes that are thin along the up axis and wide horizontally as ground (they receive shadows and
    /// define the shadow plane instead of casting shadows).
    /// </summary>
    private void ClassifyGround()
    {
        var up = UpIsZ ? Vector3.UnitZ : Vector3.UnitY;
        var h1 = Vector3.UnitX;
        var h2 = UpIsZ ? Vector3.UnitY : Vector3.UnitZ;
        foreach (var item in _items)
        {
            if (!item.IsStatic || item.Shape != ShapeType.Box) continue;
            var m = item.World;
            Vector3 r0 = new(m.M11, m.M12, m.M13), r1 = new(m.M21, m.M22, m.M23), r2 = new(m.M31, m.M32, m.M33);
            float Extent(Vector3 a) => MathF.Abs(Vector3.Dot(r0, a)) + MathF.Abs(Vector3.Dot(r1, a)) + MathF.Abs(Vector3.Dot(r2, a));
            float eu = Extent(up), e1 = Extent(h1), e2 = Extent(h2);
            bool ground = e1 > 3 && e2 > 3 && eu < 0.25f * MathF.Min(e1, e2);
            item.IsGround = ground;
            item.CastsShadow = !ground;
        }
    }

    /// <summary>Reads all body states in one native call and refreshes world transforms, FEA lines and contacts.</summary>
    public void Update()
    {
        if (System.Bodies.Count != _bodyCount) { Rebuild(); return; }
        var states = System.GetBodyStates();
        if (_states.Length < states.Length) _states = new BodyState[states.Length];
        states.CopyTo(_states);
        foreach (var item in _items)
        {
            if (item.BodyIndex < 0 || item.BodyIndex >= states.Length) { item.World = item.Local; continue; }
            ref readonly var st = ref states[item.BodyIndex];
            item.World = item.Local * ToMatrix(new Frame(st.VisualPosition, st.VisualRotation));
        }

        Lines.RemoveAll(l => l.Width < 0);
        foreach (var (mesh, elements, lines) in _fea)
        {
            var nodes = mesh.GetNodePositions();
            var pts = new Vector3[elements.Length * 2];
            for (int e = 0; e < elements.Length; e++)
            {
                pts[2 * e] = nodes[elements[e].A].ToVector3();
                pts[2 * e + 1] = nodes[elements[e].B].ToVector3();
            }
            lines[0].Points = pts;
        }

        if (ShowContacts)
        {
            Contacts.Clear();
            Contacts.AddRange(System.GetContacts());
        }
    }

    /// <summary>Line segments (pairs of points) of FEA meshes plus user lines.</summary>
    public IEnumerable<RenderLine> AllLines()
    {
        foreach (var (_, _, lines) in _fea) foreach (var l in lines) yield return l;
        foreach (var l in Lines) yield return l;
    }

    /// <summary>World-space bounds of all non-ground items.</summary>
    public (Vector3 Min, Vector3 Max) Bounds(bool includeGround = false)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var item in _items.Concat(ExtraItems))
        {
            if (item.IsGround && !includeGround) continue;
            var c = Vector3.Transform(Vector3.Zero, item.World);
            var r = item.Mesh.Bounds();
            var ext = Vector3.Abs(Vector3.TransformNormal(r.Max - r.Min, item.World)) * 0.5f;
            min = Vector3.Min(min, c - ext);
            max = Vector3.Max(max, c + ext);
            any = true;
        }
        foreach (var l in AllLines())
            foreach (var p in l.Points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true; }
        return any ? (min, max) : (new Vector3(-1), new Vector3(1));
    }

    public static Matrix4x4 ToMatrix(Frame f)
    {
        var m = Matrix4x4.CreateFromQuaternion(f.Rotation.ToQuaternion());
        m.Translation = f.Position.ToVector3();
        return m;
    }
}
