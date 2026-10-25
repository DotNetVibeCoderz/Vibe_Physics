namespace Chrono.Net.AppGen.Projects;

// Entertainment, work and industry templates.
public static partial class TemplateCatalog
{
    private static IEnumerable<ProjectTemplate> Entertainment()
    {
        yield return new("pinball-table", "Pinball Table", "Entertainment",
            "A tilted table with bumpers and two auto-firing flippers. Gravity is tilted instead of the table; bumpers are bouncy and count hits as score.",
            ["game physics", "angle motors", "restitution"], AppKind.Desktop, """
            public static Scene Create()
            {
                const double tilt = 7;
                var sys = new ChSystem { Gravity = new Vec3(0, -9.81 * Math.Cos(Build.Rad(tilt)), 9.81 * Math.Sin(Build.Rad(tilt))) };
                sys.SetSolver(SolverType.Psor, 60);
                var felt = sys.Own(new ContactMaterial { Friction = 0.2f, Restitution = 0.3f });
                var rubber = sys.Own(new ContactMaterial { Friction = 0.3f, Restitution = 0.9f });

                var table = sys.Add(ChBody.CreateBox(new(5, 0.2, 9), 1000, felt).At(0, -0.1, 0).Fixed().Colored(Color.FromHex("#1E3A5F")).Named("table"));
                var rail = Color.FromHex("#94A3B8");
                sys.Add(ChBody.CreateBox(new(0.2, 0.4, 9), 1000, felt).At(-2.6, 0.2, 0).Fixed().Colored(rail));
                sys.Add(ChBody.CreateBox(new(0.2, 0.4, 9), 1000, felt).At(2.6, 0.2, 0).Fixed().Colored(rail));
                sys.Add(ChBody.CreateBox(new(5.4, 0.4, 0.2), 1000, felt).At(0, 0.2, -4.6).Fixed().Colored(rail));
                // Slanted guides funnel the ball to the flippers.
                sys.Add(ChBody.CreateBox(new(1.6, 0.4, 0.15), 1000, felt).At(-1.8, 0.2, 3.2).Rotated(Quat.FromAngleY(-0.5)).Fixed().Colored(rail));
                sys.Add(ChBody.CreateBox(new(1.6, 0.4, 0.15), 1000, felt).At(1.8, 0.2, 3.2).Rotated(Quat.FromAngleY(0.5)).Fixed().Colored(rail));

                var bumpers = new List<ChBody>();
                foreach (var (x, z) in new[] { (-1.0, -2.2), (1.0, -2.2), (0.0, -3.2), (0.0, -1.0) })
                    bumpers.Add(sys.Add(ChBody.CreateCylinder(Axis.Y, 0.3, 0.4, 1000, rubber).At(x, 0.2, z).Fixed().Colored(Color.Red)));

                // Flippers pivot at their outer ends; angle motors swing them up-table (towards -Z) every 1.4 s.
                ChBody Flipper(double side)
                {
                    var pivot = new Vec3(side * 1.25, 0.15, 3.9);
                    var f = sys.Add(ChBody.CreateBox(new(1.0, 0.2, 0.18), 1500, rubber).At(pivot + new Vec3(-side * 0.5, 0, 0)).Colored(Color.Yellow));
                    sys.Add(ChMotor.RotationAngle(f, table, pivot, Vec3.UnitY,
                        ChFunction.FromDelegate(t => -side * 0.7 * Math.Pow(Math.Max(0, Math.Sin(2 * Math.PI * t / 1.4)), 6))));
                    return f;
                }
                Flipper(-1);
                Flipper(1);

                var ball = sys.Add(ChBody.CreateSphere(0.14, 7800, felt).At(0.4, 0.15, 2.5).WithVelocity(new Vec3(-0.6, 0, -11)).Colored(Color.White).Named("ball"));
                int score = 0;
                var cooldown = new double[bumpers.Count];
                sys.AfterStep += (s, _) =>
                {
                    for (int i = 0; i < bumpers.Count; i++)
                        if (s.Time > cooldown[i] && (ball.Position - bumpers[i].Position).Length < 0.3 + 0.14 + 0.03)
                        {
                            score += 100;
                            cooldown[i] = s.Time + 0.2;
                        }
                };

                return new Scene(sys) { TimeStep = 0.001, CameraTarget = new(0, 0, 0.5), CameraDistance = 9.5, CameraYawDeg = 0, CameraPitchDeg = 58 }
                    .Probe("Score", "", () => score)
                    .Probe("Ball speed", "m/s", () => ball.LinearVelocity.Length);
            }
            """);

        yield return new("bowling-alley", "Bowling Alley", "Entertainment",
            "A 7 kg ball with a little side spin rolls into ten pins. Count the pins down and try different lines, speeds and spins.",
            ["game physics", "collisions", "rolling"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.SetSolver(SolverType.Psor, 80);
                sys.MaxPenetrationRecoverySpeed = 0.1;
                var lane = sys.Own(new ContactMaterial { Friction = 0.12f, Restitution = 0.2f });
                var pinMat = sys.Own(new ContactMaterial { Friction = 0.4f, Restitution = 0.5f });

                sys.Add(ChBody.CreateBox(new(1.1, 0.2, 20), 1000, lane).At(0, -0.1, -8).Fixed().Colored(Color.FromHex("#B98B5A")).Named("lane"));
                sys.Add(ChBody.CreateBox(new(3, 0.2, 2), 1000, lane).At(0, -0.3, -18.5).Fixed().Colored(Color.FromHex("#1F2937")));

                const double pinR = 0.06, pinH = 0.38, spacing = 0.3048;
                var pins = new List<ChBody>();
                for (int row = 0; row < 4; row++)
                for (int i = 0; i <= row; i++)
                {
                    double x = (i - row / 2.0) * spacing;
                    double z = -16 - row * spacing * 0.866;
                    var pin = ChBody.CreateCylinder(Axis.Y, pinR, pinH, 340, pinMat).At(x, pinH / 2 + 0.002, z).Colored(Color.White);
                    pin.AddVisualCylinder(pinR * 1.02, 0.03, Color.Red, new Frame(new Vec3(0, pinH * 0.28, 0), Quat.FromAngleX(Math.PI / 2)));
                    pins.Add(sys.Add(pin));
                }

                var ball = sys.Add(ChBody.CreateSphere(0.109, 1290, lane).At(0.12, 0.111, 0).Colored(Color.FromHex("#1D4ED8")).Named("ball")
                    .WithVelocity(new Vec3(-0.08, 0, -8.5)));
                ball.AngularVelocity = new Vec3(0, 0, 6);   // side spin hooks the ball

                return new Scene(sys) { TimeStep = 0.0015, CameraTarget = new(0, 0.3, -14), CameraDistance = 7, CameraYawDeg = 12, CameraPitchDeg = 14 }
                    .Probe("Pins down", "", () => pins.Count(p => p.Rotation.AxisY.Y < 0.7 || p.Position.Y < 0.1))
                    .Probe("Ball speed", "m/s", () => ball.LinearVelocity.Length)
                    .Probe("Ball x", "m", () => ball.Position.X);
            }
            """);

        yield return new("marble-run", "Marble Run", "Entertainment",
            "Marbles race down a zig-zag of tilted ramps into a collecting tray. Rearrange the ramps to build your own track.",
            ["rolling", "ramps", "many bodies"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var mat = sys.Own(new ContactMaterial { Friction = 0.3f, Restitution = 0.25f, RollingFriction = 0.0005f });
                Build.Ground(sys, mat, 16);
                var wood = Color.FromHex("#C08A52");

                // Five ramps, alternating direction. Each ramp overhangs the drop gap of the one above it, and an end
                // stop on the low side turns the marbles down onto the next ramp.
                const double slope = 9, rise = 1.05;
                for (int i = 0; i < 5; i++)
                {
                    double dir = i % 2 == 0 ? 1 : -1;
                    double y = 5.5 - i * rise;
                    var q = Quat.FromAngleZ(-dir * Build.Rad(slope));
                    var c = new Vec3(-dir * 0.3, y, 0);
                    sys.Add(ChBody.CreateBox(new(4.6, 0.1, 0.6), 600, mat).At(c).Rotated(q).Fixed().Colored(wood));
                    sys.Add(ChBody.CreateBox(new(4.6, 0.3, 0.06), 600, mat).At(c + new Vec3(0, 0.15, -0.33)).Rotated(q).Fixed().Colored(wood));
                    sys.Add(ChBody.CreateBox(new(4.6, 0.3, 0.06), 600, mat).At(c + new Vec3(0, 0.15, 0.33)).Rotated(q).Fixed().Colored(wood));
                    sys.Add(ChBody.CreateBox(new(0.06, 0.95, 0.6), 600, mat).At(dir * 2.5, y - 0.075, 0).Fixed().Colored(wood));
                }
                sys.Add(ChBody.CreateBox(new(1.4, 0.2, 1.6), 600, mat).At(2.4, 0.1, 0).Fixed().Colored(Color.FromHex("#334155")));

                var marbles = new List<ChBody>();
                for (int i = 0; i < 24; i++)
                    marbles.Add(sys.Add(ChBody.CreateSphere(0.09, 2500, mat).At(-2.4 + (i % 6) * 0.22, 6.15 + (i / 6) * 0.25, (i % 2) * 0.1 - 0.05)
                        .Colored(Build.Palette[i % Build.Palette.Length])));

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 3, 0), CameraDistance = 11, CameraYawDeg = 10, CameraPitchDeg = 10 }
                    .Probe("Lowest marble", "m", () => marbles.Min(m => m.Position.Y))
                    .Probe("Fastest marble", "m/s", () => marbles.Max(m => m.LinearVelocity.Length))
                    .Probe("Finished", "", () => marbles.Count(m => m.Position.Y < 0.6));
            }
            """);
    }

