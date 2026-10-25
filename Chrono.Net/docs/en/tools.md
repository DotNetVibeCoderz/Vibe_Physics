# Tools

[Bahasa Indonesia](../id/tools.md)

## ChronoAppGen

The AI app generator IDE (explorer, editor, build/run/deploy, Jack the assistant, 23 project templates) has its own page: [ChronoAppGen](appgen.md).

## Chrono.Net Gallery

Two apps with the same 16 samples and the same instrument-bench design: a sample index, a live 3D viewport with a readout strip (time, step, bodies, links, contacts, real-time factor), scope-style signal plots, a bilingual description (EN/ID) and the exact C# source of each sample.

```powershell
dotnet run --project samples/Chrono.Net.Gallery.Avalonia     # desktop
dotnet run --project samples/Chrono.Net.Gallery.Blazor       # web: open the printed URL, e.g. /samples/four-bar
```

![Gallery](../images/gallery-four-bar.png)

| Category | Samples |
|---|---|
| Basics | Chaotic double pendulum, Newton's cradle, damping comparison |
| Mechanisms | Four-bar linkage, engine slider-crank, gear train |
| Contacts | Domino spiral, Galton board, industrial mixer, wrecking ball |
| Robotics | 3-DOF robot arm |
| FEA | Flexible cantilever, heavy rope pendulum (ANCF) |
| Vehicles | HMMWV ramp, sedan lane change, M113 tracked, Gator on deformable soil |

## Chrono.Net Simulator

Author and run JSON scenes: open, edit and save files (drag and drop works), "Apply & reload" from the JSON editor, inspect any body (pose, velocities, contact force, energy), plot height, speed and energy, export trajectories to CSV, and save 1080p snapshots.

```powershell
dotnet run --project tools/Chrono.Net.Simulator -- tools/Chrono.Net.Simulator/Scenes/box-pyramid.json
```

## CLI (`chrono`)

```text
chrono info                                   engine + native library status
chrono list                                   built-in samples
chrono run <scene.json|sample> [--duration s] [--dt s] [--csv file]
chrono render <scene.json|sample> --out file.png|file.gif [--width 960] [--height 540] [--duration s] [--fps 25]
chrono bench [--bodies 500] [--steps 1000]
chrono new <file.json> [--template pendulum|stack|empty]
chrono validate <scene.json>
chrono source <sample>
```

## Project templates

| Short name | Creates |
|---|---|
| `chrono-console` | Console simulation that writes `result.png` |
| `chrono-avalonia` | Desktop app: viewport, plot, run/pause |
| `chrono-blazor` | Blazor Web App (interactive server) with a WebGL viewport |

## Scene files

```jsonc
{
  "name": "Turntable",
  "settings": { "contactMethod": "Nsc", "gravity": [0, -9.81, 0], "timeStep": 0.002, "duration": 10,
                "solver": "Psor", "solverIterations": 80, "upAxis": "Y" },
  "materials": { "rubber": { "friction": 0.35, "restitution": 0.1 } },
  "bodies": [
    { "name": "ground", "shape": "Box", "size": [12, 1, 12], "position": [0, -0.5, 0], "fixed": true, "color": "#2A3342" },
    { "name": "disk", "shape": "Cylinder", "axis": "Y", "size": [1.6, 0.15], "position": [0, 0.3, 0], "density": 2000, "material": "rubber" },
    { "name": "block", "shape": "Box", "size": [0.25, 0.2, 0.25], "position": [0.5, 0.48, 0], "array": [5, 1, 1], "arraySpacing": [0.25, 0, 0] }
  ],
  "joints":  [ { "type": "Revolute", "body1": "lid", "body2": "ground", "position": [0, 1, 0], "axis": [0, 0, 1], "limits": [-1.2, 0] } ],
  "motors":  [ { "type": "RotationSpeed", "body1": "disk", "body2": "ground", "position": [0, 0.3, 0], "axis": [0, 1, 0],
                 "function": { "type": "ramp", "value": 0, "slope": 0.8 } } ],
  "springs": [ { "body1": "block[0,0,0]", "body2": "ground", "point1": [0.5, 0.5, 0], "point2": [0.5, 2, 0], "stiffness": 200, "damping": 5 } ]
}
```

- Shapes: `Box` (lengths), `Sphere` ([r]), `Cylinder` ([r, h] + `axis`), `Ellipsoid` (semi-axes), `Capsule` and `Cone` ([r, h], along local Z).
- `rotation` is Euler angles in degrees; `array` replicates a body in a grid, and copies are named `name[i,j,k]`.
- `"ground"` (or an omitted `body2`) refers to the body named `ground`, or to an implicit fixed anchor.
- Functions: `const` (`value`), `ramp` (`value`, `slope`), `sine` (`amplitude`, `frequency`, `phase`, `value` = offset), `poly345` (`value` = height, `width`).

---
Made by Gravicode Studios, led by Kang Fadhil
