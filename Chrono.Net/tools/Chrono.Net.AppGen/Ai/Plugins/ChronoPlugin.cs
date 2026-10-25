using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Chrono.Net.AppGen.Projects;
using Chrono.Net.Samples;
using Microsoft.SemanticKernel;

namespace Chrono.Net.AppGen.Ai.Plugins;

/// <summary>Chrono.Net knowledge (guide, reflected API, tested samples) and project creation.</summary>
public sealed class ChronoPlugin(IIdeHost host, Func<string> projectsRoot, Func<GeneratorOptions> generatorOptions)
{
    private static readonly Assembly[] ApiAssemblies =
        [typeof(ChSystem).Assembly, typeof(Rendering.RenderWorld).Assembly, typeof(Chrono.Net.Avalonia.ChronoViewport).Assembly];

    private static Dictionary<string, string>? _docs;

    [KernelFunction, Description("Chrono.Net guide: conventions, the generated project structure and a complete annotated scene. Read once per task before writing code.")]
    public string GetChronoGuide() => JackPrompt.Default[JackPrompt.Default.IndexOf("Generated project structure", StringComparison.Ordinal)..] + """

        Complete scene example (Simulation.Create):
        ```csharp
        public static Scene Create()
        {
            var sys = new ChSystem();                                       // NSC contacts, gravity (0,-9.81,0)
            var mat = sys.Own(new ContactMaterial { Friction = 0.5f, Restitution = 0.1f });
            var ground = Build.Ground(sys, mat, 20);                        // top surface at y = 0
            var anchor = Build.Anchor(sys);                                 // fixed reference for joints

            var arm = Build.Bar(sys, new Vec3(0, 2, 0), new Vec3(1.5, 2, 0), 0.1, Color.Orange);
            var motor = sys.Add(ChMotor.RotationSpeed(arm, anchor, new Vec3(0, 2, 0), Vec3.UnitZ, Math.PI));

            var ball = sys.Add(ChBody.CreateSphere(0.2, 1000, mat).At(1, 4, 0).Colored(Color.Teal).Named("ball"));
            sys.BeforeStep += (s, dt) => { ball.ClearForces(); ball.AddForce(new Vec3(2, 0, 0)); };   // wind

            return new Scene(sys) { TimeStep = 0.002, CameraTarget = new(0, 1.5, 0), CameraDistance = 8 }
                .Probe("Ball height", "m", () => ball.Position.Y)
                .Probe("Motor torque", "N·m", () => motor.Effort);
        }
        ```
        Vehicles: `var car = Vehicle.Create(VehicleModel.Hmmwv, new Vec3(0, 0, 0.6));` then
        `var terrain = new RigidTerrain(car.System).AddPatch(Frame.Identity, 200, 40).Initialize();`, a Scene over
        `car.System` with `UpIsZ = true`, `Follow = car.Chassis`, `StepOverride = dt => car.Step(terrain, new DriverInputs(steer, throttle, brake), dt)`,
        and `scene.Owned.Add(terrain); scene.Owned.Add(car);`. Telemetry: car.Telemetry.SpeedKmh / EngineRpm / Gear.
        FEA: `var mesh = sys.Add(new Chrono.Net.Fea.FeaMesh()); var nodes = mesh.BuildCable(a, b, 20, CableSection.SteelWire(0.01)); mesh.FixNode(nodes.Start.Value);`
        """;

