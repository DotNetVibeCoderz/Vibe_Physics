# UI components

[Bahasa Indonesia](../id/ui-components.md)

All three layers share the same render model: `RenderWorld` turns a `ChSystem` into render items (one per visual shape) and refreshes their transforms each frame with a single bulk state read.

## Rendering (any app, headless included)

```csharp
using Chrono.Net.Rendering;

var world = new RenderWorld(sys, upIsZ: false) { ShowContacts = true };
var cam = new OrbitCamera { Target = new(0, 1, 0), Distance = 8, Yaw = 0.6f, Pitch = 0.4f };

Snapshot.SavePng(world, cam, "frame.png", 1920, 1080);          // 2× supersampled

using var gif = GifEncoder.Create("clip.gif", 640, 360);
var fb = new FrameBuffer(640, 360);
var renderer = new SoftwareRenderer();
for (int f = 0; f < 150; f++)
{
    sys.Step(0.002, 20);
    world.Update();
    renderer.Render(world, cam, fb);
    gif.AddFrame(fb, delayCs: 4);
}
```

`SoftwareRenderer` is a multithreaded rasterizer (z-buffer, Blinn-Phong, planar shadows, ground grid, FEA lines, contact points). `RenderOptions` controls colors, grid, shadows and the light.

## Avalonia

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://Chrono.Net.Avalonia/Themes/ChronoTheme.axaml" />
</Application.Styles>
```

```xml
xmlns:cn="clr-namespace:Chrono.Net.Avalonia;assembly=Chrono.Net.Avalonia"
<cn:ChronoViewport x:Name="Viewport" />
<cn:ChronoPlot x:Name="Plot" TimeWindow="8" Height="180" />
```

```csharp
var world = new RenderWorld(sys);
Viewport.World = world;
Viewport.FitToScene();
var energy = Plot.AddSeries("Kinetic energy", "J");

var host = new SimulationHost(sys.Step, timeStep: 0.002);
host.Frame += () =>
{
    world.Update();
    energy.Add(sys.Time, sys.KineticEnergy);
    Viewport.Refresh();
    Plot.Refresh();
};
host.Start();
```

| Control | Highlights |
|---|---|
| `ChronoViewport` | Orbit (left drag), pan (right/middle drag), zoom (wheel), refit (double click); `FollowBody`, `RenderScale`, `ShowStats`, `Options` |
| `ChronoPlot` | Ring-buffer series, stacked or shared axis, `TimeWindow` |
| `SimulationHost` | Wall-clock paced stepping with a per-frame budget, `Speed`, `RealTimeFactor`, `StepOnce` |

## Blazor (interactive server)

The simulation runs on the server and the browser renders with WebGL (three.js, bundled). Each frame sends only body transforms as a binary float array; if the browser has not acknowledged the previous frame, the next one is dropped, so a slow client lowers the frame rate rather than the physics rate.

![Blazor Gallery rendering the industrial mixer with WebGL](../images/web-mixer.png)

```razor
@rendermode InteractiveServer
<link rel="stylesheet" href="_content/Chrono.Net.Blazor/chrono.css" />

<ChronoViewport @ref="_view" World="_world" Camera="@(new ViewportCamera(new Vec3(0,1,0), 8))" />
<ChronoPlot Series="_series" TimeWindow="8" />

@code {
    ChronoViewport? _view;
    RenderWorld _world = default!;
    List<SignalSeries> _series = [new("Height", "m", "#F2A33A")];
    SimulationLoop _loop = default!;

    protected override void OnInitialized()
    {
        // … build sys …
        _world = new RenderWorld(sys);
        _loop = new SimulationLoop(sys.Step, 0.002) { Frame = OnFrame };
        _loop.Start();
    }

    async Task OnFrame()
    {
        _series[0].Add(sys.Time, ball.Position.Y);
        if (_view is not null) await InvokeAsync(_view.PushFrameAsync);
        await InvokeAsync(StateHasChanged);
    }
}
```

`ViewportCamera.FollowBody` keeps a body (such as a vehicle chassis) centered.

---
Made by Gravicode Studios, led by Kang Fadhil
