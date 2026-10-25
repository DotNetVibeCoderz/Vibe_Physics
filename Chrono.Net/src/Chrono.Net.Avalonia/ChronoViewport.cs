using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Chrono.Net.Rendering;

namespace Chrono.Net.Avalonia;

using Color = global::Avalonia.Media.Color;

/// <summary>
/// Interactive 3D view of a Chrono system. Left-drag orbits, right/middle-drag pans, wheel zooms, double-click refits.
/// Rendering runs on the CPU (<see cref="SoftwareRenderer"/>) at the control's physical pixel size.
/// </summary>
public class ChronoViewport : Control
{
    public static readonly StyledProperty<RenderWorld?> WorldProperty =
        AvaloniaProperty.Register<ChronoViewport, RenderWorld?>(nameof(World));

    public static readonly StyledProperty<bool> ShowStatsProperty =
        AvaloniaProperty.Register<ChronoViewport, bool>(nameof(ShowStats), true);

    public static readonly StyledProperty<double> RenderScaleProperty =
        AvaloniaProperty.Register<ChronoViewport, double>(nameof(RenderScale), 1.0);

    private readonly SoftwareRenderer _renderer = new();
    private readonly FrameBuffer _fb = new(1, 1);
    private readonly Stopwatch _frameTimer = new();
    private WriteableBitmap? _bitmap;
    private Point _lastPointer;
    private bool _orbiting, _panning;
    private double _renderMs;

    static ChronoViewport()
    {
        AffectsRender<ChronoViewport>(WorldProperty);
        FocusableProperty.OverrideDefaultValue<ChronoViewport>(true);
        ClipToBoundsProperty.OverrideDefaultValue<ChronoViewport>(true);
    }

    public RenderWorld? World { get => GetValue(WorldProperty); set => SetValue(WorldProperty, value); }

    /// <summary>Draws the frame-time overlay.</summary>
    public bool ShowStats { get => GetValue(ShowStatsProperty); set => SetValue(ShowStatsProperty, value); }

    /// <summary>Resolution multiplier (lower = faster). 1 = native pixels.</summary>
    public double RenderScale { get => GetValue(RenderScaleProperty); set => SetValue(RenderScaleProperty, value); }

    public OrbitCamera Camera { get; } = new();
    public Rendering.RenderOptions Options => _renderer.Options;

    /// <summary>Optional body the camera target follows.</summary>
    public ChBody? FollowBody { get; set; }

    /// <summary>Text shown in the stats overlay in addition to frame timing.</summary>
    public string? StatusText { get; set; }

    /// <summary>
    /// Rasterizes the current world state into the backing bitmap and schedules a redraw. Call after stepping the
    /// simulation. Rasterization happens here, on the UI thread, never inside <see cref="Render"/>: locking a
    /// <see cref="WriteableBitmap"/> during the compositor's render pass can deadlock.
    /// </summary>
    public void Refresh()
    {
        RenderFrame();
        InvalidateVisual();
    }

    /// <summary>Frames all non-ground geometry.</summary>
    public void FitToScene()
    {
        if (World is null) return;
        var (min, max) = World.Bounds();
        Camera.Fit(min, max);
        Refresh();
    }

    private (int W, int H) PixelSizeFor(Size size)
    {
        double scaling = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * Math.Clamp(RenderScale, 0.25, 2);
        return (Math.Max(1, (int)(size.Width * scaling)), Math.Max(1, (int)(size.Height * scaling)));
    }

    private void RenderFrame()
    {
        var bounds = Bounds;
        if (World is not { } world || bounds.Width < 2 || bounds.Height < 2) return;
        var (w, h) = PixelSizeFor(bounds.Size);
        _frameTimer.Restart();
        Camera.UpIsZ = world.UpIsZ;
        if (FollowBody is { } f) Camera.Target = f.Position.ToVector3();
        _fb.Resize(w, h);
        _renderer.Render(world, Camera, _fb);
        Blit(w, h);
        _renderMs = _renderMs * 0.9 + _frameTimer.Elapsed.TotalMilliseconds * 0.1;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WorldProperty || change.Property == RenderScaleProperty) Refresh();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width < 2 || bounds.Height < 2) return;
        if (World is null || _bitmap is null)
        {
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(18, 22, 32)), new Rect(bounds.Size));
            return;
        }
        context.DrawImage(_bitmap, new Rect(_bitmap.Size), new Rect(bounds.Size));
        if (ShowStats) DrawStats(context);
    }

    private void Blit(int w, int h)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using var fb = _bitmap.Lock();
        var src = MemoryMarshal.AsBytes(_fb.Pixels.AsSpan(0, w * h));
        if (fb.RowBytes == w * 4)
        {
            unsafe { src.CopyTo(new Span<byte>((void*)fb.Address, src.Length)); }
        }
        else
        {
            for (int y = 0; y < h; y++)
                unsafe { src.Slice(y * w * 4, w * 4).CopyTo(new Span<byte>((byte*)fb.Address + y * fb.RowBytes, w * 4)); }
        }
    }

    private void DrawStats(DrawingContext ctx)
    {
        string text = $"{_renderMs:0.0} ms · {_renderer.LastTriangleCount:N0} tris";
        if (!string.IsNullOrEmpty(StatusText)) text = StatusText + "   " + text;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas, Cascadia Mono, monospace"), 11, new SolidColorBrush(Color.FromArgb(200, 203, 213, 225)));
        var r = new Rect(10, Bounds.Height - ft.Height - 14, ft.Width + 16, ft.Height + 8);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(140, 10, 14, 22)), r, 6);
        ctx.DrawText(ft, new Point(r.X + 8, r.Y + 4));
    }

    // ------------------------------------------------------------------ input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetCurrentPoint(this);
        _lastPointer = p.Position;
        if (e.ClickCount == 2) { FitToScene(); return; }
        _orbiting = p.Properties.IsLeftButtonPressed;
        _panning = p.Properties.IsRightButtonPressed || p.Properties.IsMiddleButtonPressed;
        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_orbiting && !_panning) return;
        var pos = e.GetPosition(this);
        var d = pos - _lastPointer;
        _lastPointer = pos;
        if (_orbiting) Camera.Orbit((float)(-d.X * 0.008), (float)(d.Y * 0.008));
        else Camera.Pan((float)(d.X / Bounds.Height), (float)(d.Y / Bounds.Height));
        Refresh();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _orbiting = _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom(e.Delta.Y > 0 ? 0.9f : 1.1f);
        Refresh();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
