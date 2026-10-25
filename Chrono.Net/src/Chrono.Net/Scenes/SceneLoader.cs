namespace Chrono.Net.Scenes;

/// <summary>A scene instantiated into a live <see cref="ChSystem"/>.</summary>
public sealed class LoadedScene : IDisposable
{
    internal LoadedScene(SceneDefinition definition, ChSystem system)
    {
        Definition = definition;
        System = system;
    }

    public SceneDefinition Definition { get; }
    public ChSystem System { get; }
    public Dictionary<string, ChBody> Bodies { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ChLink> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
    public double TimeStep => Definition.Settings.TimeStep;

    public void Dispose() => System.Dispose();
}

/// <summary>Builds a <see cref="ChSystem"/> from a <see cref="SceneDefinition"/>.</summary>
public static class SceneLoader
{
    public static LoadedScene LoadFile(string path) => Load(SceneDefinition.FromFile(path));

    public static LoadedScene Load(SceneDefinition def)
    {
        var s = def.Settings;
        var sys = new ChSystem(s.ContactMethod) { Gravity = V(s.Gravity) };
        if (s.Solver is { } solver) sys.SetSolver(solver, s.SolverIterations);
        if (s.Timestepper is { } stepper) sys.SetTimestepper(stepper);
        var scene = new LoadedScene(def, sys);

        var materials = new Dictionary<string, ContactMaterial>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, m) in def.Materials)
        {
            var mat = sys.Own(new ContactMaterial(s.ContactMethod)
            {
                Friction = m.Friction,
                Restitution = m.Restitution,
                RollingFriction = m.RollingFriction,
                SpinningFriction = m.SpinningFriction,
            });
            if (m.YoungModulus is { } y) mat.YoungModulus = y;
            if (m.PoissonRatio is { } p) mat.PoissonRatio = p;
            materials[name] = mat;
        }
        var defaultMat = sys.Own(new ContactMaterial(s.ContactMethod));

