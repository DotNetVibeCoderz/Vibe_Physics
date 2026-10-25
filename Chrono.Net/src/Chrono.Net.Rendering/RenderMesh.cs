using System.Numerics;

namespace Chrono.Net.Rendering;

/// <summary>Indexed triangle mesh in single precision with per-vertex normals.</summary>
public sealed class RenderMesh(Vector3[] positions, Vector3[] normals, int[] indices)
{
    public Vector3[] Positions { get; } = positions;
    public Vector3[] Normals { get; } = normals;
    public int[] Indices { get; } = indices;
    public int TriangleCount => Indices.Length / 3;

    /// <summary>Builds a mesh with flat (faceted) normals from arbitrary triangles.</summary>
    public static RenderMesh FromTriangles(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, bool smooth)
    {
        if (smooth)
        {
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = vertices[indices[i]];
                var n = Vector3.Cross(vertices[indices[i + 1]] - a, vertices[indices[i + 2]] - a);
                normals[indices[i]] += n;
                normals[indices[i + 1]] += n;
                normals[indices[i + 2]] += n;
            }
            for (int i = 0; i < normals.Length; i++)
                normals[i] = normals[i].LengthSquared() > 0 ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
            return new RenderMesh(vertices.ToArray(), normals, indices.ToArray());
        }

        var pos = new Vector3[indices.Length];
        var nor = new Vector3[indices.Length];
        var idx = new int[indices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];
            var n = Vector3.Cross(b - a, c - a);
            n = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitY;
            pos[i] = a; pos[i + 1] = b; pos[i + 2] = c;
            nor[i] = nor[i + 1] = nor[i + 2] = n;
            idx[i] = i; idx[i + 1] = i + 1; idx[i + 2] = i + 2;
        }
        return new RenderMesh(pos, nor, idx);
    }

    public static RenderMesh FromTriangleMesh(TriangleMesh mesh, bool smooth = false)
    {
        var v = new Vector3[mesh.Vertices.Length];
        for (int i = 0; i < v.Length; i++) v[i] = mesh.Vertices[i].ToVector3();
        return FromTriangles(v, mesh.Indices, smooth);
    }

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return (min, max);
    }
}

