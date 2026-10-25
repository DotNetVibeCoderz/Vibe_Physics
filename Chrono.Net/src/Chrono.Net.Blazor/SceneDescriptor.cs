using System.Runtime.InteropServices;
using Chrono.Net.Rendering;

namespace Chrono.Net.Blazor;

/// <summary>Static description of the scene sent once to the browser (shapes, colors, local frames).</summary>
public sealed record SceneDescriptor(bool UpIsZ, ItemDescriptor[] Items, int BodyCount);

/// <param name="Body">Body index (-1 = static extra item in world coordinates).</param>
/// <param name="Shape">box | sphere | ellipsoid | cylinder | capsule | cone | mesh.</param>
/// <param name="Dims">Box: lengths; sphere: [r]; ellipsoid: semi-axes; cylinder/capsule/cone: [r, h].</param>
/// <param name="Pos">Local position relative to the body visual frame.</param>
/// <param name="Rot">Local rotation quaternion [x, y, z, w].</param>
public sealed record ItemDescriptor(int Body, string Shape, float[] Dims, float[] Pos, float[] Rot, string Color, bool Ground,
    float[]? Vertices = null, int[]? Indices = null);

/// <summary>Converts a <see cref="RenderWorld"/> into compact payloads for the three.js viewport.</summary>
public static class SceneSerializer
{
    public static SceneDescriptor Describe(RenderWorld world)
    {
        var items = new List<ItemDescriptor>();
        foreach (var it in world.Items)
        {
            string shape = it.Shape switch
            {
                ShapeType.Box => "box",
                ShapeType.Sphere => "sphere",
                ShapeType.Ellipsoid => "ellipsoid",
                ShapeType.Cylinder => "cylinder",
                ShapeType.Capsule => "capsule",
                ShapeType.Cone => "cone",
                _ => "mesh",
            };
            var f = it.LocalFrame;
            float[]? verts = null;
            int[]? idx = null;
            if (shape == "mesh")
            {
                verts = MemoryMarshal.Cast<System.Numerics.Vector3, float>(it.Mesh.Positions).ToArray();
                idx = it.Mesh.Indices;
            }
            items.Add(new ItemDescriptor(it.BodyIndex, shape, [it.Dimensions.X, it.Dimensions.Y, it.Dimensions.Z],
                [(float)f.Position.X, (float)f.Position.Y, (float)f.Position.Z],
                [(float)f.Rotation.E1, (float)f.Rotation.E2, (float)f.Rotation.E3, (float)f.Rotation.E0],
                Hex(it.Color), it.IsGround, verts, idx));
        }
        return new SceneDescriptor(world.UpIsZ, [.. items], world.System.Bodies.Count);
    }

    /// <summary>Body visual frames packed as float32 [px, py, pz, qx, qy, qz, qw] per body (binary interop).</summary>
    public static byte[] PackBodyFrames(ReadOnlySpan<BodyState> states, ref byte[]? buffer)
    {
        int n = states.Length * 7 * sizeof(float);
        if (buffer is null || buffer.Length != n) buffer = new byte[n];
        var f = MemoryMarshal.Cast<byte, float>(buffer.AsSpan());
        for (int i = 0; i < states.Length; i++)
        {
            ref readonly var s = ref states[i];
            int k = i * 7;
            f[k] = (float)s.VisualPosition.X; f[k + 1] = (float)s.VisualPosition.Y; f[k + 2] = (float)s.VisualPosition.Z;
            f[k + 3] = (float)s.VisualRotation.E1; f[k + 4] = (float)s.VisualRotation.E2;
            f[k + 5] = (float)s.VisualRotation.E3; f[k + 6] = (float)s.VisualRotation.E0;
        }
        return buffer;
    }

    /// <summary>All line segments (FEA beams/cables, user lines) as float32 xyz pairs.</summary>
    public static byte[] PackLines(RenderWorld world)
    {
        var pts = world.AllLines().SelectMany(l => l.Points).ToArray();
        return MemoryMarshal.AsBytes(pts.AsSpan()).ToArray();
    }

    private static string Hex(System.Numerics.Vector3 c) =>
        $"#{(int)(Math.Clamp(c.X, 0, 1) * 255):X2}{(int)(Math.Clamp(c.Y, 0, 1) * 255):X2}{(int)(Math.Clamp(c.Z, 0, 1) * 255):X2}";
}
