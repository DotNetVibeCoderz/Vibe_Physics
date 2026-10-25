using System.Numerics;
using System.Runtime.CompilerServices;

namespace Chrono.Net.Rendering;

/// <summary>32-bit BGRA color buffer plus depth buffer.</summary>
public sealed class FrameBuffer
{
    public FrameBuffer(int width, int height) => Resize(width, height);

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Pixels as 0xAARRGGBB (little-endian BGRA bytes) — directly compatible with Bgra8888 bitmaps.</summary>
    public uint[] Pixels { get; private set; } = [];
    public float[] Depth { get; private set; } = [];
    internal byte[] Stencil { get; private set; } = [];

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
        Depth = new float[width * height];
        Stencil = new byte[width * height];
    }

    /// <summary>Box-filters this buffer down by an integer factor (supersampling anti-aliasing).</summary>
    public FrameBuffer Downsample(int factor)
    {
        var dst = new FrameBuffer(Width / factor, Height / factor);
        int n = factor * factor;
        Parallel.For(0, dst.Height, y =>
        {
            for (int x = 0; x < dst.Width; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int j = 0; j < factor; j++)
                for (int i = 0; i < factor; i++)
                {
                    uint p = Pixels[(y * factor + j) * Width + x * factor + i];
                    r += (int)((p >> 16) & 255); g += (int)((p >> 8) & 255); b += (int)(p & 255);
                }
                dst.Pixels[y * dst.Width + x] = 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
            }
        });
        return dst;
    }
}

/// <summary>Visual settings for <see cref="SoftwareRenderer"/>.</summary>
public sealed class RenderOptions
{
    public Vector3 SkyTop { get; set; } = new(0.07f, 0.09f, 0.14f);
    public Vector3 SkyHorizon { get; set; } = new(0.20f, 0.25f, 0.33f);
    public Vector3 GroundColor { get; set; } = new(0.16f, 0.19f, 0.24f);
    public Vector3 GridColor { get; set; } = new(0.30f, 0.36f, 0.45f);
    /// <summary>Direction the light travels (towards the scene).</summary>
    public Vector3? LightDirection { get; set; }
    public bool DrawGround { get; set; } = true;
    public bool DrawGrid { get; set; } = true;
    public bool DrawShadows { get; set; } = true;
    public float GridSpacing { get; set; } = 1f;
    public float ShadowStrength { get; set; } = 0.45f;
    /// <summary>Ground plane height along the up axis; null = derived from the scene.</summary>
    public float? GroundHeight { get; set; }
    public Vector3 ContactColor { get; set; } = new(1f, 0.85f, 0.1f);
}

/// <summary>
/// Multithreaded CPU rasterizer with Gouraud/Blinn-Phong shading, z-buffer, planar shadows, grid and line rendering.
/// Works everywhere (headless servers, CI, desktop) and is used for screenshots, GIF export and the Avalonia viewport.
/// </summary>
public sealed class SoftwareRenderer
{
    private struct ScreenTri
    {
        public Vector3 P0, P1, P2;     // x, y in pixels; z = depth 0..1
        public Vector3 C0, C1, C2;     // linear colors
        public float MinX, MaxX, MinY, MaxY;
        public byte Mode;              // 0 = shaded, 1 = shadow (stencil), 2 = flat overlay (lines/grid)
    }

    private ScreenTri[] _tris = new ScreenTri[4096];
    private int _triCount;
    private readonly Lock _triLock = new();

    public RenderOptions Options { get; set; } = new();

    /// <summary>Statistics of the last frame.</summary>
    public int LastTriangleCount => _triCount;

