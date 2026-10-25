namespace Chrono.Net.AppGen.Projects;

// Education, disaster & safety and tools templates.
public static partial class TemplateCatalog
{
    private static IEnumerable<ProjectTemplate> Education()
    {
        yield return new("pendulum-lab", "Pendulum Lab", "Education",
            "Four pendulums of 0.5, 1, 1.5 and 2 m released together. Measure each period and compare with T = 2π√(L/g).",
            ["revolute joint", "period", "classroom"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var anchor = Build.Anchor(sys);
                const double top = 2.6;
                anchor.AddVisualBox(new Vec3(0.08, 0.08, 3.4), Color.FromHex("#475569"), new Frame(new Vec3(0, top, 0)));
                double[] lengths = [0.5, 1.0, 1.5, 2.0];
                var bobs = new List<ChBody>();
                var scene = new Scene(sys) { TimeStep = 0.001, CameraTarget = new(0, 1.6, 0), CameraDistance = 6.5, CameraYawDeg = 75, CameraPitchDeg = 8 };
                for (int i = 0; i < lengths.Length; i++)
                {
                    var pivot = new Vec3(0, top, -1.2 + i * 0.8);
                    double a = Build.Rad(15);
                    var bob = sys.Add(ChBody.CreateSphere(0.08, 7800, collide: false)
                        .At(pivot + new Vec3(lengths[i] * Math.Sin(a), -lengths[i] * Math.Cos(a), 0)).Colored(Build.Palette[i]).Named($"L{lengths[i]}"));
                    sys.Add(ChJoint.Revolute(bob, anchor, pivot, Vec3.UnitZ));
                    scene.Tether(() => pivot, () => bob.Position);
                    bobs.Add(bob);
                }
                for (int i = 0; i < lengths.Length; i++)
                {
                    var b = bobs[i];
                    var pivot = new Vec3(0, top, -1.2 + i * 0.8);
                    scene.Probe($"Angle L={lengths[i]} m", "deg", () => Math.Atan2(b.Position.X - pivot.X, pivot.Y - b.Position.Y) * 180 / Math.PI);
                }
                return scene.Probe("Theory T(1 m)", "s", () => 2 * Math.PI * Math.Sqrt(1.0 / 9.81));
            }
            """);

        yield return new("inclined-plane", "Friction on an Inclined Plane", "Education",
            "Three blocks with friction coefficients 0.1, 0.3 and 0.6 on a 25° slope. Blocks slide only when μ < tan 25° ≈ 0.47 — see which one stays.",
            ["friction", "materials", "classroom"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var floor = sys.Own(new ContactMaterial { Friction = 0.5f });
                Build.Ground(sys, floor, 20);
                const double angle = 25;
                var q = Quat.FromAngleZ(Build.Rad(-angle));
                double[] mu = [0.1, 0.3, 0.6];
                var blocks = new List<ChBody>();
                for (int i = 0; i < mu.Length; i++)
                {
                    var m = sys.Own(new ContactMaterial { Friction = (float)mu[i], Restitution = 0 });
                    double z = -1.2 + i * 1.2;
                    sys.Add(ChBody.CreateBox(new(6, 0.2, 1.0), 1000, m).At(0, 1.6, z).Rotated(q).Fixed().Colored(Color.FromHex("#475569")));
                    // Place each block on the slope surface, near the top.
                    var onSlope = new Vec3(0, 1.6, z) + q.Rotate(new Vec3(-2.2, 0.1 + 0.2 + 0.005, 0));
                    blocks.Add(sys.Add(ChBody.CreateBox(new(0.5, 0.4, 0.5), 800, m).At(onSlope).Rotated(q).Colored(Build.Palette[i]).Named($"mu{mu[i]}")));
                }
                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 1.2, 0), CameraDistance = 9, CameraYawDeg = 20, CameraPitchDeg = 14 }
                    .Probe("Speed μ=0.1", "m/s", () => blocks[0].LinearVelocity.Length)
                    .Probe("Speed μ=0.3", "m/s", () => blocks[1].LinearVelocity.Length)
                    .Probe("Speed μ=0.6", "m/s", () => blocks[2].LinearVelocity.Length);
            }
            """);

        yield return new("momentum-carts", "Momentum & Collisions", "Education",
            "Two pairs of carts on frictionless tracks: one collision is elastic, the other perfectly inelastic. Total momentum is conserved in both; kinetic energy only in one.",
            ["collisions", "restitution", "conservation laws"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var anchor = Build.Anchor(sys);
                var elastic = sys.Own(new ContactMaterial { Friction = 0, Restitution = 1 });
                var plastic = sys.Own(new ContactMaterial { Friction = 0, Restitution = 0 });
                anchor.AddVisualBox(new Vec3(10, 0.05, 0.4), Color.FromHex("#475569"), new Frame(new Vec3(0, 0.45, -0.8)));
                anchor.AddVisualBox(new Vec3(10, 0.05, 0.4), Color.FromHex("#475569"), new Frame(new Vec3(0, 0.45, 0.8)));

                (ChBody A, ChBody B) Pair(double z, ContactMaterial m, Color color)
                {
                    var a = sys.Add(ChBody.CreateBox(new(0.5, 0.4, 0.4), 1000, m).At(-3, 0.7, z).Colored(color).WithVelocity(new Vec3(2, 0, 0)));
                    var b = sys.Add(ChBody.CreateBox(new(0.5, 0.4, 0.4), 2000, m).At(0, 0.7, z).Colored(Color.Gray));
                    sys.Add(ChJoint.Prismatic(a, anchor, a.Position, Vec3.UnitX));
                    sys.Add(ChJoint.Prismatic(b, anchor, b.Position, Vec3.UnitX));
                    return (a, b);
                }
                var e = Pair(-0.8, elastic, Color.Teal);
                var p = Pair(0.8, plastic, Color.Orange);
                double Momentum((ChBody A, ChBody B) c) => c.A.Mass * c.A.LinearVelocity.X + c.B.Mass * c.B.LinearVelocity.X;
                double Energy((ChBody A, ChBody B) c) => c.A.KineticEnergy + c.B.KineticEnergy;

                return new Scene(sys) { TimeStep = 0.001, CameraTarget = new(0, 0.6, 0), CameraDistance = 9, CameraYawDeg = 10, CameraPitchDeg = 25 }
                    .Probe("Momentum (elastic)", "kg·m/s", () => Momentum(e))
                    .Probe("Momentum (inelastic)", "kg·m/s", () => Momentum(p))
                    .Probe("Energy (elastic)", "J", () => Energy(e))
                    .Probe("Energy (inelastic)", "J", () => Energy(p));
            }
            """);
    }

    private static IEnumerable<ProjectTemplate> Safety()
    {
        yield return new("earthquake-shake-table", "Earthquake Shake Table", "Disaster & Safety",
            "A shake table drives a stacked block building with a 1.5 Hz ground motion that ramps up. Compare a braced and an unbraced tower.",
            ["position motor", "seismic", "stacking"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.MaxPenetrationRecoverySpeed = 0.05;
                sys.SetSolver(SolverType.Psor, 80);
                var mat = sys.Own(new ContactMaterial { Friction = 0.5f, Restitution = 0 });
                var anchor = Build.Anchor(sys);
                Build.Ground(sys, mat, 20).SetColor(Color.FromHex("#1F2937"));

                var table = sys.Add(ChBody.CreateBox(new(5, 0.3, 3), 3000, mat).At(0, 0.2, 0).Colored(Color.FromHex("#475569")).Named("table"));
                // Ground motion: 1.5 Hz, amplitude ramps to 0.12 m over 6 s.
                sys.Add(ChMotor.LinearPosition(table, anchor, table.Position, Vec3.UnitX,
                    ChFunction.FromDelegate(t => Math.Min(1, t / 6) * 0.12 * Math.Sin(2 * Math.PI * 1.5 * t))));

                var blocks = new List<(ChBody Body, double Height)>();
                void Tower(double x, bool braced)
                {
                    const double w = 0.6, h = 0.5;
                    for (int level = 0; level < 6; level++)
                    {
                        double y = 0.35 + h / 2 + 0.003 + level * (h + 0.003);
                        var b = sys.Add(ChBody.CreateBox(new(w, h, w), 900, mat).At(x, y, 0).Colored(braced ? Color.Teal : Color.Orange));
                        if (braced && level > 0) sys.Add(ChJoint.Lock(b, blocks[^1].Body, new Vec3(x, y - h / 2, 0)));
                        blocks.Add((b, y));
                    }
                }
                Tower(-1.2, braced: false);
                int firstBraced = blocks.Count;
                Tower(1.2, braced: true);

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 1.6, 0), CameraDistance = 9, CameraYawDeg = 15, CameraPitchDeg = 12 }
                    .Probe("Table x", "m", () => table.Position.X)
                    .Probe("Unbraced fallen", "", () => blocks.Take(firstBraced).Count(b => b.Body.Position.Y < b.Height - 0.25))
                    .Probe("Braced top drift", "m", () => blocks[^1].Body.Position.X - table.Position.X - 1.2);
            }
            """);

        yield return new("rockfall-barrier", "Rockfall Protection Barrier", "Disaster & Safety",
            "Boulders roll down a slope into a row of steel posts. Count the rocks the barrier stops and the hardest hit it takes.",
            ["impact", "slope", "protective structure"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.SetSolver(SolverType.Psor, 60);
                var rock = sys.Own(new ContactMaterial { Friction = 0.7f, Restitution = 0.2f, RollingFriction = 0.01f });
                Build.Ground(sys, rock, 40);
                var slope = Quat.FromAngleZ(Build.Rad(-22));
                sys.Add(ChBody.CreateBox(new(14, 0.6, 6), 1000, rock).At(-6, 2.4, 0).Rotated(slope).Fixed().Colored(Color.FromHex("#57534E")));

                var posts = new List<ChBody>();
                for (int i = 0; i < 9; i++)
                    posts.Add(sys.Add(ChBody.CreateCylinder(Axis.Y, 0.08, 1.8, 7800, rock).At(2.5, 0.9, -2.4 + i * 0.6).Fixed().Colored(Color.FromHex("#CBD5E1"))));
                // A rail across the post tops.
                sys.Add(ChBody.CreateBox(new(0.12, 0.12, 5.2), 7800, rock).At(2.5, 1.75, 0).Fixed().Colored(Color.FromHex("#94A3B8")));

                var rng = new Random(4);
                var boulders = new List<ChBody>();
                for (int i = 0; i < 10; i++)
                {
                    double r = 0.25 + rng.NextDouble() * 0.2;
                    var start = new Vec3(-11 + (i % 2) * 1.2, 0, -2 + (i / 2) * 1.0);
                    start = new Vec3(start.X, 2.4 + (start.X + 6) * -Math.Tan(Build.Rad(22)) + 0.3 / Math.Cos(Build.Rad(22)) + r + 0.05 + (i / 2) * 0.02, start.Z);
                    boulders.Add(sys.Add(ChBody.CreateSphere(r, 2600, rock).At(start).Colored(Color.FromHex("#A8A29E"))));
                }
                double maxHit = 0;
                sys.AfterStep += (_, _) => { foreach (var p in posts) maxHit = Math.Max(maxHit, p.ContactForce.Length); };

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(-2, 1.5, 0), CameraDistance = 17, CameraYawDeg = 25, CameraPitchDeg = 16 }
                    .Probe("Rocks stopped", "", () => boulders.Count(b => b.Position.X < 2.5 && b.Position.Y < 1.2 && b.LinearVelocity.Length < 0.3))
                    .Probe("Rocks passed", "", () => boulders.Count(b => b.Position.X > 2.7))
                    .Probe("Max post load", "kN", () => maxHit / 1000);
            }
            """);

        yield return new("crash-test", "Crash Test Sled", "Disaster & Safety",
            "A 1.2 t sled hits a rigid barrier at 50 km/h. A spring-damper crumple zone and a belted occupant show how deceleration (g) depends on crush stiffness.",
            ["springs", "deceleration", "occupant safety"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var mat = sys.Own(new ContactMaterial { Friction = 0.02f, Restitution = 0.05f });
                Build.Ground(sys, mat, 60);
                sys.Add(ChBody.CreateBox(new(1, 2, 4), 2000, mat).At(10.5, 1, 0).Fixed().Colored(Color.FromHex("#475569")).Named("barrier"));

                const double speed = 50 / 3.6;
                var sled = sys.Add(ChBody.CreateBox(new(3, 0.6, 1.6), 400, mat).At(0, 0.32, 0).Colored(Color.FromHex("#E5E7EB")).Named("sled").WithVelocity(new Vec3(speed, 0, 0)));
                sled.Mass = 1200;
                // Crumple zone: a bumper 1 m ahead of the sled on a prismatic guide, held by a stiff spring-damper.
                // Stiffness is chosen so the crash energy (≈116 kJ) is absorbed in less than the 0.85 m of free stroke.
                var bumper = sys.Add(ChBody.CreateBox(new(0.3, 0.5, 1.6), 300, mat).At(2.5, 0.32, 0).Colored(Color.Orange).Named("bumper").WithVelocity(new Vec3(speed, 0, 0)));
                sys.Add(ChJoint.Prismatic(bumper, sled, bumper.Position, Vec3.UnitX));
                var crumple = sys.Add(new ChSpring(sled, bumper, new Vec3(1.5, 0.32, 0), bumper.Position, 4.0e5, 2.0e4));
                // Occupant: a block on the seat, belted by a spring to the sled.
                var dummy = sys.Add(ChBody.CreateBox(new(0.4, 0.8, 0.4), 950, collide: false).At(-0.2, 1.02, 0).Colored(Color.Yellow).Named("dummy").WithVelocity(new Vec3(speed, 0, 0)));
                sys.Add(ChJoint.Prismatic(dummy, sled, dummy.Position, Vec3.UnitX));
                sys.Add(new ChSpring(sled, dummy, new Vec3(-0.6, 1.02, 0), dummy.Position, 6e4, 3e3));

                double prevSled = speed, prevDummy = speed, gSled = 0, gDummy = 0, peak = 0;
                sys.AfterStep += (_, dt) =>
                {
                    double vs = sled.LinearVelocity.X, vd = dummy.LinearVelocity.X;
                    gSled = -(vs - prevSled) / dt / 9.81;
                    gDummy = -(vd - prevDummy) / dt / 9.81;
                    peak = Math.Max(peak, gDummy);
                    prevSled = vs;
                    prevDummy = vd;
                };

                return new Scene(sys) { TimeStep = 0.0005, CameraTarget = new(8, 0.8, 0), CameraDistance = 9, CameraYawDeg = 30, CameraPitchDeg = 12 }
                    .Probe("Sled speed", "km/h", () => sled.LinearVelocity.X * 3.6)
                    .Probe("Sled decel", "g", () => gSled)
                    .Probe("Occupant decel", "g", () => gDummy)
                    .Probe("Peak occupant", "g", () => peak)
                    .Probe("Crush", "m", () => 1.0 - crumple.Length);
            }
            """);
    }

    private static IEnumerable<ProjectTemplate> Tools()
    {
        yield return new("json-scene-player", "JSON Scene Player", "Tools",
            "Loads scene.json (bodies, joints, motors, springs) with Chrono.Net's scene loader, so you can design rigs without recompiling. Edit the JSON and press Reset.",
            ["scene files", "no-code", "loader"], AppKind.Desktop, """
            public static Scene Create()
            {
                string path = Path.Combine(AppContext.BaseDirectory, "scene.json");
                var loaded = Chrono.Net.Scenes.SceneLoader.LoadFile(path);
                var first = loaded.Bodies.Values.FirstOrDefault(b => !b.IsFixed);
                var scene = new Scene(loaded.System)
                {
                    TimeStep = loaded.TimeStep > 0 ? loaded.TimeStep : 0.002,
                    CameraTarget = new(0, 0.5, 0), CameraDistance = 6, CameraYawDeg = 35, CameraPitchDeg = 28,
                };
                scene.Probe("Kinetic energy", "J", () => loaded.System.KineticEnergy);
                if (first is not null) scene.Probe($"{first.Name} speed", "m/s", () => first.LinearVelocity.Length);
                return scene.Probe("Contacts", "", () => loaded.System.ContactCount);
            }
            """)
        {
            ExtraFiles = [("scene.json", SceneJson)],
        };

        yield return new("parameter-sweep", "Parameter Sweep (CSV)", "Tools",
            "A console tool that runs one simulation per parameter value (pendulum length × damping) and writes the measured periods to sweep.csv for Excel or Python.",
            ["console", "batch", "CSV export"], AppKind.Console, """
            /// <summary>One headless pendulum, used by the sweep and by <c>--selftest</c>.</summary>
            public static Scene Create() => Create(1.0, 0.0);

            public static Scene Create(double length, double damping)
            {
                var sys = new ChSystem();
                var anchor = Build.Anchor(sys);
                var bob = sys.Add(ChBody.CreateSphere(0.05, 7800, collide: false).At(length * Math.Sin(0.2), -length * Math.Cos(0.2), 0));
                sys.Add(ChJoint.Revolute(bob, anchor, Vec3.Zero, Vec3.UnitZ));
                if (damping > 0) sys.BeforeStep += (_, _) => { bob.ClearForces(); bob.AddForce(bob.LinearVelocity * -damping); };
                return new Scene(sys) { TimeStep = 0.0005 }
                    .Probe("x", "m", () => bob.Position.X);
            }

            /// <summary>Measures the period from successive upward zero crossings of x.</summary>
            private static double Period(double length, double damping)
            {
                using var scene = Create(length, damping);
                var x = scene.Probes[0].Read;
                double last = x(), t0 = double.NaN;
                while (scene.System.Time < 12)
                {
                    scene.Step(scene.TimeStep);
                    double now = x();
                    if (last < 0 && now >= 0)
                    {
                        if (double.IsNaN(t0)) t0 = scene.System.Time;
                        else return scene.System.Time - t0;
                    }
                    last = now;
                }
                return double.NaN;
            }

            public static int Run(string[] args)
            {
                if (args.Contains("--selftest"))
                {
                    using var scene = Create();
                    return Runner.StepAndPrint(scene, Runner.Seconds(args, 3));
                }
                var csv = new System.Text.StringBuilder("length_m,damping_Ns_per_m,period_s,theory_s\n");
                foreach (double length in new[] { 0.25, 0.5, 1.0, 2.0 })
                foreach (double damping in new[] { 0.0, 0.5, 2.0 })
                {
                    double period = Period(length, damping);
                    double theory = 2 * Math.PI * Math.Sqrt(length / 9.81);
                    Console.WriteLine($"L={length,4:0.00} m  c={damping,3:0.0}  T={period:0.0000} s  (small-angle theory {theory:0.0000} s)");
                    csv.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{length},{damping},{period:0.00000},{theory:0.00000}"));
                }
                File.WriteAllText("sweep.csv", csv.ToString());
                Console.WriteLine("Wrote sweep.csv");
                return 0;
            }
            """);
    }

    private const string SceneJson = """
        {
          "name": "Motorized turntable",
          "description": "A speed motor spins a disk; blocks slide off when friction can no longer supply the centripetal force.",
          "settings": { "timeStep": 0.002, "duration": 10 },
          "materials": { "rubber": { "friction": 0.35 } },
          "bodies": [
            { "name": "ground", "shape": "Box", "size": [12, 1, 12], "position": [0, -0.5, 0], "fixed": true, "color": "#2A3342", "material": "rubber" },
            { "name": "disk", "shape": "Cylinder", "axis": "Y", "size": [1.6, 0.15], "position": [0, 0.3, 0], "density": 2000, "material": "rubber", "color": "#334155" },
            { "name": "block0", "shape": "Box", "size": [0.25, 0.2, 0.25], "position": [0.3, 0.48, 0], "density": 600, "material": "rubber", "color": "#F2A33A" },
            { "name": "block1", "shape": "Box", "size": [0.25, 0.2, 0.25], "position": [0.6, 0.48, 0], "density": 600, "material": "rubber", "color": "#F2A33A" },
            { "name": "block2", "shape": "Box", "size": [0.25, 0.2, 0.25], "position": [0.9, 0.48, 0], "density": 600, "material": "rubber", "color": "#F2A33A" },
            { "name": "block3", "shape": "Box", "size": [0.25, 0.2, 0.25], "position": [1.2, 0.48, 0], "density": 600, "material": "rubber", "color": "#3CC8B4" }
          ],
          "joints": [],
          "motors": [
            { "name": "spindle", "type": "RotationSpeed", "body1": "disk", "body2": "ground", "position": [0, 0.3, 0], "axis": [0, 1, 0],
              "function": { "type": "ramp", "value": 0, "slope": 0.8 } }
          ]
        }
        """;
}
