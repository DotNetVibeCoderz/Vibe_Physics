using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chrono.Net.Scenes;

/// <summary>
/// Declarative, JSON-serializable description of a multibody scene. Load it with <see cref="SceneLoader"/>.
/// Angles are in degrees, lengths in meters, densities in kg/m³.
/// </summary>
public sealed class SceneDefinition
{
    public string Name { get; set; } = "Untitled";
    public string? Description { get; set; }
    public SceneSettings Settings { get; set; } = new();
    public Dictionary<string, MaterialDefinition> Materials { get; set; } = [];
    public List<BodyDefinition> Bodies { get; set; } = [];
    public List<JointDefinition> Joints { get; set; } = [];
    public List<MotorDefinition> Motors { get; set; } = [];
    public List<SpringDefinition> Springs { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, SceneJsonContext.Default.SceneDefinition);

    public static SceneDefinition FromJson(string json) =>
        JsonSerializer.Deserialize(json, SceneJsonContext.Default.SceneDefinition)
        ?? throw new JsonException("Empty scene document.");

    public static SceneDefinition FromFile(string path) => FromJson(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, ToJson());
}

public sealed class SceneSettings
{
    public ContactMethod ContactMethod { get; set; } = ContactMethod.Nsc;
    public double[] Gravity { get; set; } = [0, -9.81, 0];
    public double TimeStep { get; set; } = 0.002;
    public double Duration { get; set; } = 10;
    public SolverType? Solver { get; set; }
    public int SolverIterations { get; set; }
    public TimestepperType? Timestepper { get; set; }
    /// <summary>"Y" (default) or "Z": which world axis renderers treat as up.</summary>
    public string UpAxis { get; set; } = "Y";
}

public sealed class MaterialDefinition
{
    public float Friction { get; set; } = 0.6f;
    public float Restitution { get; set; }
    public float RollingFriction { get; set; }
    public float SpinningFriction { get; set; }
    public float? YoungModulus { get; set; }
    public float? PoissonRatio { get; set; }
}

public enum BodyShape { Box, Sphere, Cylinder, Ellipsoid, Capsule, Cone, Compound }

public sealed class BodyDefinition
{
    public string Name { get; set; } = "";
    public BodyShape Shape { get; set; } = BodyShape.Box;
    /// <summary>Box: full lengths; Sphere: [r]; Cylinder/Capsule/Cone: [r, h]; Ellipsoid: semi-axes.</summary>
    public double[] Size { get; set; } = [1, 1, 1];
    /// <summary>Cylinder axis for <see cref="BodyShape.Cylinder"/>: "X", "Y" or "Z".</summary>
    public string Axis { get; set; } = "Y";
    public double[] Position { get; set; } = [0, 0, 0];
    /// <summary>Euler angles in degrees (X, Y, Z).</summary>
    public double[] Rotation { get; set; } = [0, 0, 0];
    public double[]? Velocity { get; set; }
    public double[]? AngularVelocity { get; set; }
    public double Density { get; set; } = 1000;
    /// <summary>Overrides the mass computed from density.</summary>
    public double? Mass { get; set; }
    public bool Fixed { get; set; }
    public bool Collide { get; set; } = true;
    public string? Material { get; set; }
    public string Color { get; set; } = "#8FA3B8";
    /// <summary>Replicates the body in a grid: [nx, ny, nz] with spacing <see cref="ArraySpacing"/>.</summary>
    public int[]? Array { get; set; }
    public double[]? ArraySpacing { get; set; }
}

public sealed class JointDefinition
{
    public string Name { get; set; } = "";
    public JointKind Type { get; set; } = JointKind.Revolute;
    public string Body1 { get; set; } = "";
    /// <summary>Second body; "ground" (or empty) connects to an implicit fixed ground body.</summary>
    public string Body2 { get; set; } = "ground";
    public double[] Position { get; set; } = [0, 0, 0];
    /// <summary>Joint axis (frame Z): rotation axis for revolute, sliding axis for prismatic.</summary>
    public double[] Axis { get; set; } = [0, 0, 1];
    public double[]? Limits { get; set; }
}

public sealed class MotorDefinition
{
    public string Name { get; set; } = "";
    public MotorKind Type { get; set; } = MotorKind.RotationSpeed;
    public string Body1 { get; set; } = "";
    public string Body2 { get; set; } = "ground";
    public double[] Position { get; set; } = [0, 0, 0];
    public double[] Axis { get; set; } = [0, 0, 1];
    public FunctionDefinition Function { get; set; } = new();
}

public sealed class FunctionDefinition
{
    /// <summary>"const", "ramp", "sine" or "poly345".</summary>
    public string Type { get; set; } = "const";
    public double Value { get; set; }
    public double Slope { get; set; }
    public double Amplitude { get; set; }
    public double Frequency { get; set; }
    public double Phase { get; set; }
    public double Width { get; set; } = 1;
}

public sealed class SpringDefinition
{
    public string Name { get; set; } = "";
    public string Body1 { get; set; } = "";
    public string Body2 { get; set; } = "ground";
    public double[] Point1 { get; set; } = [0, 0, 0];
    public double[] Point2 { get; set; } = [0, 0, 0];
    public double Stiffness { get; set; } = 100;
    public double Damping { get; set; } = 1;
    public double RestLength { get; set; } = -1;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(SceneDefinition))]
internal sealed partial class SceneJsonContext : JsonSerializerContext;