    public void Render(RenderWorld world, OrbitCamera camera, FrameBuffer fb)
    {
        int w = fb.Width, h = fb.Height;
        var view = camera.View;
        var proj = camera.Projection(w / (float)h);
        var viewProj = view * proj;
        var up = camera.Up;
        var light = Vector3.Normalize(Options.LightDirection ?? (camera.UpIsZ ? new Vector3(-0.45f, -0.3f, -1f) : new Vector3(-0.45f, -1f, -0.3f)));
        var eye = camera.Position;

        float groundH = Options.GroundHeight ?? EstimateGroundHeight(world, up);

        Background(fb, camera);
        Array.Fill(fb.Depth, 1f);
        Array.Clear(fb.Stencil);

        // ---- pass 1: ground (scene ground items, or a synthetic plane) + grid
        _triCount = 0;
        bool hasGroundItems = false;
        foreach (var item in world.Items) if (item.IsGround) { hasGroundItems = true; break; }
        if (Options.DrawGround && !hasGroundItems) AddGroundPlane(camera, groundH, viewProj, w, h, light, eye);
        foreach (var item in world.Items.Concat(world.ExtraItems))
            if (item.IsGround) AddMesh(item, viewProj, w, h, light, eye, up, mode: 0);
        RasterizeAll(fb);

        // ---- pass 2: planar shadows onto the ground plane
        if (Options.DrawShadows)
        {
            _triCount = 0;
            var shadowMatrix = PlanarShadow(up, groundH + 0.002f, light);
            foreach (var item in world.Items.Concat(world.ExtraItems))
                if (item.CastsShadow && !item.IsGround) AddShadow(item, shadowMatrix, viewProj, w, h);
            RasterizeAll(fb);
            ApplyShadowStencil(fb);
        }

        if (Options.DrawGrid) DrawGrid(fb, camera, groundH, viewProj, w, h);

        // ---- pass 3: objects
        _triCount = 0;
        Parallel.ForEach(world.Items.Concat(world.ExtraItems).Where(i => !i.IsGround), item =>
            AddMesh(item, viewProj, w, h, light, eye, up, mode: 0));
        foreach (var line in world.AllLines()) AddLineSegments(line.Points, line.Color, line.Width, viewProj, w, h);
        if (world.ShowContacts)
            foreach (var c in world.Contacts) AddPoint(c.PointA.ToVector3(), Options.ContactColor, 5, viewProj, w, h);
        RasterizeAll(fb);
    }

    // ------------------------------------------------------------------ scene helpers