        foreach (var b in def.Bodies)
        {
            var mat = b.Material is not null && materials.TryGetValue(b.Material, out var m) ? m : defaultMat;
            int nx = 1, ny = 1, nz = 1;
            if (b.Array is { Length: 3 } a) (nx, ny, nz) = (a[0], a[1], a[2]);
            var spacing = b.ArraySpacing is { Length: 3 } sp ? V(sp) : Vec3.Zero;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                var body = CreateBody(b, mat);
                body.Position = V(b.Position) + new Vec3(i * spacing.X, j * spacing.Y, k * spacing.Z);
                body.Rotation = Quat.FromEulerDegrees(V(b.Rotation));
                if (b.Velocity is { } v) body.LinearVelocity = V(v);
                if (b.AngularVelocity is { } w) body.AngularVelocity = V(w);
                if (b.Mass is { } mass) body.Mass = mass;
                body.IsFixed = b.Fixed;
                body.SetColor(Color.FromHex(b.Color));
                string name = nx * ny * nz == 1 ? b.Name : $"{b.Name}[{i},{j},{k}]";
                body.Name = name;
                sys.Add(body);
                if (!string.IsNullOrEmpty(name)) scene.Bodies[name] = body;
            }
        }

        ChBody? ground = null;
        ChBody Resolve(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Equals("ground", StringComparison.OrdinalIgnoreCase))
            {
                if (scene.Bodies.TryGetValue("ground", out var g)) return g;
                return ground ??= sys.Add(new ChBody { Name = "__ground", IsFixed = true, CollisionEnabled = false });
            }
            return scene.Bodies.TryGetValue(name, out var body)
                ? body
                : throw new InvalidDataException($"Scene references unknown body '{name}'.");
        }

        foreach (var j in def.Joints)
        {
            var joint = new ChJoint(j.Type, Resolve(j.Body1), Resolve(j.Body2), Frame.AlongAxis(V(j.Position), V(j.Axis)));
            if (j.Limits is { Length: 2 } lim)
            {
                var axis = j.Type == JointKind.Prismatic ? JointAxis.Z : JointAxis.RotZ;
                joint.SetLimit(axis, lim[0], lim[1]);
            }
            if (!string.IsNullOrEmpty(j.Name)) { joint.Name = j.Name; scene.Links[j.Name] = joint; }
            sys.Add(joint);
        }

        foreach (var mdef in def.Motors)
        {
            var motor = new ChMotor(mdef.Type, Resolve(mdef.Body1), Resolve(mdef.Body2),
                Frame.AlongAxis(V(mdef.Position), V(mdef.Axis)), CreateFunction(mdef.Function));
            if (!string.IsNullOrEmpty(mdef.Name)) { motor.Name = mdef.Name; scene.Links[mdef.Name] = motor; }
            sys.Add(motor);
        }

        foreach (var sdef in def.Springs)
        {
            var spring = new ChSpring(Resolve(sdef.Body1), Resolve(sdef.Body2), V(sdef.Point1), V(sdef.Point2),
                sdef.Stiffness, sdef.Damping, sdef.RestLength);
            if (!string.IsNullOrEmpty(sdef.Name)) { spring.Name = sdef.Name; scene.Links[sdef.Name] = spring; }
            sys.Add(spring);
        }

        return scene;
    }

    public static ChFunction CreateFunction(FunctionDefinition f) => f.Type.ToLowerInvariant() switch
    {
        "ramp" => ChFunction.Ramp(f.Value, f.Slope),
        "sine" => ChFunction.Sine(f.Amplitude, f.Frequency, f.Phase, f.Value),
        "poly345" => ChFunction.Poly345(f.Value, f.Width),
        _ => ChFunction.Constant(f.Value),
    };

    private static ChBody CreateBody(BodyDefinition b, ContactMaterial mat)
    {
        double S(int i, double fallback) => b.Size.Length > i ? b.Size[i] : fallback;
        switch (b.Shape)
        {
            case BodyShape.Sphere:
                return ChBody.CreateSphere(S(0, 0.5), b.Density, mat, collide: b.Collide);
            case BodyShape.Cylinder:
                return ChBody.CreateCylinder(Enum.Parse<Axis>(b.Axis, true), S(0, 0.5), S(1, 1), b.Density, mat, collide: b.Collide);
            case BodyShape.Ellipsoid:
                return ChBody.CreateEllipsoid(new(S(0, 0.5), S(1, 0.5), S(2, 0.5)), b.Density, mat, collide: b.Collide);
            case BodyShape.Capsule:
            case BodyShape.Cone:
            {
                double r = S(0, 0.25), h = S(1, 1);
                var body = new ChBody();
                bool capsule = b.Shape == BodyShape.Capsule;
                double volume = capsule
                    ? Math.PI * r * r * h + 4.0 / 3.0 * Math.PI * r * r * r
                    : Math.PI * r * r * h / 3.0;
                double mass = b.Density * volume;
                body.Mass = mass;
                body.Inertia = capsule
                    ? new Vec3(mass * (3 * r * r + h * h) / 12, mass * (3 * r * r + h * h) / 12, mass * r * r / 2)
                    : new Vec3(mass * (3 * r * r / 20 + 3 * h * h / 80), mass * (3 * r * r / 20 + 3 * h * h / 80), 3 * mass * r * r / 10);
                var color = Color.FromHex(b.Color);
                if (capsule)
                {
                    body.AddVisualCapsule(r, h, color);
                    if (b.Collide) body.AddCollisionCapsule(r, h, material: mat);
                }
                else
                {
                    body.AddVisualCone(r, h, color);
                    if (b.Collide) body.AddCollisionCone(r, h, material: mat);
                }
                body.CollisionEnabled = b.Collide;
                return body;
            }
            default:
                return ChBody.CreateBox(new(S(0, 1), S(1, 1), S(2, 1)), b.Density, mat, collide: b.Collide);
        }
    }

    private static Vec3 V(double[] a) => a.Length >= 3 ? new(a[0], a[1], a[2]) : Vec3.Zero;
}
