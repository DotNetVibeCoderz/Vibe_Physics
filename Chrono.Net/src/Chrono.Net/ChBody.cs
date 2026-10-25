using System.Text;
using Chrono.Net.Interop;

namespace Chrono.Net;

/// <summary>
/// A rigid body. Create with one of the <c>Create*</c> factories (mass and inertia computed from density and shape)
/// or with <see cref="ChBody()"/> and set mass, inertia, collision and visual shapes manually.
/// </summary>
public sealed unsafe class ChBody : IDisposable
{
    private string? _name;

    internal BodyHandle Handle { get; }

    /// <summary>System the body was added to (null until added).</summary>
    public ChSystem? System { get; internal set; }

    /// <summary>Free-form user data (e.g. a game object or a renderer node).</summary>
    public object? Tag { get; set; }

    internal ChBody(BodyHandle handle) => Handle = NativeMethods.Check(handle);

    /// <summary>Creates an empty body (mass 1, unit inertia, no shapes).</summary>
    public ChBody() : this(Create()) { }

    private static BodyHandle Create()
    {
        ChronoEngine.EnsureAvailable();
        return NativeMethods.chn_body_create();
    }

    // ------------------------------------------------------------------ factories

    /// <summary>Box with full side lengths <paramref name="size"/>, centered at its COM.</summary>
    public static ChBody CreateBox(Vec3 size, double density, ContactMaterial? material = null, bool visual = true, bool collide = true)
    {
        ChronoEngine.EnsureAvailable();
        return new(NativeMethods.chn_body_create_box(size, density, visual ? 1 : 0, collide ? 1 : 0, material?.Handle ?? MaterialHandle.Null));
    }

    public static ChBody CreateSphere(double radius, double density, ContactMaterial? material = null, bool visual = true, bool collide = true)
    {
        ChronoEngine.EnsureAvailable();
        return new(NativeMethods.chn_body_create_sphere(radius, density, visual ? 1 : 0, collide ? 1 : 0, material?.Handle ?? MaterialHandle.Null));
    }

    /// <summary>Cylinder aligned with <paramref name="axis"/>, centered at its COM.</summary>
    public static ChBody CreateCylinder(Axis axis, double radius, double height, double density, ContactMaterial? material = null, bool visual = true, bool collide = true)
    {
        ChronoEngine.EnsureAvailable();
        return new(NativeMethods.chn_body_create_cylinder((int)axis, radius, height, density, visual ? 1 : 0, collide ? 1 : 0, material?.Handle ?? MaterialHandle.Null));
    }

    public static ChBody CreateEllipsoid(Vec3 semiAxes, double density, ContactMaterial? material = null, bool visual = true, bool collide = true)
    {
        ChronoEngine.EnsureAvailable();
        return new(NativeMethods.chn_body_create_ellipsoid(semiAxes, density, visual ? 1 : 0, collide ? 1 : 0, material?.Handle ?? MaterialHandle.Null));
    }

    /// <summary>Convex hull of a point cloud. Points are re-centered so the body frame sits at the COM.</summary>
    public static ChBody CreateConvexHull(ReadOnlySpan<Vec3> points, double density, ContactMaterial? material = null, bool visual = true, bool collide = true)
    {
        ChronoEngine.EnsureAvailable();
        fixed (Vec3* p = points)
            return new(NativeMethods.chn_body_create_convex_hull(p, points.Length, density, visual ? 1 : 0, collide ? 1 : 0, material?.Handle ?? MaterialHandle.Null));
    }

    /// <summary>Body from a triangle mesh (reference frame = mesh frame; COM computed when <paramref name="computeMass"/>).</summary>
    public static ChBody CreateMesh(TriangleMesh mesh, double density, ContactMaterial? material = null, bool computeMass = true,
        bool visual = true, bool collide = true, double sphereSwept = 0.001)
    {
        ChronoEngine.EnsureAvailable();
        fixed (Vec3* v = mesh.Vertices)
        fixed (int* i = mesh.Indices)
            return new(NativeMethods.chn_body_create_mesh(v, mesh.Vertices.Length, i, mesh.TriangleCount, density,
                computeMass ? 1 : 0, visual ? 1 : 0, collide ? 1 : 0, material?.Handle ?? MaterialHandle.Null, sphereSwept));
    }

    // ------------------------------------------------------------------ properties

    public string Name
    {
        get
        {
            if (_name is not null) return _name;
            int len = NativeMethods.chn_body_get_name(Handle, null, 0);
            if (len == 0) return _name = string.Empty;
            var buffer = new byte[len + 1];
            fixed (byte* p = buffer) NativeMethods.chn_body_get_name(Handle, p, buffer.Length);
            return _name = Encoding.UTF8.GetString(buffer, 0, len);
        }
        set { _name = value; NativeMethods.chn_body_set_name(Handle, value); }
    }