    private static float EstimateGroundHeight(RenderWorld world, Vector3 up)
    {
        float best = float.NegativeInfinity;
        foreach (var item in world.Items)
        {
            if (!item.IsGround) continue;
            var (mn, mx) = item.Mesh.Bounds();
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? mn.X : mx.X, (i & 2) == 0 ? mn.Y : mx.Y, (i & 4) == 0 ? mn.Z : mx.Z);
                best = MathF.Max(best, Vector3.Dot(Vector3.Transform(corner, item.World), up));
            }
        }
        if (!float.IsNegativeInfinity(best)) return best;
        var (min, _) = world.Bounds();
        return MathF.Min(0, Vector3.Dot(min, up));
    }

    private void Background(FrameBuffer fb, OrbitCamera camera)
    {
        var top = Options.SkyTop;
        var hor = Options.SkyHorizon;
        int w = fb.Width, h = fb.Height;
        // Horizon line moves with camera pitch.
        float horizon = 0.5f + MathF.Tan(camera.Pitch) / MathF.Tan(camera.FieldOfView / 2) * 0.5f;
        Parallel.For(0, h, y =>
        {
            float t = Math.Clamp(y / (float)h / MathF.Max(horizon, 0.05f), 0, 1);
            uint c = Pack(Vector3.Lerp(top, hor, t * t));
            fb.Pixels.AsSpan(y * w, w).Fill(c);
        });
    }

    private void AddGroundPlane(OrbitCamera camera, float height, Matrix4x4 viewProj, int w, int h, Vector3 light, Vector3 eye)
    {
        float size = MathF.Max(camera.Distance * 6, 40);
        var up = camera.Up;
        var center = camera.Target - up * Vector3.Dot(camera.Target, up) + up * height;
        Vector3 a1 = camera.UpIsZ ? Vector3.UnitX : Vector3.UnitX, a2 = camera.UpIsZ ? Vector3.UnitY : Vector3.UnitZ;
        const int n = 12;
        var col = Options.GroundColor;
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            Vector3 P(int u, int v) => center + a1 * ((u / (float)n - 0.5f) * size) + a2 * ((v / (float)n - 0.5f) * size);
            var p00 = P(i, j); var p10 = P(i + 1, j); var p11 = P(i + 1, j + 1); var p01 = P(i, j + 1);
            Vector3 Shade(Vector3 p)
            {
                float fade = Math.Clamp(1 - Vector3.Distance(p, center) / (size * 0.5f), 0, 1);
                return Vector3.Lerp(Options.SkyHorizon, col * (0.85f + 0.15f * MathF.Max(0, -Vector3.Dot(light, up))), MathF.Sqrt(fade));
            }
            ClipAndAdd(viewProj, w, h, p00, p10, p11, Shade(p00), Shade(p10), Shade(p11), 0, cull: false);
            ClipAndAdd(viewProj, w, h, p00, p11, p01, Shade(p00), Shade(p11), Shade(p01), 0, cull: false);
        }
    }

    private void DrawGrid(FrameBuffer fb, OrbitCamera camera, float height, Matrix4x4 viewProj, int w, int h)
    {
        _triCount = 0;
        float spacing = Options.GridSpacing;
        while (camera.Distance / spacing > 60) spacing *= 5;
        int lines = 40;
        var up = camera.Up;
        Vector3 a1 = Vector3.UnitX, a2 = camera.UpIsZ ? Vector3.UnitY : Vector3.UnitZ;
        var t = camera.Target;
        float c1 = MathF.Round(Vector3.Dot(t, a1) / spacing) * spacing, c2 = MathF.Round(Vector3.Dot(t, a2) / spacing) * spacing;
        var origin = a1 * c1 + a2 * c2 + up * (height + 0.004f);
        float half = lines / 2f * spacing;
        var pts = new List<Vector3>(lines * 4 + 4);
        for (int i = -lines / 2; i <= lines / 2; i++)
        {
            pts.Add(origin + a1 * (i * spacing) - a2 * half); pts.Add(origin + a1 * (i * spacing) + a2 * half);
            pts.Add(origin + a2 * (i * spacing) - a1 * half); pts.Add(origin + a2 * (i * spacing) + a1 * half);
        }
        AddLineSegments([.. pts], Options.GridColor, 1f, viewProj, w, h, fadeCenter: origin, fadeRadius: half);
        RasterizeAll(fb);
    }

    private static Matrix4x4 PlanarShadow(Vector3 n, float d, Vector3 l)
    {
        // Projects points onto plane n·x = d along direction l.
        float nl = Vector3.Dot(n, l);
        if (MathF.Abs(nl) < 1e-4f) nl = -1e-4f;
        // x' = x - l * (n·x - d) / (n·l)
        var m = Matrix4x4.Identity;
        m.M11 -= l.X * n.X / nl; m.M21 -= l.X * n.Y / nl; m.M31 -= l.X * n.Z / nl; m.M41 += l.X * d / nl;
        m.M12 -= l.Y * n.X / nl; m.M22 -= l.Y * n.Y / nl; m.M32 -= l.Y * n.Z / nl; m.M42 += l.Y * d / nl;
        m.M13 -= l.Z * n.X / nl; m.M23 -= l.Z * n.Y / nl; m.M33 -= l.Z * n.Z / nl; m.M43 += l.Z * d / nl;
        return m;
    }

    // ------------------------------------------------------------------ geometry → screen triangles

    private void AddMesh(RenderItem item, Matrix4x4 viewProj, int w, int h, Vector3 light, Vector3 eye, Vector3 up, byte mode)
    {
        var mesh = item.Mesh;
        var world = item.World;
        var pos = mesh.Positions;
        var nor = mesh.Normals;
        var idx = mesh.Indices;
        // Normal matrix (handles non-uniform scale).
        Matrix4x4.Invert(world, out var inv);
        var normalM = Matrix4x4.Transpose(inv);
        var baseColor = item.Color;

        Span<Vector3> wp = pos.Length <= 1024 ? stackalloc Vector3[pos.Length] : new Vector3[pos.Length];
        Span<Vector3> col = pos.Length <= 1024 ? stackalloc Vector3[pos.Length] : new Vector3[pos.Length];
        for (int i = 0; i < pos.Length; i++)
        {
            var p = Vector3.Transform(pos[i], world);
            var n = Vector3.TransformNormal(nor[i], normalM);
            float len = n.Length();
            n = len > 1e-12f ? n / len : up;
            wp[i] = p;
            col[i] = Shade(baseColor, n, p, light, eye, up);
        }
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            ClipAndAdd(viewProj, w, h, wp[a], wp[b], wp[c], col[a], col[b], col[c], mode, cull: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 Shade(Vector3 albedo, Vector3 n, Vector3 p, Vector3 light, Vector3 eye, Vector3 up)
    {
        var l = -light;
        float diff = MathF.Max(0, Vector3.Dot(n, l));
        float hemi = 0.5f + 0.5f * Vector3.Dot(n, up);
        var v = Vector3.Normalize(eye - p);
        var hv = Vector3.Normalize(l + v);
        float spec = MathF.Pow(MathF.Max(0, Vector3.Dot(n, hv)), 40) * 0.35f;
        float rim = MathF.Pow(1 - MathF.Max(0, Vector3.Dot(n, v)), 3) * 0.12f;
        var ambient = Vector3.Lerp(new Vector3(0.10f, 0.10f, 0.12f), new Vector3(0.28f, 0.31f, 0.36f), hemi);
        return albedo * (ambient + new Vector3(0.95f, 0.92f, 0.86f) * diff * 0.85f) + new Vector3(spec + rim);
    }

    private void AddShadow(RenderItem item, Matrix4x4 shadow, Matrix4x4 viewProj, int w, int h)
    {
        var mesh = item.Mesh;
        var m = item.World * shadow;
        var pos = mesh.Positions;
        var idx = mesh.Indices;
        Span<Vector3> wp = pos.Length <= 1024 ? stackalloc Vector3[pos.Length] : new Vector3[pos.Length];
        for (int i = 0; i < pos.Length; i++) wp[i] = Vector3.Transform(pos[i], m);
        for (int t = 0; t < idx.Length; t += 3)
            ClipAndAdd(viewProj, w, h, wp[idx[t]], wp[idx[t + 1]], wp[idx[t + 2]], default, default, default, 1, cull: false);
    }

    private void AddLineSegments(Vector3[] points, Vector3 color, float width, Matrix4x4 viewProj, int w, int h,
        Vector3? fadeCenter = null, float fadeRadius = 0)
    {
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            var a = Project(points[i], viewProj, w, h, out bool okA);
            var b = Project(points[i + 1], viewProj, w, h, out bool okB);
            if (!okA || !okB) continue;
            var dir = new Vector2(b.X - a.X, b.Y - a.Y);
            float len = dir.Length();
            if (len < 1e-3f) continue;
            var nrm = new Vector2(-dir.Y, dir.X) / len * (width * 0.5f);
            var off = new Vector3(nrm, 0);
            var ca = color; var cb = color;
            if (fadeCenter is { } fc)
            {
                // Fade towards the horizon color, like the synthetic ground plane, so far lines vanish into it.
                float fa = Math.Clamp(Vector3.Distance(points[i], fc) / fadeRadius, 0, 1);
                float fb = Math.Clamp(Vector3.Distance(points[i + 1], fc) / fadeRadius, 0, 1);
                ca = Vector3.Lerp(color, Vector3.Lerp(Options.GroundColor, Options.SkyHorizon, fa), fa);
                cb = Vector3.Lerp(color, Vector3.Lerp(Options.GroundColor, Options.SkyHorizon, fb), fb);
            }
            Emit(a - off, a + off, b + off, ca, ca, cb, 2);
            Emit(a - off, b + off, b - off, ca, cb, cb, 2);
        }
    }

    private void AddPoint(Vector3 p, Vector3 color, float size, Matrix4x4 viewProj, int w, int h)
    {
        var s = Project(p, viewProj, w, h, out bool ok);
        if (!ok) return;
        s.Z -= 0.0005f;
        var dx = new Vector3(size, 0, 0); var dy = new Vector3(0, size, 0);
        Emit(s - dx, s + dy, s + dx, color, color, color, 2);
        Emit(s - dx, s + dx, s - dy, color, color, color, 2);
    }

    private static Vector3 Project(Vector3 p, Matrix4x4 viewProj, int w, int h, out bool ok)
    {
        var c = Vector4.Transform(new Vector4(p, 1), viewProj);
        ok = c.W > 1e-5f && c.Z >= 0;
        float iw = 1 / c.W;
        return new Vector3((c.X * iw * 0.5f + 0.5f) * w, (0.5f - c.Y * iw * 0.5f) * h, c.Z * iw - 0.00002f);
    }

    private void ClipAndAdd(Matrix4x4 viewProj, int w, int h, Vector3 a, Vector3 b, Vector3 c,
        Vector3 ca, Vector3 cb, Vector3 cc, byte mode, bool cull)
    {
        var A = Vector4.Transform(new Vector4(a, 1), viewProj);
        var B = Vector4.Transform(new Vector4(b, 1), viewProj);
        var C = Vector4.Transform(new Vector4(c, 1), viewProj);
        bool ia = A.Z >= 0, ib = B.Z >= 0, ic = C.Z >= 0;
        if (!ia && !ib && !ic) return;
        // Trivial frustum rejection.
        if (A.X > A.W && B.X > B.W && C.X > C.W) return;
        if (A.X < -A.W && B.X < -B.W && C.X < -C.W) return;
        if (A.Y > A.W && B.Y > B.W && C.Y > C.W) return;
        if (A.Y < -A.W && B.Y < -B.W && C.Y < -C.W) return;

        if (ia && ib && ic) { ToScreen(A, B, C, ca, cb, cc, w, h, mode, cull); return; }

        // Clip against the near plane z = 0 (Sutherland–Hodgman on a triangle).
        Span<Vector4> inP = [A, B, C];
        Span<Vector3> inC = [ca, cb, cc];
        Span<Vector4> outP = stackalloc Vector4[4];
        Span<Vector3> outC = stackalloc Vector3[4];
        int n = 0;
        for (int i = 0; i < 3; i++)
        {
            var p = inP[i]; var q = inP[(i + 1) % 3];
            var cp = inC[i]; var cq = inC[(i + 1) % 3];
            bool pin = p.Z >= 0, qin = q.Z >= 0;
            if (pin) { outP[n] = p; outC[n] = cp; n++; }
            if (pin != qin)
            {
                float t = p.Z / (p.Z - q.Z);
                outP[n] = Vector4.Lerp(p, q, t); outC[n] = Vector3.Lerp(cp, cq, t); n++;
            }
        }
        if (n >= 3) ToScreen(outP[0], outP[1], outP[2], outC[0], outC[1], outC[2], w, h, mode, cull);
        if (n == 4) ToScreen(outP[0], outP[2], outP[3], outC[0], outC[2], outC[3], w, h, mode, cull);
    }

    private void ToScreen(Vector4 A, Vector4 B, Vector4 C, Vector3 ca, Vector3 cb, Vector3 cc, int w, int h, byte mode, bool cull)
    {
        static Vector3 S(Vector4 v, int w, int h)
        {
            float iw = 1 / MathF.Max(v.W, 1e-6f);
            return new Vector3((v.X * iw * 0.5f + 0.5f) * w, (0.5f - v.Y * iw * 0.5f) * h, v.Z * iw);
        }
        var p0 = S(A, w, h); var p1 = S(B, w, h); var p2 = S(C, w, h);
        float area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
        if (cull && area >= 0) return; // screen Y points down: CCW in world → negative area
        if (MathF.Abs(area) < 1e-6f) return;
        Emit(p0, p1, p2, ca, cb, cc, mode);
    }

    private void Emit(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 c0, Vector3 c1, Vector3 c2, byte mode)
    {
        var t = new ScreenTri
        {
            P0 = p0, P1 = p1, P2 = p2, C0 = c0, C1 = c1, C2 = c2, Mode = mode,
            MinX = MathF.Min(p0.X, MathF.Min(p1.X, p2.X)), MaxX = MathF.Max(p0.X, MathF.Max(p1.X, p2.X)),
            MinY = MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)), MaxY = MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)),
        };
        lock (_triLock)
        {
            if (_triCount == _tris.Length) Array.Resize(ref _tris, _tris.Length * 2);
            _tris[_triCount++] = t;
        }
    }

    // ------------------------------------------------------------------ rasterization

    private void RasterizeAll(FrameBuffer fb)
    {
        int bands = Math.Clamp(Environment.ProcessorCount * 2, 2, 32);
        int bandHeight = (fb.Height + bands - 1) / bands;
        var tris = _tris;
        int count = _triCount;
        Parallel.For(0, bands, band =>
        {
            int y0 = band * bandHeight, y1 = Math.Min(fb.Height, y0 + bandHeight);
            for (int i = 0; i < count; i++)
            {
                ref var t = ref tris[i];
                if (t.MaxY < y0 || t.MinY >= y1) continue;
                RasterizeTriangle(fb, ref t, y0, y1);
            }
        });
    }

    private static void RasterizeTriangle(FrameBuffer fb, ref ScreenTri t, int bandY0, int bandY1)
    {
        int w = fb.Width;
        int minX = Math.Max(0, (int)MathF.Floor(t.MinX));
        int maxX = Math.Min(w - 1, (int)MathF.Ceiling(t.MaxX));
        int minY = Math.Max(bandY0, (int)MathF.Floor(t.MinY));
        int maxY = Math.Min(bandY1 - 1, (int)MathF.Ceiling(t.MaxY));
        if (minX > maxX || minY > maxY) return;

        var p0 = t.P0; var p1 = t.P1; var p2 = t.P2;
        float area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
        float invArea = 1 / area;
        var pixels = fb.Pixels;
        var depth = fb.Depth;
        var stencil = fb.Stencil;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * w;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float w0 = ((p2.X - p1.X) * (py - p1.Y) - (p2.Y - p1.Y) * (px - p1.X)) * invArea;
                float w1 = ((p0.X - p2.X) * (py - p2.Y) - (p0.Y - p2.Y) * (px - p2.X)) * invArea;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;
                int idx = row + x;
                if (t.Mode == 1)
                {
                    if (z <= depth[idx] + 0.0008f) stencil[idx] = 1;
                    continue;
                }
                if (z >= depth[idx] || z < 0) continue;
                depth[idx] = z;
                pixels[idx] = Pack(w0 * t.C0 + w1 * t.C1 + w2 * t.C2);
            }
        }
    }

    private void ApplyShadowStencil(FrameBuffer fb)
    {
        float k = 1 - Options.ShadowStrength;
        var pixels = fb.Pixels;
        var stencil = fb.Stencil;
        Parallel.For(0, fb.Height, y =>
        {
            int row = y * fb.Width;
            for (int x = 0; x < fb.Width; x++)
            {
                int i = row + x;
                if (stencil[i] == 0) continue;
                uint p = pixels[i];
                uint r = (uint)(((p >> 16) & 255) * k), g = (uint)(((p >> 8) & 255) * k), b = (uint)((p & 255) * k);
                pixels[i] = 0xFF000000u | r << 16 | g << 8 | b;
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint Pack(Vector3 c)
    {
        // Simple filmic-ish tone curve keeps highlights from clipping harshly.
        c = c / (c + new Vector3(0.6f)) * 1.6f;
        uint r = (uint)Math.Clamp(c.X * 255f, 0, 255);
        uint g = (uint)Math.Clamp(c.Y * 255f, 0, 255);
        uint b = (uint)Math.Clamp(c.Z * 255f, 0, 255);
        return 0xFF000000u | r << 16 | g << 8 | b;
    }
}
