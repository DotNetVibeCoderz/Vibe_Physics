namespace Chrono.Net.AppGen.Projects;

// Simulator, science and military templates.
public static partial class TemplateCatalog
{
    private static IEnumerable<ProjectTemplate> Simulators()
    {
        yield return new("vehicle-test-track", "Vehicle Test Track", "Simulator",
            "A full HMMWV model (powertrain, suspension, TMeasy tires) drives a cone slalom. Tune the driver law and watch speed, steering and lateral offset.",
            ["Chrono::Vehicle", "driver model", "slalom"], AppKind.Desktop, """
            public static Scene Create()
            {
                var car = Vehicle.Create(VehicleModel.Hmmwv, new Vec3(-40, 0, 0.6));
                var terrain = new RigidTerrain(car.System).AddPatch(Frame.Identity, 220, 60).Initialize();
                car.System.SetSolver(SolverType.BarzilaiBorwein, 60).SetTimestepper(TimestepperType.EulerImplicitLinearized);

                // Slalom cones (visual only) every 14 m, alternating sides.
                var cones = Build.Anchor(car.System);
                for (int i = 0; i < 8; i++)
                    cones.AddVisualCone(0.3, 0.8, Color.Orange, new Frame(new Vec3(-10 + i * 14, i % 2 == 0 ? 2.2 : -2.2, 0.4)));

                // Driver: accelerate, then weave between the cones.
                DriverInputs Driver(double t) => new(
                    Steering: t < 3 ? 0 : 0.32 * Math.Sin(2 * Math.PI * (t - 3) / 4.2),
                    Throttle: t < 1 ? 0 : 0.5,
                    Braking: 0);

                var scene = new Scene(car.System)
                {
                    TimeStep = 0.002, UpIsZ = true, Follow = car.Chassis,
                    CameraTarget = car.Chassis.Position, CameraDistance = 12, CameraYawDeg = -150, CameraPitchDeg = 18,
                    StepOverride = dt => car.Step(terrain, Driver(car.System.Time), dt),
                };
                scene.Owned.Add(terrain);
                scene.Owned.Add(car);
                return scene
                    .Probe("Speed", "km/h", () => car.Telemetry.SpeedKmh)
                    .Probe("Steering", "", () => car.Telemetry.Steering)
                    .Probe("Lateral offset", "m", () => car.Chassis.Position.Y);
            }
            """) { RequiresVehicle = true };

        yield return new("crane-operator", "Gantry Crane Operator", "Simulator",
            "A trolley carries a crate on a cable across a gantry. The move profile is smooth, but the load still swings: try to shape the motion to cancel it.",
            ["linear motor", "distance constraint", "swing"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var mat = sys.Own(new ContactMaterial { Friction = 0.6f });
                Build.Ground(sys, mat, 30);
                var anchor = Build.Anchor(sys);
                var steel = Color.FromHex("#475569");
                anchor.AddVisualBox(new Vec3(16, 0.3, 0.4), steel, new Frame(new Vec3(0, 8, 0)));
                anchor.AddVisualBox(new Vec3(0.4, 8, 0.4), steel, new Frame(new Vec3(-8, 4, 0)));
                anchor.AddVisualBox(new Vec3(0.4, 8, 0.4), steel, new Frame(new Vec3(8, 4, 0)));

                var trolley = sys.Add(ChBody.CreateBox(new(1, 0.4, 0.7), 2000, collide: false).At(-5, 7.6, 0).Colored(Color.Orange).Named("trolley"));
                // Travel 10 m right with a smooth S-curve, wait, come back.
                static double S(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
                sys.Add(ChMotor.LinearPosition(trolley, anchor, trolley.Position, Vec3.UnitX,
                    ChFunction.FromDelegate(t => 10 * (S((t - 1) / 5) - S((t - 10) / 5)))));

                var crate = sys.Add(ChBody.CreateBox(new(1.2, 1, 1.2), 500, mat).At(-5, 2.6, 0).Colored(Color.FromHex("#B45309")).Named("crate"));
                var hookTop = new Vec3(0, -0.2, 0);
                var hookLoad = new Vec3(0, 0.5, 0);
                var cable = sys.Add(new ChDistanceConstraint(trolley, crate, trolley.Position + hookTop, crate.Position + hookLoad));

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 4, 0), CameraDistance = 20, CameraYawDeg = 15, CameraPitchDeg = 12 }
                    .Tether(() => trolley.Frame.TransformPoint(hookTop), () => crate.Frame.TransformPoint(hookLoad))
                    .Probe("Trolley x", "m", () => trolley.Position.X)
                    .Probe("Swing angle", "deg", () => Math.Atan2(crate.Position.X - trolley.Position.X, trolley.Position.Y - crate.Position.Y) * 180 / Math.PI)
                    .Probe("Cable tension", "N", () => cable.Reaction.Item1.Length);
            }
            """);
    }

    private static IEnumerable<ProjectTemplate> Science()
    {
        yield return new("projectile-lab", "Projectile & Drag Lab", "Science",
            "Four balls launched at 30°, 45°, 60° and 75° with quadratic air drag, next to a 45° ball in vacuum. Compare ranges against the textbook formula.",
            ["ballistics", "air drag", "custom forces"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var mat = sys.Own(new ContactMaterial { Friction = 0.9f, Restitution = 0.1f });
                Build.Ground(sys, mat, 120);

                const double speed = 15, radius = 0.11, rho = 1.2, cd = 0.25;
                double k = 0.5 * rho * cd * Math.PI * radius * radius;      // drag = -k |v| v
                var angles = new[] { 30.0, 45, 60, 75 };
                var dragged = new List<ChBody>();
                for (int i = 0; i < angles.Length; i++)
                {
                    double a = Build.Rad(angles[i]);
                    var ball = ChBody.CreateSphere(radius, 80, mat).At(0, radius + 0.01, -4.5 + i * 2).Colored(Build.Palette[i]).Named($"ball{angles[i]}")
                        .WithVelocity(new Vec3(speed * Math.Cos(a), speed * Math.Sin(a), 0));
                    dragged.Add(sys.Add(ball));
                }
                var vacuum = sys.Add(ChBody.CreateSphere(radius, 80, mat).At(0, radius + 0.01, 4.5).Colored(Color.Gray).Named("vacuum45")
                    .WithVelocity(new Vec3(speed * Math.Cos(Math.PI / 4), speed * Math.Sin(Math.PI / 4), 0)));

                sys.BeforeStep += (_, _) =>
                {
                    foreach (var b in dragged)
                    {
                        b.ClearForces();
                        var v = b.LinearVelocity;
                        b.AddForce(v * (-k * v.Length));
                    }
                };

                // Landing distance = x at the first touchdown (the balls roll on afterwards).
                var all = dragged.Append(vacuum).ToList();
                var landed = new double?[all.Count];
                sys.AfterStep += (s, _) =>
                {
                    for (int i = 0; i < all.Count; i++)
                        if (landed[i] is null && s.Time > 0.2 && all[i].Position.Y < radius + 0.02) landed[i] = all[i].Position.X;
                };

                double ideal = speed * speed / 9.81;   // vacuum range at 45°
                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(11, 3, 0), CameraDistance = 30, CameraYawDeg = 0, CameraPitchDeg = 14 }
                    .Probe("Landing 45° drag", "m", () => landed[1] ?? dragged[1].Position.X)
                    .Probe("Landing 45° vacuum", "m", () => landed[4] ?? vacuum.Position.X)
                    .Probe("Ideal range", "m", () => ideal)
                    .Probe("Landing 30° drag", "m", () => landed[0] ?? dragged[0].Position.X);
            }
            """);