    private static IEnumerable<ProjectTemplate> Work()
    {
        yield return new("forklift-pallet", "Forklift Pallet Handling", "Work",
            "A forklift mast lifts a loaded pallet to a rack shelf and lowers it again. Watch the load shift and the lift force through the cycle.",
            ["linear motor", "load handling", "friction"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.MaxPenetrationRecoverySpeed = 0.1;
                var mat = sys.Own(new ContactMaterial { Friction = 0.6f, Restitution = 0.05f });
                Build.Ground(sys, mat, 20);

                // Truck body and mast (fixed for this study).
                var truck = sys.Add(ChBody.CreateBox(new(2.2, 1.4, 1.2), 1000, mat).At(-1.6, 0.7, 0).Fixed().Colored(Color.FromHex("#F59E0B")).Named("truck"));
                truck.AddVisualBox(new Vec3(0.1, 3.6, 0.1), Color.FromHex("#334155"), new Frame(new Vec3(1.15, 1.1, -0.45)));
                truck.AddVisualBox(new Vec3(0.1, 3.6, 0.1), Color.FromHex("#334155"), new Frame(new Vec3(1.15, 1.1, 0.45)));

                // Carriage with two forks, driven by a position motor (lift 2.0 m, hold, lower).
                var carriage = sys.Add(ChBody.CreateBox(new(0.12, 0.6, 1.0), 2000, mat).At(-0.35, 0.35, 0).Colored(Color.FromHex("#475569")).Named("carriage"));
                carriage.AddCollisionBox(new Vec3(1.2, 0.05, 0.12), new Frame(new Vec3(0.6, -0.27, -0.3)), mat);
                carriage.AddCollisionBox(new Vec3(1.2, 0.05, 0.12), new Frame(new Vec3(0.6, -0.27, 0.3)), mat);
                carriage.AddVisualBox(new Vec3(1.2, 0.05, 0.12), Color.FromHex("#64748B"), new Frame(new Vec3(0.6, -0.27, -0.3)));
                carriage.AddVisualBox(new Vec3(1.2, 0.05, 0.12), Color.FromHex("#64748B"), new Frame(new Vec3(0.6, -0.27, 0.3)));
                static double S(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
                var lift = sys.Add(ChMotor.LinearPosition(carriage, truck, carriage.Position, Vec3.UnitY,
                    ChFunction.FromDelegate(t => 2.0 * (S((t - 1) / 3) - S((t - 7) / 3)))));

                // Pallet sitting on the forks, with four cartons on top.
                var pallet = sys.Add(ChBody.CreateBox(new(1.2, 0.12, 1.0), 500, mat).At(0.3, 0.17, 0).Colored(Color.FromHex("#A16207")).Named("pallet"));
                var cartons = new List<ChBody>();
                for (int i = 0; i < 4; i++)
                    cartons.Add(sys.Add(ChBody.CreateBox(new(0.5, 0.4, 0.45), 300, mat)
                        .At(0.05 + (i % 2) * 0.55, 0.44 + (i / 2) * 0.41, (i % 2 == 0 ? -0.24 : 0.24)).Colored(Color.FromHex("#D6B98C"))));

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 1.4, 0), CameraDistance = 7.5, CameraYawDeg = 40, CameraPitchDeg = 14 }
                    .Probe("Fork height", "m", () => carriage.Position.Y - 0.35)
                    .Probe("Lift force", "N", () => lift.Effort)
                    .Probe("Load shift", "m", () => cartons.Max(c => Math.Abs(c.Position.X - pallet.Position.X)));
            }
            """);
    }

    private static IEnumerable<ProjectTemplate> Industry()
    {
        yield return new("belt-conveyor", "Belt Conveyor Sorter", "Industry",
            "A belt conveyor carries cartons past a sensor; a pusher diverts the red ones onto a side chute. A small PLC-style control loop runs every step.",
            ["belt drive", "control loop", "setpoint"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                sys.SetSolver(SolverType.Psor, 50);
                var mat = sys.Own(new ContactMaterial { Friction = 0.7f, Restitution = 0.05f });
                Build.Ground(sys, mat, 24);
                var frame = Build.Anchor(sys);
                frame.AddVisualBox(new Vec3(7.4, 0.08, 0.08), Color.FromHex("#475569"), new Frame(new Vec3(1.2, 0.72, -0.62)));
                frame.AddVisualBox(new Vec3(7.4, 0.08, 0.08), Color.FromHex("#475569"), new Frame(new Vec3(1.2, 0.72, 0.62)));

                // Belt bed (top at y = 0.82) with 20 idler rollers that turn at belt speed for the look.
                // The belt itself is modelled as a friction-limited drive force (see BeforeStep), which is robust
                // and cheap compared with resolving every roller contact.
                const double r = 0.07, pitch = 0.36, belt = 0.8, beltTop = 0.82, mu = 0.7;
                var bed = sys.Own(new ContactMaterial { Friction = 0.05f, Restitution = 0 });
                sys.Add(ChBody.CreateBox(new(7.3, 0.04, 1.1), 1000, bed).At(1.2, beltTop - 0.02, 0).Fixed().Colored(Color.FromHex("#1F2937")));
                for (int i = 0; i < 20; i++)
                {
                    var roller = sys.Add(ChBody.CreateCylinder(Axis.Z, r, 1.1, 2000, collide: false).At(-2.2 + i * pitch, beltTop - r - 0.045, 0).Colored(Color.FromHex("#9CA3AF")));
                    sys.Add(ChMotor.RotationSpeed(roller, frame, roller.Position, -Vec3.UnitZ, belt / r));
                }
                // Side chute for diverted cartons and a bin at the end of the line.
                sys.Add(ChBody.CreateBox(new(1.2, 0.08, 1.6), 1000, mat).At(2.9, 0.45, 1.4).Rotated(Quat.FromAngleX(Build.Rad(18))).Fixed().Colored(Color.FromHex("#334155")));
                sys.Add(ChBody.CreateBox(new(1.4, 0.3, 1.6), 1000, mat).At(5.9, 0.15, 0).Fixed().Colored(Color.FromHex("#334155")));

                // Pusher on a prismatic position motor, commanded through a setpoint.
                var pusher = sys.Add(ChBody.CreateBox(new(0.6, 0.3, 0.1), 2000, mat).At(2.9, 1.0, -0.75).Colored(Color.Orange).Named("pusher"));
                var command = ChFunction.Setpoint();
                sys.Add(ChMotor.LinearPosition(pusher, frame, pusher.Position, Vec3.UnitZ, command));

                // Cartons enter every 1.6 s; every third one is red (to be diverted).
                var cartons = new List<(ChBody Body, bool Divert)>();
                for (int i = 0; i < 9; i++)
                {
                    bool red = i % 3 == 1;
                    var c = sys.Add(ChBody.CreateBox(new(0.4, 0.3, 0.4), 250, mat).At(-6, 0.15, -3 + i * 0.7).Fixed().Colored(red ? Color.Red : Color.FromHex("#D6B98C")));
                    c.CollisionEnabled = false;
                    cartons.Add((c, red));
                }
                int released = 0, diverted = 0;
                double pushUntil = -1, beltForce = 0;
                sys.BeforeStep += (s, dt) =>
                {
                    // Belt drive: pull every carton lying on the belt towards belt speed, limited by friction (μ·m·g).
                    beltForce = 0;
                    foreach (var (c, _) in cartons)
                    {
                        if (c.IsFixed) continue;
                        c.ClearForces();
                        var p = c.Position;
                        if (p.X < -2.4 || p.X > 4.85 || Math.Abs(p.Z) > 0.55 || p.Y > beltTop + 0.25 || p.Y < beltTop) continue;
                        double limit = mu * c.Mass * 9.81;
                        double f = Math.Clamp(c.Mass * (belt - c.LinearVelocity.X) / 0.05, -limit, limit);
                        c.AddForce(new Vec3(f, 0, 0));
                        beltForce += Math.Abs(f);
                    }

                    if (released < cartons.Count && s.Time >= 0.3 + released * 1.6)
                    {
                        var c = cartons[released++].Body;
                        c.Position = new Vec3(-2.0, beltTop + 0.155, 0);
                        c.IsFixed = false;
                        c.CollisionEnabled = true;
                    }
                    // Sensor at x = 2.9: fire the pusher when a red carton is in front of it.
                    if (s.Time > pushUntil && cartons.Any(c => c.Divert && !c.Body.IsFixed && Math.Abs(c.Body.Position.X - 2.9) < 0.12 && Math.Abs(c.Body.Position.Z) < 0.2))
                    {
                        pushUntil = s.Time + 0.9;
                        diverted++;
                    }
                    double target = s.Time < pushUntil - 0.4 ? 1.3 : 0;
                    // Rate-limit the setpoint so the motor never sees a jump.
                    command.Value += Math.Clamp(target - command.Value, -3 * dt, 3 * dt);
                };

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(1.5, 0.8, 0), CameraDistance = 9, CameraYawDeg = 35, CameraPitchDeg = 28 }
                    .Probe("Released", "", () => released)
                    .Probe("Diverted", "", () => diverted)
                    .Probe("Belt force", "N", () => beltForce);
            }
            """);

