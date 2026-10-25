using Chrono.Net.Fea;
using Chrono.Net.Scenes;
using Xunit;

namespace Chrono.Net.Tests;

/// <summary>Tests that run the real Chrono engine; skipped when chrono_native is not built.</summary>
public class PhysicsTests
{
    private static void RequireNative() => Assert.SkipUnless(ChronoEngine.IsAvailable, "chrono_native not available");

    [Fact]
    public void ReportsChronoVersion()
    {
        RequireNative();
        Assert.StartsWith("10.", ChronoEngine.ChronoVersion);
    }

    [Fact]
    public void FreeFallMatchesAnalyticSolution()
    {
        RequireNative();
        using var sys = new ChSystem();
        var ball = sys.Add(ChBody.CreateSphere(0.1, 1000, collide: false).At(0, 10, 0));
        const double dt = 0.001, g = 9.81;
        const int n = 1000;
        sys.Step(dt, n);
        // Semi-implicit Euler (Chrono's default) is exact for y_n = y0 - g dt² n(n+1)/2 …
        Assert.Equal(10 - g * dt * dt * n * (n + 1) / 2, ball.Position.Y, 6);
        // … and within O(dt) of the continuous solution y(t) = y0 - g t²/2.
        Assert.InRange(ball.Position.Y, 10 - 0.5 * g - 0.01, 10 - 0.5 * g + 0.01);
        Assert.Equal(-g, ball.LinearVelocity.Y, 6);
    }

    [Fact]
    public void SmallAnglePendulumPeriod()
    {
        RequireNative();
        using var sys = new ChSystem();
        var ground = sys.Add(new ChBody().Fixed());
        const double L = 1.0;
        // Point-like bob: small sphere far from the pivot, massless rod via revolute joint at pivot.
        var bob = sys.Add(ChBody.CreateSphere(0.01, 1000, collide: false).At(L * Math.Sin(0.05), -L * Math.Cos(0.05), 0));
        sys.Add(ChJoint.Revolute(bob, ground, Vec3.Zero, Vec3.UnitZ));
        sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);