        yield return new("orbital-mechanics", "Orbital Mechanics", "Science",
            "Three planets on circular orbits and a comet on an eccentric one around a star, with Newtonian gravity applied as forces. The total energy should stay constant.",
            ["gravity", "custom forces", "energy conservation"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem { Gravity = Vec3.Zero };
                const double GM = 40;
                sys.Add(ChBody.CreateSphere(0.9, 1000, collide: false).Fixed().Colored(Color.Yellow).Named("star"));

                var bodies = new List<ChBody>();
                ChBody Orbiter(string name, double r, double speedFactor, double size, Color color)
                {
                    double v = Math.Sqrt(GM / r) * speedFactor;
                    var b = ChBody.CreateSphere(size, 500, collide: false).At(r, 0, 0).Colored(color).Named(name).WithVelocity(new Vec3(0, 0, -v));
                    bodies.Add(sys.Add(b));
                    return b;
                }
                Orbiter("mercury", 3.5, 1, 0.18, Color.Gray);
                Orbiter("earth", 6, 1, 0.3, Color.Blue);
                Orbiter("mars", 9, 1, 0.24, Color.Red);
                var comet = Orbiter("comet", 12, 0.55, 0.15, Color.Teal);

                sys.BeforeStep += (_, _) =>
                {
                    foreach (var b in bodies)
                    {
                        b.ClearForces();
                        var d = -b.Position;
                        double r = d.Length;
                        b.AddForce(d.Normalized() * (GM * b.Mass / (r * r)));
                    }
                };
                double Energy() => bodies.Sum(b => 0.5 * b.Mass * b.LinearVelocity.LengthSquared - GM * b.Mass / b.Position.Length);
                double e0 = Energy();

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = Vec3.Zero, CameraDistance = 30, CameraYawDeg = 20, CameraPitchDeg = 55 }
                    .Probe("Comet distance", "", () => comet.Position.Length)
                    .Probe("Comet speed", "", () => comet.LinearVelocity.Length)
                    .Probe("Energy drift", "%", () => 100 * (Energy() - e0) / Math.Abs(e0));
            }
            """);

        yield return new("coupled-oscillators", "Coupled Oscillators", "Science",
            "Three carts on a frictionless rail linked by springs. Displace one and watch energy move between the normal modes.",
            ["springs", "normal modes", "prismatic joint"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var anchor = Build.Anchor(sys);
                var slate = Color.FromHex("#475569");
                anchor.AddVisualBox(new Vec3(8.6, 0.06, 0.5), slate, new Frame(new Vec3(0, 0.7, 0)));
                anchor.AddVisualBox(new Vec3(0.15, 1, 0.8), slate, new Frame(new Vec3(-4, 1, 0)));
                anchor.AddVisualBox(new Vec3(0.15, 1, 0.8), slate, new Frame(new Vec3(4, 1, 0)));

                const double k = 80, c = 0.05, rest = 2;
                double[] start = [-2 + 0.6, 0, 2];        // first cart displaced by 0.6 m
                var carts = new List<ChBody>();
                for (int i = 0; i < 3; i++)
                {
                    var cart = sys.Add(ChBody.CreateBox(new(0.5, 0.5, 0.5), 1600, collide: false).At(start[i], 1, 0).Colored(Build.Palette[i]).Named($"cart{i}"));
                    sys.Add(ChJoint.Prismatic(cart, anchor, cart.Position, Vec3.UnitX));
                    carts.Add(cart);
                }
                var scene = new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 1, 0), CameraDistance = 9, CameraYawDeg = 0, CameraPitchDeg = 15 };
                void Spring(ChBody a, Vec3 pa, ChBody b, Vec3 pb)
                {
                    sys.Add(new ChSpring(a, b, pa, pb, k, c, rest));
                    scene.Tether(() => a.Frame.TransformPoint(a.IsFixed ? pa : Vec3.Zero), () => b.Frame.TransformPoint(Vec3.Zero));
                }
                Spring(anchor, new Vec3(-4, 1, 0), carts[0], carts[0].Position);
                Spring(carts[0], carts[0].Position, carts[1], carts[1].Position);
                Spring(carts[1], carts[1].Position, carts[2], carts[2].Position);
                sys.Add(new ChSpring(carts[2], anchor, carts[2].Position, new Vec3(4, 1, 0), k, c, rest));
                scene.Tether(() => carts[2].Position, () => new Vec3(4, 1, 0));

                return scene
                    .Probe("Cart 1", "m", () => carts[0].Position.X + 2)
                    .Probe("Cart 2", "m", () => carts[1].Position.X)
                    .Probe("Cart 3", "m", () => carts[2].Position.X - 2);
            }
            """);
    }

    private static IEnumerable<ProjectTemplate> Military()
    {
        yield return new("artillery-range", "Artillery Fire Control", "Military",
            "A fire-control law computes the gun elevation for a 40 m target and fires three salvos at a stack of crates. Change the range or muzzle speed and the solution follows.",
            ["ballistics", "fire control", "impact"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.MaxPenetrationRecoverySpeed = 0.1;
                var mat = sys.Own(new ContactMaterial { Friction = 0.6f, Restitution = 0.1f });
                Build.Ground(sys, mat, 120);

                const double range = 40, muzzle = 24, g = 9.81;
                double elevation = 0.5 * Math.Asin(Math.Min(1, g * range / (muzzle * muzzle)));   // low-angle solution
                var gun = Build.Anchor(sys);
                gun.AddVisualBox(new Vec3(1.6, 0.6, 1.2), Color.FromHex("#3F4A3C"), new Frame(new Vec3(-0.6, 0.3, 0)));
                gun.AddVisualCylinder(0.12, 2.4, Color.FromHex("#566152"),
                    new Frame(new Vec3(0.4, 0.8, 0), Quat.FromAngleZ(elevation) * Quat.FromAngleY(Math.PI / 2)));
                var muzzlePoint = new Vec3(0.4 + 1.2 * Math.Cos(elevation), 0.8 + 1.2 * Math.Sin(elevation), 0);

                // Target: a stack of crates just in front of the impact point.
                var crates = new List<(ChBody Body, Vec3 Start)>();
                for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                {
                    var p = new Vec3(range - 0.6, 0.41 + row * 0.82, -1.2 + col * 0.82 + (row % 2) * 0.2);
                    var crate = sys.Add(ChBody.CreateBox(new(0.8, 0.8, 0.8), 250, mat).At(p).Colored(Color.FromHex("#A16207")));
                    crates.Add((crate, p));
                }

                // Shells wait (fixed, not colliding) and are released at the fire times.
                double[] fireAt = [0.5, 3.5, 6.5];
                var shells = new List<ChBody>();
                for (int i = 0; i < fireAt.Length; i++)
                {
                    var s = sys.Add(ChBody.CreateSphere(0.12, 7800, mat).At(-2, 0.2, -1 + i).Fixed().Colored(Color.FromHex("#9CA3AF")).Named($"shell{i}"));
                    s.CollisionEnabled = false;
                    shells.Add(s);
                }
                int fired = 0;
                sys.BeforeStep += (s, _) =>
                {
                    if (fired < shells.Count && s.Time >= fireAt[fired])
                    {
                        var shell = shells[fired++];
                        shell.Position = muzzlePoint + new Vec3(0, 0, (fired - 2) * 0.6);
                        shell.IsFixed = false;
                        shell.CollisionEnabled = true;
                        shell.LinearVelocity = new Vec3(muzzle * Math.Cos(elevation), muzzle * Math.Sin(elevation), 0);
                    }
                };

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(20, 4, 0), CameraDistance = 38, CameraYawDeg = 10, CameraPitchDeg = 14 }
                    .Probe("Elevation", "deg", () => elevation * 180 / Math.PI)
                    .Probe("Shells fired", "", () => fired)
                    .Probe("Crates displaced", "", () => crates.Count(c => (c.Body.Position - c.Start).Length > 0.3));
            }
            """);

        yield return new("obstacle-course", "Tactical Obstacle Course", "Military",
            "An HMMWV tackles a ramp, a step and a trench crossing at speed. Watch chassis pitch and engine load through each obstacle.",
            ["Chrono::Vehicle", "terrain patches", "mobility"], AppKind.Desktop, """
            public static Scene Create()
            {
                var car = Vehicle.Create(VehicleModel.Hmmwv, new Vec3(-25, 0, 0.6));
                var terrain = new RigidTerrain(car.System)
                    .AddPatch(Frame.Identity, 200, 40)
                    .AddPatch(new Frame(new Vec3(0, 0, -0.3), Quat.FromAngleY(-Build.Rad(12))), 10, 8)      // ramp
                    .AddPatch(new Frame(new Vec3(20, 0, -0.8)), 6, 10)                                       // step up
                    .AddPatch(new Frame(new Vec3(38, -4, -0.45), Quat.FromAngleX(Build.Rad(8))), 8, 6)       // side slope
                    .Initialize();
                car.System.SetSolver(SolverType.BarzilaiBorwein, 60).SetTimestepper(TimestepperType.EulerImplicitLinearized);

                var scene = new Scene(car.System)
                {
                    TimeStep = 0.002, UpIsZ = true, Follow = car.Chassis,
                    CameraTarget = car.Chassis.Position, CameraDistance = 11, CameraYawDeg = -140, CameraPitchDeg = 16,
                    StepOverride = dt => car.Step(terrain, new DriverInputs(0, car.System.Time < 1 ? 0 : 0.65, 0), dt),
                };
                scene.Owned.Add(terrain);
                scene.Owned.Add(car);
                return scene
                    .Probe("Speed", "km/h", () => car.Telemetry.SpeedKmh)
                    .Probe("Chassis pitch", "deg", () => car.Chassis.Rotation.ToEuler().Y * 180 / Math.PI)
                    .Probe("Engine", "rpm", () => car.Telemetry.EngineRpm);
            }
            """) { RequiresVehicle = true };

        yield return new("blast-barrier", "Blast Load on a Barrier", "Military",
            "A sandbag barrier takes a short radial blast impulse. Compare how many bags are displaced as you change charge size, distance or wall thickness.",
            ["impulse", "stacking", "custom forces"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.MaxPenetrationRecoverySpeed = 0.05;
                var mat = sys.Own(new ContactMaterial { Friction = 0.8f, Restitution = 0.05f });
                Build.Ground(sys, mat, 40);

                var bags = new List<(ChBody Body, Vec3 Start)>();
                const double bx = 0.6, by = 0.25, bz = 0.5;
                for (int row = 0; row < 6; row++)
                for (int col = 0; col < 7; col++)
                {
                    double x = (col - 3) * bx + (row % 2) * bx / 2;
                    var p = new Vec3(x, by / 2 + 0.003 + row * (by + 0.003), 0);
                    var bag = sys.Add(ChBody.CreateBox(new(bx * 0.97, by, bz), 1600, mat).At(p).Colored(Color.FromHex(row % 2 == 0 ? "#A8A29E" : "#78716C")));
                    bags.Add((bag, p));
                }

                var charge = new Vec3(0.4, 0.4, -3);
                const double peak = 9e4, duration = 0.02, at = 1.0;
                var marker = Build.Anchor(sys);
                marker.AddVisualSphere(0.15, Color.Red, new Frame(charge));
                sys.BeforeStep += (s, _) =>
                {
                    bool active = s.Time >= at && s.Time < at + duration;
                    foreach (var (b, _) in bags)
                    {
                        b.ClearForces();
                        if (!active) continue;
                        var d = b.Position - charge;
                        double r = Math.Max(0.5, d.Length);
                        b.AddForce(d.Normalized() * (peak / (r * r)));
                    }
                };

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 0.8, 0), CameraDistance = 9, CameraYawDeg = -35, CameraPitchDeg = 18 }
                    .Probe("Bags displaced", "", () => bags.Count(b => (b.Body.Position - b.Start).Length > 0.15))
                    .Probe("Fastest bag", "m/s", () => bags.Max(b => b.Body.LinearVelocity.Length))
                    .Probe("Contacts", "", () => sys.ContactCount);
            }
            """);
    }
}
