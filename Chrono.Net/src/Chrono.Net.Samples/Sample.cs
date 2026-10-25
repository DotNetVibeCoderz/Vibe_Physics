using System.Reflection;
using Chrono.Net.Rendering;

namespace Chrono.Net.Samples;

/// <summary>A named scalar signal sampled every frame (for plots and CSV export).</summary>
public sealed record Probe(string Name, string Unit, Func<double> Read);

/// <summary>Initial camera placement for a sample.</summary>
public sealed record CameraHint(Vec3 Target, double Distance, double YawDeg = 40, double PitchDeg = 25)
{
    public void Apply(OrbitCamera cam)
    {
        cam.Target = Target.ToVector3();
        cam.Distance = (float)Distance;
        cam.Yaw = (float)(YawDeg * Math.PI / 180);
        cam.Pitch = (float)(PitchDeg * Math.PI / 180);
    }
}

/// <summary>A live instance of a sample: its system, how to step it, what to plot and where to look.</summary>
public class SampleRun : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    public SampleRun(ChSystem system) => System = system;

    public ChSystem System { get; }
    public List<Probe> Probes { get; } = [];
    public CameraHint Camera { get; set; } = new(Vec3.Zero, 6);
    /// <summary>Body the camera should follow (e.g. a vehicle chassis).</summary>
    public ChBody? Follow { get; set; }
    public bool ShowContacts { get; set; }

    /// <summary>Custom per-step logic (controllers). Default: <c>System.Step(dt)</c>.</summary>
    public Action<double>? StepOverride { get; set; }

    /// <summary>Optional hook to add extra render geometry (terrain meshes...).</summary>
    public Action<RenderWorld>? DecorateWorld { get; set; }
    public Action<RenderWorld>? UpdateWorld { get; set; }

    public void Step(double dt)
    {
        if (StepOverride is { } s) s(dt);
        else System.Step(dt);
    }

    public T Own<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    public SampleRun Probe(string name, string unit, Func<double> read)
    {
        Probes.Add(new Probe(name, unit, read));
        return this;
    }

    public virtual void Dispose()
    {
        System.Dispose();
        foreach (var d in _owned) d.Dispose();
    }
}

/// <summary>Base class of a gallery sample. Subclasses build a <see cref="SampleRun"/>.</summary>
public abstract class Sample
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Category { get; }
    /// <summary>English description.</summary>
    public abstract string Description { get; }
    /// <summary>Deskripsi dalam Bahasa Indonesia.</summary>
    public abstract string DescriptionId { get; }
    public virtual bool UpIsZ => false;
    public virtual double TimeStep => 0.002;
    public virtual double Duration => 10;
    /// <summary>Features of Chrono demonstrated (tags).</summary>
    public virtual string[] Tags => [];
    public virtual bool RequiresVehicle => false;

    public abstract SampleRun Create();

    /// <summary>The C# source of this sample, embedded at build time.</summary>
    public string SourceCode
    {
        get
        {
            var asm = typeof(Sample).Assembly;
            string? Read(string name)
            {
                using var s = asm.GetManifestResourceStream(name);
                if (s is null) return null;
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
            // One file per sample normally; files holding several samples are found by class declaration.
            return Read("sample:" + GetType().Name)
                ?? asm.GetManifestResourceNames().Where(n => n.StartsWith("sample:")).Select(Read)
                    .FirstOrDefault(src => src!.Contains($"class {GetType().Name} "))
                ?? "// source not embedded";
        }
    }
}

/// <summary>Registry of all built-in samples.</summary>
public static class SampleCatalog
{
    private static readonly Lazy<Sample[]> _all = new(() =>
        typeof(Sample).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Sample)) && !t.IsAbstract)
            .Select(t => (Sample)Activator.CreateInstance(t)!)
            .OrderBy(s => CategoryOrder(s.Category)).ThenBy(s => s.Title)
            .ToArray());

    public static IReadOnlyList<Sample> All => _all.Value;

    public static IEnumerable<string> Categories => All.Select(s => s.Category).Distinct();

    public static Sample? Find(string id) => All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static int CategoryOrder(string c) => c switch
    {
        "Basics" => 0, "Mechanisms" => 1, "Contacts" => 2, "Robotics" => 3, "FEA" => 4, "Vehicles" => 5, _ => 9,
    };
}
