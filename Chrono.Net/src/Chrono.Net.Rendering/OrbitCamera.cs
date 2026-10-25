using System.Numerics;

namespace Chrono.Net.Rendering;

/// <summary>Orbit camera around a target point, for Y-up or Z-up worlds.</summary>
public sealed class OrbitCamera
{
    public Vector3 Target { get; set; }
    public float Distance { get; set; } = 10;
    /// <summary>Azimuth around the up axis (rad).</summary>
    public float Yaw { get; set; } = 0.7f;
    /// <summary>Elevation above the horizon (rad).</summary>
    public float Pitch { get; set; } = 0.45f;
    public float FieldOfView { get; set; } = 45 * MathF.PI / 180;
    public float Near { get; set; } = 0.05f;
    public float Far { get; set; } = 2000;
    public bool UpIsZ { get; set; }

    public Vector3 Up => UpIsZ ? Vector3.UnitZ : Vector3.UnitY;

    public Vector3 Position
    {
        get
        {
            float cp = MathF.Cos(Pitch), sp = MathF.Sin(Pitch);
            var dir = UpIsZ
                ? new Vector3(cp * MathF.Cos(Yaw), cp * MathF.Sin(Yaw), sp)
                : new Vector3(cp * MathF.Sin(Yaw), sp, cp * MathF.Cos(Yaw));
            return Target + dir * Distance;
        }
    }

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Up);

    public Matrix4x4 Projection(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, MathF.Max(aspect, 0.01f), Near, Far);

    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -1.45f, 1.45f);
    }

    public void Zoom(float factor) => Distance = Math.Clamp(Distance * factor, 0.05f, 5000);

    /// <summary>Pans the target in the view plane by screen-relative amounts (fractions of the view height).</summary>
    public void Pan(float dx, float dy)
    {
        var forward = Vector3.Normalize(Target - Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Up));
        var up = Vector3.Cross(right, forward);
        float scale = Distance * MathF.Tan(FieldOfView / 2) * 2;
        Target += (-right * dx + up * dy) * scale;
    }

    /// <summary>Frames an axis-aligned bounding box.</summary>
    public void Fit(Vector3 min, Vector3 max, float margin = 1.25f)
    {
        Target = (min + max) / 2;
        float radius = MathF.Max((max - min).Length() / 2, 0.1f);
        Distance = radius * margin / MathF.Sin(FieldOfView / 2);
        Far = MathF.Max(2000, Distance * 20);
    }
}
