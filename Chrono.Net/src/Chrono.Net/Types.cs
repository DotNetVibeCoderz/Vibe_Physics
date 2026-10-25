using System.Runtime.InteropServices;

namespace Chrono.Net;

/// <summary>Contact formulation of a system: non-smooth (complementarity) or smooth (penalty).</summary>
public enum ContactMethod
{
    /// <summary>Non-smooth contact: hard constraints, large time steps, rigid-body stacking.</summary>
    Nsc = 0,
    /// <summary>Smooth contact: penalty-based with stiffness/damping from material properties (DEM-like).</summary>
    Smc = 1,
}

/// <summary>Chrono solver types (mirrors <c>ChSolver::Type</c>).</summary>
public enum SolverType
{
    Psor = 0, Pssor = 1, PJacobi = 2, PMinres = 3, BarzilaiBorwein = 4, Apgd = 5, Admm = 6,
    SparseLu = 7, SparseQr = 8, PardisoMkl = 9, Mumps = 10,
    Gmres = 11, Minres = 12, BiCgStab = 13,
}

/// <summary>Chrono time integrators (mirrors <c>ChTimestepper::Type</c>).</summary>
public enum TimestepperType
{
    EulerExplicitI = 0, EulerExplicitII = 1, EulerSemiImplicit = 2, RungeKutta = 3, Heun = 4, Leapfrog = 5,
    EulerImplicit = 6, EulerImplicitLinearized = 7, EulerImplicitProjected = 8,
    Trapezoidal = 9, TrapezoidalLinearized = 10, Newmark = 11, Hht = 12,
}

public enum CollisionSystemType { Bullet = 0, Multicore = 1 }

public enum Axis { X = 0, Y = 1, Z = 2 }

public enum JointKind
{
    Lock = 0, Revolute = 1, Spherical = 2, Cylindrical = 3, Prismatic = 4,
    PointPlane = 5, PointLine = 6, Planar = 7, Oldham = 8, Universal = 9,
    Parallel = 10, Orthogonal = 11,
}

public enum MotorKind
{
    RotationAngle = 0, RotationSpeed = 1, RotationTorque = 2,
    LinearPosition = 3, LinearSpeed = 4, LinearForce = 5,
}

/// <summary>Degree of freedom of a lock-family joint, used for limits.</summary>
public enum JointAxis { X = 0, Y = 1, Z = 2, RotX = 3, RotY = 4, RotZ = 5 }

public enum ShapeType { Box = 0, Sphere = 1, Cylinder = 2, Capsule = 3, Cone = 4, Ellipsoid = 5, Mesh = 6, Other = 99 }

/// <summary>Kinematic snapshot of one body (layout-compatible with the native <c>chn_body_state</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BodyState
{
    /// <summary>Center of mass, absolute frame.</summary>
    public Vec3 Position;
    public Quat Rotation;
    public Vec3 LinearVelocity;
    /// <summary>Angular velocity expressed in the absolute frame.</summary>
    public Vec3 AngularVelocity;
    /// <summary>Visual/reference frame (differs from the COM frame for aux-ref bodies such as meshes or vehicle parts).</summary>
    public Vec3 VisualPosition;
    public Quat VisualRotation;

    public readonly Frame VisualFrame => new(VisualPosition, VisualRotation);
}

/// <summary>One active contact point.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ContactInfo
{
    public Vec3 PointA;
    public Vec3 PointB;
    public Vec3 Normal;
    public Vec3 Force;
    /// <summary>Signed distance (negative while penetrating).</summary>
    public double Distance;
}

/// <summary>Describes one visual shape attached to a body.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VisualShape
{
    public ShapeType Type;
    public int Index;
    /// <summary>Box: full lengths. Sphere: X=radius. Cylinder/Capsule/Cone: X=radius, Y=height (local Z). Ellipsoid: semi-axes.</summary>
    public Vec3 Dimensions;
    /// <summary>Placement relative to the body visual frame.</summary>
    public Frame Frame;
    public float R, G, B, A;

    public readonly Color Color => new(R, G, B, A);
}

/// <summary>Linear RGBA color with 0..1 components.</summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        int v = Convert.ToInt32(hex, 16);
        return hex.Length == 8
            ? new(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f)
            : new(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    public string ToHex() => $"#{(int)(R * 255):X2}{(int)(G * 255):X2}{(int)(B * 255):X2}";

    public static readonly Color White = new(1, 1, 1);
    public static readonly Color Gray = new(0.55f, 0.57f, 0.6f);
    public static readonly Color Orange = FromHex("#F28C28");
    public static readonly Color Teal = FromHex("#18B6A3");
    public static readonly Color Blue = FromHex("#3B82F6");
    public static readonly Color Red = FromHex("#EF4444");
    public static readonly Color Yellow = FromHex("#FACC15");
    public static readonly Color Purple = FromHex("#8B5CF6");
    public static readonly Color Green = FromHex("#22C55E");
    public static readonly Color Slate = FromHex("#334155");
}

/// <summary>Triangle mesh in body-local coordinates.</summary>
public sealed class TriangleMesh(Vec3[] vertices, int[] indices)
{
    public Vec3[] Vertices { get; } = vertices;
    /// <summary>Three vertex indices per triangle (counter-clockwise = outward).</summary>
    public int[] Indices { get; } = indices;
    public int TriangleCount => Indices.Length / 3;
}
