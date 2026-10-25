using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chrono.Net;

/// <summary>Double-precision 3D vector, layout-compatible with Chrono's <c>ChVector3d</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 One = new(1, 1, 1);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public double Length => System.Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    public Vec3 Normalized()
    {
        double l = Length;
        return l > 1e-300 ? this / l : Zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
    public static implicit operator Vec3(Vector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3((double x, double y, double z) t) => new(t.x, t.y, t.z);

    public override string ToString() => $"({X:G5}, {Y:G5}, {Z:G5})";
}

/// <summary>
/// Double-precision unit quaternion using Chrono's convention: <see cref="E0"/> is the scalar part (w),
/// <see cref="E1"/>..<see cref="E3"/> the vector part (x, y, z).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Quat(double E0, double E1, double E2, double E3)
{
    public static readonly Quat Identity = new(1, 0, 0, 0);

    public double W => E0;
    public double X => E1;
    public double Y => E2;
    public double Z => E3;

    public static Quat FromAxisAngle(Vec3 axis, double angle)
    {
        var a = axis.Normalized();
        double h = angle * 0.5, s = System.Math.Sin(h);
        return new Quat(System.Math.Cos(h), a.X * s, a.Y * s, a.Z * s);
    }

    public static Quat FromAngleX(double angle) => FromAxisAngle(Vec3.UnitX, angle);
    public static Quat FromAngleY(double angle) => FromAxisAngle(Vec3.UnitY, angle);
    public static Quat FromAngleZ(double angle) => FromAxisAngle(Vec3.UnitZ, angle);

    /// <summary>Rotation from intrinsic Z-Y-X (yaw, pitch, roll) Euler angles, in radians.</summary>
    public static Quat FromEuler(double roll, double pitch, double yaw) =>
        FromAngleZ(yaw) * FromAngleY(pitch) * FromAngleX(roll);

    /// <summary>Rotation from Euler angles in degrees (X, Y, Z), applied as Z * Y * X.</summary>
    public static Quat FromEulerDegrees(Vec3 deg) =>
        FromEuler(deg.X * System.Math.PI / 180, deg.Y * System.Math.PI / 180, deg.Z * System.Math.PI / 180);

    /// <summary>Shortest rotation that maps <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static Quat FromTo(Vec3 from, Vec3 to)
    {
        var f = from.Normalized();
        var t = to.Normalized();
        double d = Vec3.Dot(f, t);
        if (d > 1 - 1e-12) return Identity;
        if (d < -1 + 1e-12)
        {
            var ortho = Vec3.Cross(Vec3.UnitX, f);
            if (ortho.LengthSquared < 1e-12) ortho = Vec3.Cross(Vec3.UnitY, f);
            return FromAxisAngle(ortho, System.Math.PI);
        }
        var c = Vec3.Cross(f, t);
        return new Quat(1 + d, c.X, c.Y, c.Z).Normalized();
    }

    public double Length => System.Math.Sqrt(E0 * E0 + E1 * E1 + E2 * E2 + E3 * E3);

    public Quat Normalized()
    {
        double l = Length;
        return l > 1e-300 ? new Quat(E0 / l, E1 / l, E2 / l, E3 / l) : Identity;
    }

    public Quat Conjugate() => new(E0, -E1, -E2, -E3);

    public static Quat operator *(Quat a, Quat b) => new(
        a.E0 * b.E0 - a.E1 * b.E1 - a.E2 * b.E2 - a.E3 * b.E3,
        a.E0 * b.E1 + a.E1 * b.E0 + a.E2 * b.E3 - a.E3 * b.E2,
        a.E0 * b.E2 - a.E1 * b.E3 + a.E2 * b.E0 + a.E3 * b.E1,
        a.E0 * b.E3 + a.E1 * b.E2 - a.E2 * b.E1 + a.E3 * b.E0);

    /// <summary>Rotates a vector by this quaternion.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 Rotate(Vec3 v)
    {
        // v' = v + 2w(q x v) + 2 q x (q x v)
        double qx = E1, qy = E2, qz = E3, w = E0;
        double tx = 2 * (qy * v.Z - qz * v.Y);
        double ty = 2 * (qz * v.X - qx * v.Z);
        double tz = 2 * (qx * v.Y - qy * v.X);
        return new Vec3(
            v.X + w * tx + (qy * tz - qz * ty),
            v.Y + w * ty + (qz * tx - qx * tz),
            v.Z + w * tz + (qx * ty - qy * tx));
    }

    public Vec3 AxisX => Rotate(Vec3.UnitX);
    public Vec3 AxisY => Rotate(Vec3.UnitY);
    public Vec3 AxisZ => Rotate(Vec3.UnitZ);

    /// <summary>Euler angles (roll about X, pitch about Y, yaw about Z) in radians.</summary>
    public Vec3 ToEuler()
    {
        double sinr = 2 * (E0 * E1 + E2 * E3), cosr = 1 - 2 * (E1 * E1 + E2 * E2);
        double sinp = 2 * (E0 * E2 - E3 * E1);
        double siny = 2 * (E0 * E3 + E1 * E2), cosy = 1 - 2 * (E2 * E2 + E3 * E3);
        return new Vec3(System.Math.Atan2(sinr, cosr),
            System.Math.Abs(sinp) >= 1 ? System.Math.CopySign(System.Math.PI / 2, sinp) : System.Math.Asin(sinp),
            System.Math.Atan2(siny, cosy));
    }

    public Quaternion ToQuaternion() => new((float)E1, (float)E2, (float)E3, (float)E0);
    public static implicit operator Quat(Quaternion q) => new(q.W, q.X, q.Y, q.Z);

    public override string ToString() => $"[{E0:G4}; {E1:G4}, {E2:G4}, {E3:G4}]";
}

/// <summary>A coordinate frame: position + orientation.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Frame(Vec3 Position, Quat Rotation)
{
    public static readonly Frame Identity = new(Vec3.Zero, Quat.Identity);

    public Frame(Vec3 position) : this(position, Quat.Identity) { }

    /// <summary>Frame at <paramref name="position"/> whose Z axis points along <paramref name="axis"/>.</summary>
    /// <remarks>Revolute, prismatic and cylindrical joints act along the frame Z axis.</remarks>
    public static Frame AlongAxis(Vec3 position, Vec3 axis) => new(position, Quat.FromTo(Vec3.UnitZ, axis));

    public Vec3 TransformPoint(Vec3 local) => Position + Rotation.Rotate(local);
    public Vec3 TransformDirection(Vec3 local) => Rotation.Rotate(local);

    /// <summary>Composes this (parent) frame with a child frame expressed in it.</summary>
    public Frame Compose(Frame child) => new(TransformPoint(child.Position), Rotation * child.Rotation);

    public static implicit operator Frame(Vec3 position) => new(position, Quat.Identity);
}