        // Detect two successive upward zero crossings of x.
        double dt = 1e-4, prevX = bob.Position.X, firstCross = -1, period = -1;
        for (int i = 0; i < 40000 && period < 0; i++)
        {
            sys.Step(dt);
            double x = bob.Position.X;
            if (prevX < 0 && x >= 0)
            {
                if (firstCross < 0) firstCross = sys.Time;
                else period = sys.Time - firstCross;
            }
            prevX = x;
        }
        double expected = 2 * Math.PI * Math.Sqrt(L / 9.81);
        Assert.InRange(period, expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void BoxRestsOnGround()
    {
        RequireNative();
        using var sys = new ChSystem();
        using var mat = new ContactMaterial();
        sys.Add(ChBody.CreateBox(new(10, 1, 10), 1000, mat).At(0, -0.5, 0).Fixed());
        var box = sys.Add(ChBody.CreateBox(new(1, 1, 1), 500, mat).At(0, 1.0, 0));
        sys.Step(0.002, 1500);
        Assert.InRange(box.Position.Y, 0.45, 0.55);
        Assert.True(box.LinearVelocity.Length < 0.05);
        Assert.True(sys.ContactCount > 0);
    }

    [Fact]
    public void SaveAndRestoreStateIsExact()
    {
        RequireNative();
        using var sys = new ChSystem();
        var b = sys.Add(ChBody.CreateSphere(0.2, 1000, collide: false).At(0, 5, 0).WithVelocity(new Vec3(1, 0, 0)));
        sys.Step(0.01, 10);
        var snapshot = sys.SaveState();
        var p1 = b.Position;
        sys.Step(0.01, 50);
        sys.RestoreState(snapshot);
        Assert.Equal(p1.X, b.Position.X, 9);
        Assert.Equal(p1.Y, b.Position.Y, 9);
        Assert.Equal(snapshot.Time, sys.Time, 9);
    }

    [Fact]
    public void SpeedMotorReachesCommandedSpeed()
    {
        RequireNative();
        using var sys = new ChSystem();
        var ground = sys.Add(new ChBody().Fixed());
        var wheel = sys.Add(ChBody.CreateCylinder(Axis.Z, 0.5, 0.1, 1000, collide: false));
        var motor = sys.Add(ChMotor.RotationSpeed(wheel, ground, Vec3.Zero, Vec3.UnitZ, 3.0));
        sys.Step(0.005, 200);
        Assert.Equal(3.0, motor.Speed, 3);
        Assert.Equal(3.0, wheel.AngularVelocity.Z, 3);
    }

    [Fact]
    public void DelegateFunctionDrivesMotor()
    {
        RequireNative();
        using var sys = new ChSystem();
        var ground = sys.Add(new ChBody().Fixed());
        var arm = sys.Add(ChBody.CreateBox(new(1, 0.1, 0.1), 500, collide: false));
        var law = ChFunction.FromDelegate(t => 0.5 * t);
        var motor = sys.Add(ChMotor.RotationAngle(arm, ground, Vec3.Zero, Vec3.UnitZ, law));
        sys.Step(0.001, 1000);
        Assert.Equal(0.5, motor.Value, 2);
    }

    [Fact]
    public void SpringOscillatesAtNaturalFrequency()
    {
        RequireNative();
        using var sys = new ChSystem { Gravity = Vec3.Zero };
        var ground = sys.Add(new ChBody().Fixed());
        var mass = sys.Add(ChBody.CreateSphere(0.1, 1000, collide: false).At(1.2, 0, 0));
        mass.Mass = 1;
        sys.Add(ChJoint.Prismatic(mass, ground, mass.Position, Vec3.UnitX));
        var spring = sys.Add(new ChSpring(mass, ground, mass.Position, Vec3.Zero, stiffness: 100, damping: 0, restLength: 1.0));
        double minLen = double.MaxValue;
        sys.Step(1e-3, (int)(Math.PI / 10 / 1e-3)); // half period of sqrt(k/m) = 10 rad/s
        minLen = spring.Length;
        Assert.InRange(minLen, 0.78, 0.82);
    }

    [Fact]
    public void CustomSpringForceLawIsInvoked()
    {
        RequireNative();
        using var sys = new ChSystem { Gravity = Vec3.Zero };
        var ground = sys.Add(new ChBody().Fixed());
        var b = sys.Add(ChBody.CreateSphere(0.1, 1000, collide: false).At(2, 0, 0));
        var spring = sys.Add(new ChSpring(b, ground, b.Position, Vec3.Zero, 0, 0, restLength: 1));
        int calls = 0;
        spring.ForceLaw = (t, rest, len, vel) => { calls++; return -50 * (len - rest); };
        sys.Step(0.001, 100);
        Assert.True(calls > 0);
        Assert.True(b.Position.X < 2);
    }

    [Fact]
    public void BulkStatesMatchPerBodyQueries()
    {
        RequireNative();
        using var sys = new ChSystem();
        for (int i = 0; i < 5; i++) sys.Add(ChBody.CreateSphere(0.1, 1000, collide: false).At(i, i, 0));
        sys.Step(0.01, 10);
        var states = sys.GetBodyStates();
        Assert.Equal(5, states.Length);
        for (int i = 0; i < 5; i++) Assert.Equal(sys.Bodies[i].Position, states[i].Position);
    }

    [Fact]
    public void CantileverBeamDeflectsUnderLoad()
    {
        RequireNative();
        using var sys = new ChSystem(ContactMethod.Smc) { Gravity = Vec3.Zero };
        sys.SetSolver(SolverType.SparseQr);
        var mesh = sys.Add(new FeaMesh());
        var nodes = mesh.BuildEulerBeam(Vec3.Zero, new Vec3(1, 0, 0), 10, BeamSection.Steel(0.02, 0.02));
        mesh.FixNode(nodes.Start.Value);
        int tip = nodes.End.Value - 1;
        mesh.SetNodeForce(tip, new Vec3(0, -100, 0));
        Assert.True(sys.DoStaticLinear());
        // δ = F L³ / (3 E I), I = b h³ / 12
        double I = 0.02 * Math.Pow(0.02, 3) / 12;
        double expected = 100.0 / (3 * 210e9 * I);
        Assert.Equal(-expected, mesh.GetNodePosition(tip).Y, 4);
    }

    [Fact]
    public void SceneLoadsFromJson()
    {
        RequireNative();
        const string json = """
        {
          "name": "test",
          "bodies": [
            { "name": "ground", "shape": "Box", "size": [10, 1, 10], "position": [0, -0.5, 0], "fixed": true },
            { "name": "arm", "shape": "Box", "size": [1, 0.1, 0.1], "position": [0.5, 2, 0], "collide": false }
          ],
          "joints": [ { "name": "hinge", "type": "Revolute", "body1": "arm", "body2": "ground", "position": [0, 2, 0], "axis": [0, 0, 1] } ]
        }
        """;
        using var scene = SceneLoader.Load(SceneDefinition.FromJson(json));
        Assert.Equal(2, scene.Bodies.Count);
        Assert.Single(scene.Links);
        scene.System.Step(0.002, 200);
        Assert.True(scene.Bodies["arm"].Position.Y < 2);
    }
}