        yield return new("inline-four-engine", "Inline-4 Engine", "Industry",
            "A crankshaft with four throws (0°-180°-180°-0°) drives four connecting rods and pistons. Probe the piston motion and the drive torque.",
            ["slider-crank", "revolute joints", "rotation motor"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem { Gravity = Vec3.Zero };
                var anchor = Build.Anchor(sys);
                const double r = 0.25, L = 0.8, rpm = 180;
                anchor.AddVisualBox(new Vec3(1.0, 0.05, 4.2), Color.FromHex("#334155"), new Frame(new Vec3(0, -0.5, 0)));

                var crank = sys.Add(ChBody.CreateCylinder(Axis.Z, 0.08, 4.0, 7800, collide: false).Colored(Color.FromHex("#94A3B8")).Named("crankshaft"));
                double[] phase = [0, Math.PI, Math.PI, 0];
                var pistons = new List<ChBody>();
                for (int i = 0; i < 4; i++)
                {
                    double z = -1.5 + i;
                    var pin = new Vec3(r * Math.Sin(phase[i]), r * Math.Cos(phase[i]), z);
                    crank.AddVisualBox(new Vec3(0.14, r + 0.2, 0.08), Color.FromHex("#64748B"),
                        new Frame(new Vec3(pin.X / 2, pin.Y / 2, z - 0.12), Quat.FromAngleZ(-phase[i])));
                    crank.AddVisualCylinder(0.06, 0.3, Color.FromHex("#CBD5E1"), new Frame(pin));
                    var top = new Vec3(0, pin.Y + Math.Sqrt(L * L - pin.X * pin.X), z);
                    var rod = Build.Bar(sys, pin, top, 0.07, Color.Teal);
                    var piston = sys.Add(ChBody.CreateCylinder(Axis.Y, 0.18, 0.28, 2700, collide: false).At(top).Colored(Color.FromHex("#E2E8F0")));
                    anchor.AddVisualCylinder(0.2, 0.05, Color.FromHex("#475569"), new Frame(new Vec3(0, r + L + 0.25, z), Quat.FromAngleX(Math.PI / 2)));
                    sys.Add(ChJoint.Revolute(rod, crank, pin, Vec3.UnitZ));
                    sys.Add(ChJoint.Revolute(piston, rod, top, Vec3.UnitZ));
                    sys.Add(ChJoint.Prismatic(piston, anchor, top, Vec3.UnitY));
                    pistons.Add(piston);
                }
                var motor = sys.Add(ChMotor.RotationSpeed(crank, anchor, Vec3.Zero, Vec3.UnitZ, rpm * 2 * Math.PI / 60));

                return new Scene(sys) { TimeStep = 0.001, CameraTarget = new(0, 0.6, 0), CameraDistance = 6, CameraYawDeg = 55, CameraPitchDeg = 18 }
                    .Probe("Piston 1 height", "m", () => pistons[0].Position.Y)
                    .Probe("Piston 2 height", "m", () => pistons[1].Position.Y)
                    .Probe("Drive torque", "N·m", () => motor.Effort);
            }
            """);

        yield return new("pick-and-place", "Pick & Place Arm", "Industry",
            "A two-joint arm grips a part, swings it over a fixture and releases it. The grip is a lock link that is switched off at the drop point.",
            ["angle motors", "link enable/disable", "trajectory"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var mat = sys.Own(new ContactMaterial { Friction = 0.6f, Restitution = 0.05f });
                Build.Ground(sys, mat, 12);
                var anchor = Build.Anchor(sys);
                anchor.AddVisualCylinder(0.25, 1.0, Color.FromHex("#334155"), new Frame(new Vec3(0, 0.5, 0), Quat.FromAngleX(Math.PI / 2)));

                // Upper arm (shoulder at 1 m) and forearm; both swing about vertical axes (a SCARA layout).
                var shoulder = new Vec3(0, 1.05, 0);
                var upper = Build.Bar(sys, shoulder, shoulder + new Vec3(1.2, 0, 0), 0.14, Color.Orange);
                var elbow = shoulder + new Vec3(1.2, 0, 0);
                var fore = Build.Bar(sys, elbow, elbow + new Vec3(1.0, 0, 0), 0.12, Color.FromHex("#F59E0B"));
                var wrist = elbow + new Vec3(1.0, 0, 0);
                var gripper = sys.Add(ChBody.CreateBox(new(0.16, 0.5, 0.16), 800, collide: false).At(wrist - new Vec3(0, 0.25, 0)).Colored(Color.FromHex("#475569")));
                sys.Add(ChJoint.Lock(gripper, fore, wrist));

                static double S(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
                // Shoulder sweeps 100° and back; elbow folds 40° on the way.
                sys.Add(ChMotor.RotationAngle(upper, anchor, shoulder, Vec3.UnitY, ChFunction.FromDelegate(t => Build.Rad(100) * (S((t - 0.5) / 2.5) - S((t - 5) / 2.5)))));
                sys.Add(ChMotor.RotationAngle(fore, upper, elbow, Vec3.UnitY, ChFunction.FromDelegate(t => Build.Rad(-40) * (S((t - 0.5) / 2.5) - S((t - 5) / 2.5)))));

                // Part starts in the gripper; the grip link is released at t = 3.3 s over the fixture.
                var part = sys.Add(ChBody.CreateBox(new(0.3, 0.3, 0.3), 400, mat).At(wrist - new Vec3(0, 0.65, 0)).Colored(Color.Teal).Named("part"));
                var grip = sys.Add(ChJoint.Lock(part, gripper, part.Position + new Vec3(0, 0.15, 0)));
                sys.Add(ChBody.CreateBox(new(0.7, 0.15, 0.7), 1000, mat).At(0.29, 0.075, -2.05).Fixed().Colored(Color.FromHex("#1F2937")).Named("fixture"));
                sys.BeforeStep += (s, _) => { if (s.Time >= 3.3) grip.Disabled = true; };

                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0.3, 0.8, -0.6), CameraDistance = 6.5, CameraYawDeg = 35, CameraPitchDeg = 28 }
                    .Probe("Part height", "m", () => part.Position.Y)
                    .Probe("Part x", "m", () => part.Position.X)
                    .Probe("Part z", "m", () => part.Position.Z);
            }
            """);
    }
}