    [KernelFunction, Description("Chrono.Net API reference by reflection. Empty typeName lists all public types; otherwise returns constructors, properties, methods and events of that type (e.g. 'ChBody', 'ChMotor', 'ChronoViewport').")]
    public string GetApiReference([Description("Type name, e.g. ChBody; blank = list types")] string typeName = "")
    {
        var types = ApiAssemblies.SelectMany(a => a.GetExportedTypes())
            .Where(t => t.Namespace is { } ns && !ns.Contains("Interop") && !t.Name.StartsWith('!') && !ns.StartsWith("CompiledAvaloniaXaml"))
            .OrderBy(t => t.FullName).ToList();
        if (string.IsNullOrWhiteSpace(typeName))
            return string.Join("\n", types.GroupBy(t => t.Namespace).Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Name))}"));

        var type = types.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) || t.FullName!.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (type is null) return $"No public type '{typeName}'. Call GetApiReference with no argument for the list.";
        return Describe(type);
    }

    [KernelFunction, Description("Lists the built-in, tested Chrono.Net samples (id, category, title, description).")]
    public string ListSamples() =>
        string.Join("\n", SampleCatalog.All.Select(s => $"{s.Id} [{s.Category}] {s.Title}: {s.Description}"));

    [KernelFunction, Description("Returns the full C# source of a built-in sample (see ListSamples). Good reference for working scene code.")]
    public string GetSampleSource(string sampleId) =>
        SampleCatalog.Find(sampleId) is { } s ? s.SourceCode : $"Unknown sample '{sampleId}'. Call ListSamples.";

    [KernelFunction, Description("Lists the project templates (id, category, name, description).")]
    public string ListProjectTemplates() =>
        string.Join("\n", TemplateCatalog.All.Select(t => $"{t.Id} [{t.Category}, {t.Kind}] {t.Name}: {t.Description}"));

    [KernelFunction, Description("Creates a new project from a template in the projects folder and opens it in the IDE. Returns the project path.")]
    public async Task<string> CreateProjectFromTemplate([Description("Template id from ListProjectTemplates, e.g. blank-desktop")] string templateId,
        [Description("Project name (letters and digits)")] string projectName)
    {
        var t = TemplateCatalog.Find(templateId);
        if (t is null) return $"Unknown template '{templateId}'. Call ListProjectTemplates.";
        string name = ProjectGenerator.ToIdentifier(projectName);
        string root = projectsRoot();
        Directory.CreateDirectory(root);
        if (Directory.Exists(Path.Combine(root, name))) name += DateTime.Now.ToString("HHmmss");
        string csproj = ProjectGenerator.Create(t, root, name, generatorOptions());
        await host.OpenProjectAsync(Path.GetDirectoryName(csproj)!);
        host.Log(LogChannel.Jack, $"Created project {name} from template '{t.Name}'");
        return $"Created {csproj} from template '{t.Name}' and opened it. Files: {name}.csproj, Program.cs, Scene.cs, Build.cs, Simulation.cs, README.md.";
    }

    // ------------------------------------------------------------------ reflection

    private static string Describe(Type t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Kind(t)} {t.FullName}{(t.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && !t.IsEnum ? " : " + Name(b) : "")}");
        if (Doc("T:" + t.FullName) is { } td) sb.AppendLine("  // " + td);
        if (t.IsEnum) { sb.AppendLine("  values: " + string.Join(", ", Enum.GetNames(t))); return sb.ToString(); }
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var c in t.GetConstructors()) sb.AppendLine($"  new {t.Name}({Params(c.GetParameters())})");
        foreach (var p in t.GetProperties(F))
        {
            bool st = p.GetMethod?.IsStatic == true;
            string acc = p.CanWrite && p.SetMethod!.IsPublic ? " { get; set; }" : " { get; }";
            sb.AppendLine($"  {(st ? "static " : "")}{Name(p.PropertyType)} {p.Name}{acc}{Comment("P:" + t.FullName + "." + p.Name)}");
        }
        foreach (var f in t.GetFields(F).Where(f => !f.IsSpecialName))
            sb.AppendLine($"  {(f.IsStatic ? "static " : "")}{Name(f.FieldType)} {f.Name}{Comment("F:" + t.FullName + "." + f.Name)}");
        foreach (var m in t.GetMethods(F).Where(m => !m.IsSpecialName && m.Name is not ("Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "<Clone>$" or "PrintMembers")))
            sb.AppendLine($"  {(m.IsStatic ? "static " : "")}{Name(m.ReturnType)} {m.Name}({Params(m.GetParameters())}){Comment("M:" + t.FullName + "." + m.Name, prefix: true)}");
        foreach (var e in t.GetEvents(F)) sb.AppendLine($"  event {Name(e.EventHandlerType!)} {e.Name}");
        return sb.ToString();
    }

    private static string Kind(Type t) => t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "interface" : t.IsAbstract && t.IsSealed ? "static class" : "class";

    private static string Params(ParameterInfo[] ps) =>
        string.Join(", ", ps.Select(p => $"{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + (p.DefaultValue is string s ? $"\"{s}\"" : p.DefaultValue?.ToString()?.ToLowerInvariant() ?? "null") : "")}"));

    private static string Name(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } u) return Name(u) + "?";
        if (t.IsByRef) return Name(t.GetElementType()!);
        if (t.IsArray) return Name(t.GetElementType()!) + "[]";
        if (t.IsGenericType) return t.Name.Split('`')[0] + "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
        return t == typeof(void) ? "void" : t == typeof(double) ? "double" : t == typeof(float) ? "float" : t == typeof(int) ? "int"
            : t == typeof(bool) ? "bool" : t == typeof(string) ? "string" : t == typeof(object) ? "object" : t.Name;
    }

    private static string Comment(string key, bool prefix = false) => (prefix ? DocPrefix(key) : Doc(key)) is { } d ? "  // " + d : "";

    private static string? Doc(string key) => Docs().TryGetValue(key, out var v) ? v : null;

    private static string? DocPrefix(string key)
    {
        var docs = Docs();
        if (docs.TryGetValue(key, out var v)) return v;
        string p = key + "(";
        return docs.FirstOrDefault(kv => kv.Key.StartsWith(p, StringComparison.Ordinal)).Value;
    }

    /// <summary>XML documentation summaries of the API assemblies (when the .xml files are next to them).</summary>
    private static Dictionary<string, string> Docs()
    {
        if (_docs is not null) return _docs;
        var d = new Dictionary<string, string>();
        foreach (var a in ApiAssemblies)
        {
            string xml = Path.ChangeExtension(a.Location, ".xml");
            if (!File.Exists(xml)) continue;
            foreach (var m in XDocument.Load(xml).Descendants("member"))
            {
                var summary = m.Element("summary");
                if (summary is null || m.Attribute("name")?.Value is not { } name) continue;
                string text = string.Join(" ", summary.DescendantNodes().Select(n => n switch
                {
                    XText x => x.Value,
                    XElement { Name.LocalName: "see" or "paramref" or "typeparamref" } e =>
                        (e.Attribute("cref")?.Value ?? e.Attribute("name")?.Value ?? e.Attribute("langword")?.Value ?? "").Split('.', ':').Last(),
                    _ => "",
                }).Where(s => s.Length > 0));
                d[name] = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            }
        }
        return _docs = d;
    }
}