    /// <summary>Position of the center of mass (absolute).</summary>
    public Vec3 Position { get => NativeMethods.chn_body_get_pos(Handle); set => NativeMethods.chn_body_set_pos(Handle, value); }

    public Quat Rotation { get => NativeMethods.chn_body_get_rot(Handle); set => NativeMethods.chn_body_set_rot(Handle, value); }

    public Frame Frame
    {
        get => new(Position, Rotation);
        set { Position = value.Position; Rotation = value.Rotation; }
    }

    public Vec3 LinearVelocity { get => NativeMethods.chn_body_get_lin_vel(Handle); set => NativeMethods.chn_body_set_lin_vel(Handle, value); }

    /// <summary>Angular velocity in the absolute frame (rad/s).</summary>
    public Vec3 AngularVelocity { get => NativeMethods.chn_body_get_ang_vel(Handle); set => NativeMethods.chn_body_set_ang_vel(Handle, value); }

    public Vec3 LinearAcceleration => NativeMethods.chn_body_get_lin_acc(Handle);

    public double Mass { get => NativeMethods.chn_body_get_mass(Handle); set => NativeMethods.chn_body_set_mass(Handle, value); }

    /// <summary>Principal moments of inertia (Ixx, Iyy, Izz) about the COM.</summary>
    public Vec3 Inertia { get => NativeMethods.chn_body_get_inertia_xx(Handle); set => NativeMethods.chn_body_set_inertia_xx(Handle, value); }

    /// <summary>Products of inertia (Ixy, Ixz, Iyz).</summary>
    public Vec3 InertiaProducts { set => NativeMethods.chn_body_set_inertia_xy(Handle, value); }

    /// <summary>Fixed bodies are immovable ground (still collide).</summary>
    public bool IsFixed { get => NativeMethods.chn_body_is_fixed(Handle) != 0; set => NativeMethods.chn_body_set_fixed(Handle, value ? 1 : 0); }

    public bool CollisionEnabled { get => NativeMethods.chn_body_collision_enabled(Handle) != 0; set => NativeMethods.chn_body_enable_collision(Handle, value ? 1 : 0); }

    public bool SleepingAllowed { set => NativeMethods.chn_body_set_sleeping_allowed(Handle, value ? 1 : 0); }

    /// <summary>Resultant contact force on the body (absolute), after the last step.</summary>
    public Vec3 ContactForce => NativeMethods.chn_body_get_contact_force(Handle);

    public Vec3 ContactTorque => NativeMethods.chn_body_get_contact_torque(Handle);

    public BodyState State
    {
        get { NativeMethods.chn_body_get_state(Handle, out var s); return s; }
    }

    /// <summary>Kinetic energy (translational + rotational, principal inertia approximation in body frame).</summary>
    public double KineticEnergy
    {
        get
        {
            var s = State;
            var wLocal = s.Rotation.Conjugate().Rotate(s.AngularVelocity);
            var I = Inertia;
            return 0.5 * Mass * s.LinearVelocity.LengthSquared
                 + 0.5 * (I.X * wLocal.X * wLocal.X + I.Y * wLocal.Y * wLocal.Y + I.Z * wLocal.Z * wLocal.Z);
        }
    }

    // ------------------------------------------------------------------ loads

    /// <summary>Clears forces/torques applied through <see cref="AddForce"/> / <see cref="AddTorque"/>.</summary>
    /// <remarks>Applied loads persist across steps until cleared; call once per step when they change.</remarks>
    public void ClearForces() => NativeMethods.chn_body_clear_forces(Handle);

    /// <summary>Adds a force applied at <paramref name="point"/>. With <paramref name="local"/>, both are in body coordinates.</summary>
    public void AddForce(Vec3 force, Vec3 point, bool local = false) => NativeMethods.chn_body_add_force(Handle, force, point, local ? 1 : 0);

    /// <summary>Adds a force applied at the center of mass (absolute frame).</summary>
    public void AddForce(Vec3 force) => NativeMethods.chn_body_add_force(Handle, force, Position, 0);

    public void AddTorque(Vec3 torque, bool local = false) => NativeMethods.chn_body_add_torque(Handle, torque, local ? 1 : 0);

    // ------------------------------------------------------------------ shapes

