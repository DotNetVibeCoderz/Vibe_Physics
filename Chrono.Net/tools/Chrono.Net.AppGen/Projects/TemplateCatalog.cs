namespace Chrono.Net.AppGen.Projects;

/// <summary>A project template: metadata plus the body of the generated <c>Simulation</c> class.</summary>
public sealed record ProjectTemplate(string Id, string Name, string Category, string Description, string[] Tags, AppKind Kind, string SimulationCode)
{
    public bool RequiresVehicle { get; init; }
    public bool IsBlank { get; init; }
    public (string File, string Content)[] ExtraFiles { get; init; } = [];
}

/// <summary>Every template ChronoAppGen can create. Scene code lives in <c>TemplateCatalog.*.cs</c> partial files.</summary>
public static partial class TemplateCatalog
{
    public static readonly string[] Categories =
        ["Simulator", "Science", "Military", "Entertainment", "Work", "Industry", "Education", "Disaster & Safety", "Tools"];

    private static List<ProjectTemplate>? _all;

    public static IReadOnlyList<ProjectTemplate> All => _all ??= [.. Blank(), .. Simulators(), .. Science(), .. Military(),
        .. Entertainment(), .. Work(), .. Industry(), .. Education(), .. Safety(), .. Tools()];

    public static IEnumerable<ProjectTemplate> BlankTemplates => All.Where(t => t.IsBlank);
    public static IEnumerable<ProjectTemplate> Gallery => All.Where(t => !t.IsBlank);

    public static ProjectTemplate? Find(string id) => All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ProjectTemplate> Blank()
    {
        yield return new("blank-desktop", "Blank desktop app", "Blank",
            "An empty Chrono.Net desktop app: a ground slab, one falling box, a 3D viewport and a live plot. Start here and describe what you want to Jack.",
            ["avalonia", "starter"], AppKind.Desktop, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var mat = sys.Own(new ContactMaterial { Friction = 0.5f, Restitution = 0.2f });
                Build.Ground(sys, mat, 20);
                var box = sys.Add(ChBody.CreateBox(new(1, 1, 1), 500, mat).At(0, 3, 0).Rotated(Quat.FromAngleZ(0.4)).Colored(Build.Palette[0]).Named("box"));
                return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 1, 0), CameraDistance = 9 }
                    .Probe("Box height", "m", () => box.Position.Y)
                    .Probe("Box speed", "m/s", () => box.LinearVelocity.Length);
            }
            """) { IsBlank = true };

        yield return new("blank-console", "Blank console app", "Blank",
            "An empty Chrono.Net console app: builds a scene, steps it and prints the probes. Good for batch runs, studies and servers.",
            ["console", "starter"], AppKind.Console, """
            public static Scene Create()
            {
                var sys = new ChSystem();
                var anchor = Build.Anchor(sys);
                var bob = sys.Add(ChBody.CreateSphere(0.1, 7800).At(1, 0, 0).Named("bob"));
                sys.Add(ChJoint.Revolute(bob, anchor, Vec3.Zero, Vec3.UnitZ));
                return new Scene(sys) { TimeStep = 0.001 }
                    .Probe("Angle", "deg", () => Math.Atan2(bob.Position.X, -bob.Position.Y) * 180 / Math.PI);
            }

            /// <summary>Console entry: <c>dotnet run -- --seconds 10</c>.</summary>
            public static int Run(string[] args)
            {
                using var scene = Create();
                return Runner.StepAndPrint(scene, Runner.Seconds(args, 5));
            }
            """) { IsBlank = true };
    }
}
