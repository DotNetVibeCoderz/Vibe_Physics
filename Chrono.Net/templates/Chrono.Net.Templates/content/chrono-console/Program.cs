using Chrono.Net;
using Chrono.Net.Rendering;

// A pendulum hitting a stack of boxes. Project Chrono does the physics; Chrono.Net renders the result.
using var sys = new ChSystem();
var mat = sys.Own(new ContactMaterial { Friction = 0.5f });

sys.Add(ChBody.CreateBox(new(10, 1, 10), 1000, mat).At(0, -0.5, 0).Fixed().Colored(Color.Slate));
for (int i = 0; i < 6; i++)
    sys.Add(ChBody.CreateBox(new(0.4, 0.4, 0.4), 300, mat).At(1.0, 0.2 + i * 0.4, 0).Colored(Color.Orange));

var anchor = sys.Add(new ChBody { IsFixed = true, CollisionEnabled = false });
var ball = sys.Add(ChBody.CreateSphere(0.3, 7800, mat).At(-1.8, 3.0, 0).Colored(Color.Teal));
sys.Add(new ChDistanceConstraint(ball, anchor, ball.Position, new Vec3(0.6, 3.0, 0)));

Console.WriteLine($"Project Chrono {ChronoEngine.ChronoVersion} · {sys.Bodies.Count} bodies");
const double dt = 0.002;
while (sys.Time < 3)
{
    sys.Step(dt);
    if (Math.Abs(sys.Time % 0.5) < dt) Console.WriteLine($"t = {sys.Time:0.00} s   ball = {ball.Position}   contacts = {sys.ContactCount}");
}

var world = new RenderWorld(sys);
var camera = new OrbitCamera { Target = new(0.3f, 1.2f, 0), Distance = 7, Yaw = 0.5f, Pitch = 0.3f };
Snapshot.SavePng(world, camera, "result.png");
Console.WriteLine("Saved result.png");