    public ChBody AddCollisionBox(Vec3 size, Frame? frame = null, ContactMaterial? material = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_collision_box(Handle, material?.Handle ?? MaterialHandle.Null, size, frame ?? Frame.Identity));
        return this;
    }

    public ChBody AddCollisionSphere(double radius, Frame? frame = null, ContactMaterial? material = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_collision_sphere(Handle, material?.Handle ?? MaterialHandle.Null, radius, frame ?? Frame.Identity));
        return this;
    }

    /// <summary>Cylinder along the local Z axis of <paramref name="frame"/>.</summary>
    public ChBody AddCollisionCylinder(double radius, double height, Frame? frame = null, ContactMaterial? material = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_collision_cylinder(Handle, material?.Handle ?? MaterialHandle.Null, radius, height, frame ?? Frame.Identity));
        return this;
    }

    /// <summary>Capsule along local Z; <paramref name="height"/> is the length of the cylindrical part.</summary>
    public ChBody AddCollisionCapsule(double radius, double height, Frame? frame = null, ContactMaterial? material = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_collision_capsule(Handle, material?.Handle ?? MaterialHandle.Null, radius, height, frame ?? Frame.Identity));
        return this;
    }

    public ChBody AddCollisionCone(double radius, double height, Frame? frame = null, ContactMaterial? material = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_collision_cone(Handle, material?.Handle ?? MaterialHandle.Null, radius, height, frame ?? Frame.Identity));
        return this;
    }

    public ChBody AddCollisionMesh(TriangleMesh mesh, bool isStatic = false, bool isConvex = false, double sphereSwept = 0.001,
        Frame? frame = null, ContactMaterial? material = null)
    {
        fixed (Vec3* v = mesh.Vertices)
        fixed (int* i = mesh.Indices)
            NativeMethods.Check(NativeMethods.chn_body_add_collision_mesh(Handle, material?.Handle ?? MaterialHandle.Null, v,
                mesh.Vertices.Length, i, mesh.TriangleCount, isStatic ? 1 : 0, isConvex ? 1 : 0, sphereSwept, frame ?? Frame.Identity));
        return this;
    }

    public ChBody AddVisualBox(Vec3 size, Color color, Frame? frame = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_visual_box(Handle, size, frame ?? Frame.Identity, color.R, color.G, color.B));
        return this;
    }

    public ChBody AddVisualSphere(double radius, Color color, Frame? frame = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_visual_sphere(Handle, radius, frame ?? Frame.Identity, color.R, color.G, color.B));
        return this;
    }

    public ChBody AddVisualCylinder(double radius, double height, Color color, Frame? frame = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_visual_cylinder(Handle, radius, height, frame ?? Frame.Identity, color.R, color.G, color.B));
        return this;
    }

    public ChBody AddVisualCapsule(double radius, double height, Color color, Frame? frame = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_visual_capsule(Handle, radius, height, frame ?? Frame.Identity, color.R, color.G, color.B));
        return this;
    }

    public ChBody AddVisualCone(double radius, double height, Color color, Frame? frame = null)
    {
        NativeMethods.Check(NativeMethods.chn_body_add_visual_cone(Handle, radius, height, frame ?? Frame.Identity, color.R, color.G, color.B));
        return this;
    }

    /// <summary>Recolors all visual shapes of the body.</summary>
    public ChBody SetColor(Color color)
    {
        NativeMethods.Check(NativeMethods.chn_body_set_color(Handle, color.R, color.G, color.B));
        return this;
    }

    /// <summary>Visual shapes attached to this body (used by renderers).</summary>
    public VisualShape[] GetVisualShapes()
    {
        int n = NativeMethods.chn_body_get_visual_shapes(Handle, null, 0);
        NativeMethods.Check(n);
        var shapes = new VisualShape[n];
        fixed (VisualShape* p = shapes) NativeMethods.chn_body_get_visual_shapes(Handle, p, n);
        return shapes;
    }

    /// <summary>Geometry of a mesh visual shape.</summary>
    public TriangleMesh GetVisualMesh(int shapeIndex)
    {
        int nv = 0, nt = 0;
        NativeMethods.Check(NativeMethods.chn_body_get_visual_mesh(Handle, shapeIndex, null, ref nv, null, ref nt));
        var verts = new Vec3[nv];
        var idx = new int[nt * 3];
        fixed (Vec3* v = verts)
        fixed (int* i = idx)
            NativeMethods.Check(NativeMethods.chn_body_get_visual_mesh(Handle, shapeIndex, v, ref nv, i, ref nt));
        return new TriangleMesh(verts, idx);
    }

    // ------------------------------------------------------------------ fluent helpers

    public ChBody At(Vec3 position) { Position = position; return this; }
    public ChBody At(double x, double y, double z) { Position = new(x, y, z); return this; }
    public ChBody Rotated(Quat rotation) { Rotation = rotation; return this; }
    public ChBody Fixed(bool value = true) { IsFixed = value; return this; }
    public ChBody Named(string name) { Name = name; return this; }
    public ChBody Colored(Color color) { SetColor(color); return this; }
    public ChBody WithVelocity(Vec3 velocity) { LinearVelocity = velocity; return this; }

    public void Dispose() => Handle.Dispose();

    public override string ToString() => string.IsNullOrEmpty(Name) ? $"ChBody@{Position}" : $"{Name}@{Position}";
}