/// <summary>Procedural primitive meshes matching Chrono's shape conventions (axes along local Z, centered).</summary>
public static class MeshFactory
{
    private static readonly Dictionary<(ShapeType, int), RenderMesh> UnitCache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>Unit box (side 1) centered at the origin.</summary>
    public static RenderMesh Box()
    {
        Vector3[] n = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        var pos = new List<Vector3>(24);
        var nor = new List<Vector3>(24);
        var idx = new List<int>(36);
        foreach (var normal in n)
        {
            var u = MathF.Abs(normal.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var t = Vector3.Cross(normal, u);
            u = Vector3.Cross(t, normal);
            int b = pos.Count;
            var c = normal * 0.5f;
            pos.Add(c - u * 0.5f - t * 0.5f);
            pos.Add(c + u * 0.5f - t * 0.5f);
            pos.Add(c + u * 0.5f + t * 0.5f);
            pos.Add(c - u * 0.5f + t * 0.5f);
            for (int i = 0; i < 4; i++) nor.Add(normal);
            // Orient counter-clockwise as seen from outside.
            var check = Vector3.Cross(pos[b + 1] - pos[b], pos[b + 2] - pos[b]);
            if (Vector3.Dot(check, normal) > 0) idx.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
            else idx.AddRange([b, b + 2, b + 1, b, b + 3, b + 2]);
        }
        return new RenderMesh([.. pos], [.. nor], [.. idx]);
    }

    /// <summary>Unit sphere (radius 1).</summary>
    public static RenderMesh Sphere(int segments = 24, int rings = 16)
    {
        var pos = new List<Vector3>();
        var idx = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * r / rings;
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2 * MathF.PI * s / segments;
                pos.Add(new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Sin(phi) * MathF.Sin(theta), MathF.Cos(phi)));
            }
        }
        int stride = segments + 1;
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segments; s++)
        {
            int a = r * stride + s, b = a + stride;
            idx.AddRange([a, b, a + 1, a + 1, b, b + 1]);
        }
        var arr = pos.ToArray();
        return new RenderMesh(arr, arr.ToArray(), [.. idx]);
    }

    /// <summary>Cylinder of radius 1 and height 1 along Z, centered.</summary>
    public static RenderMesh Cylinder(int segments = 28) => Frustum(1, 1, segments);

    /// <summary>Cone of radius 1 and height 1 along Z, centered (base at z = -0.5).</summary>
    public static RenderMesh Cone(int segments = 28) => Frustum(1, 0, segments);

    private static RenderMesh Frustum(float bottomRadius, float topRadius, int segments)
    {
        var pos = new List<Vector3>();
        var nor = new List<Vector3>();
        var idx = new List<int>();
        float slope = bottomRadius - topRadius; // dz = 1
        for (int s = 0; s <= segments; s++)
        {
            float t = 2 * MathF.PI * s / segments;
            float c = MathF.Cos(t), sn = MathF.Sin(t);
            var n = Vector3.Normalize(new Vector3(c, sn, slope));
            pos.Add(new Vector3(c * bottomRadius, sn * bottomRadius, -0.5f)); nor.Add(n);
            pos.Add(new Vector3(c * topRadius, sn * topRadius, 0.5f)); nor.Add(n);
        }
        for (int s = 0; s < segments; s++)
        {
            int a = 2 * s;
            idx.AddRange([a, a + 2, a + 1, a + 1, a + 2, a + 3]);
        }
        Cap(pos, nor, idx, bottomRadius, -0.5f, -1, segments);
        if (topRadius > 0) Cap(pos, nor, idx, topRadius, 0.5f, 1, segments);
        return new RenderMesh([.. pos], [.. nor], [.. idx]);
    }

    private static void Cap(List<Vector3> pos, List<Vector3> nor, List<int> idx, float radius, float z, int dir, int segments)
    {
        int center = pos.Count;
        var n = new Vector3(0, 0, dir);
        pos.Add(new Vector3(0, 0, z)); nor.Add(n);
        for (int s = 0; s <= segments; s++)
        {
            float t = 2 * MathF.PI * s / segments;
            pos.Add(new Vector3(MathF.Cos(t) * radius, MathF.Sin(t) * radius, z)); nor.Add(n);
        }
        for (int s = 0; s < segments; s++)
        {
            int a = center + 1 + s;
            if (dir > 0) idx.AddRange([center, a, a + 1]);
            else idx.AddRange([center, a + 1, a]);
        }
    }

    /// <summary>Capsule along Z with given radius and cylindrical length.</summary>
    public static RenderMesh Capsule(float radius, float height, int segments = 24, int rings = 8)
    {
        var pos = new List<Vector3>();
        var nor = new List<Vector3>();
        var idx = new List<int>();
        int ringCount = rings * 2 + 2;
        for (int r = 0; r < ringCount; r++)
        {
            bool top = r <= rings;
            float phi = top ? MathF.PI / 2 * r / rings : MathF.PI / 2 + MathF.PI / 2 * (r - rings - 1) / rings;
            float zOffset = top ? height / 2 : -height / 2;
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2 * MathF.PI * s / segments;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Sin(phi) * MathF.Sin(theta), MathF.Cos(phi));
                nor.Add(n);
                pos.Add(n * radius + new Vector3(0, 0, zOffset));
            }
        }
        int stride = segments + 1;
        for (int r = 0; r < ringCount - 1; r++)
        for (int s = 0; s < segments; s++)
        {
            int a = r * stride + s, b = a + stride;
            idx.AddRange([a, b, a + 1, a + 1, b, b + 1]);
        }
        return new RenderMesh([.. pos], [.. nor], [.. idx]);
    }

    /// <summary>Returns a shared unit mesh for a primitive type (scaled per instance by the renderer).</summary>
    public static RenderMesh Unit(ShapeType type, int detail = 1)
    {
        lock (CacheLock)
        {
            if (UnitCache.TryGetValue((type, detail), out var m)) return m;
            int seg = 16 * detail + 8;
            m = type switch
            {
                ShapeType.Box => Box(),
                ShapeType.Sphere or ShapeType.Ellipsoid => Sphere(seg, seg * 2 / 3),
                ShapeType.Cylinder => Cylinder(seg),
                ShapeType.Cone => Cone(seg),
                _ => Box(),
            };
            UnitCache[(type, detail)] = m;
            return m;
        }
    }
}
