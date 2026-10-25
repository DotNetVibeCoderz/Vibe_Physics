using System.Runtime.InteropServices;
using Chrono.Net.Interop;

namespace Chrono.Net.Vehicles;

/// <summary>Ready-made Chrono::Vehicle models.</summary>
public enum VehicleModel
{
    /// <summary>HMMWV off-road truck (AWD, double-wishbone suspension).</summary>
    Hmmwv = 0,
    Sedan = 1,
    /// <summary>John Deere Gator utility vehicle.</summary>
    Gator = 2,
    /// <summary>UAZ-452 van.</summary>
    UazBus = 3,
    CityBus = 4,
    /// <summary>M113 tracked armored personnel carrier.</summary>
    M113 = 100,
}

public enum TireModel { Rigid = 0, TMeasy = 1, Fiala = 2, Pac02 = 3 }

/// <summary>Driver commands: steering in [-1, 1] (positive = left), throttle and braking in [0, 1].</summary>
public readonly record struct DriverInputs(double Steering, double Throttle, double Braking);

[StructLayout(LayoutKind.Sequential)]
public struct VehicleTelemetry
{
    public double Time;
    /// <summary>Forward speed (m/s).</summary>
    public double Speed;
    public double EngineRpm;
    public double EngineTorque;
    public int Gear;
    private int _pad;
    public Vec3 ChassisPosition;
    public Quat ChassisRotation;
    public double Steering;
    public double Throttle;
    public double Braking;

    public readonly double SpeedKmh => Speed * 3.6;
}

/// <summary>
/// A complete vehicle (chassis, suspension, steering, powertrain, tires) with its own Z-up <see cref="ChSystem"/>.
/// </summary>
/// <example>
/// <code>
/// using var car = Vehicle.Create(VehicleModel.Sedan, new Vec3(0, 0, 0.5));
/// using var terrain = RigidTerrain.Flat(car.System, 200, 200);
/// while (car.System.Time &lt; 10) car.Step(terrain, new DriverInputs(0, 0.5, 0), 2e-3);
/// </code>
/// </example>
public sealed class Vehicle : IDisposable
{
    internal VehicleHandle Handle { get; }

    public VehicleModel Model { get; }
    public ChSystem System { get; }
    public ChBody Chassis { get; }
    public bool IsTracked { get; }

    private Vehicle(VehicleHandle handle, VehicleModel model)
    {
        Handle = handle;
        Model = model;
        System = new ChSystem(NativeMethods.chn_vehicle_get_system(handle));
        Chassis = new ChBody(NativeMethods.chn_vehicle_get_chassis(handle));
        IsTracked = NativeMethods.chn_vehicle_is_tracked(handle) != 0;
    }

    /// <param name="position">Initial chassis reference position (Z-up world).</param>
    /// <param name="yaw">Initial heading about Z (rad).</param>
    /// <param name="stepSize">Integration step that will be used (tires sub-step with it).</param>
    public static Vehicle Create(VehicleModel model, Vec3 position, double yaw = 0, TireModel tire = TireModel.TMeasy,
        ContactMethod contact = ContactMethod.Nsc, double stepSize = 2e-3)
    {
        ChronoEngine.EnsureAvailable();
        if (!ChronoEngine.VehicleSupported) throw new ChronoException("This native build does not include Chrono::Vehicle.");
        var h = NativeMethods.Check(NativeMethods.chn_vehicle_create((int)model, position, yaw, (int)contact, (int)tire, stepSize));
        return new Vehicle(h, model);
    }

    /// <summary>Synchronizes driver inputs and terrain, then advances vehicle and terrain by <paramref name="dt"/>.</summary>
    public void Step(Terrain terrain, DriverInputs inputs, double dt) =>
        NativeMethods.Check(NativeMethods.chn_vehicle_step(Handle, terrain.Handle, inputs.Steering, inputs.Throttle, inputs.Braking, dt));

    public VehicleTelemetry Telemetry
    {
        get
        {
            NativeMethods.Check(NativeMethods.chn_vehicle_get_telemetry(Handle, out var t));
            return t;
        }
    }

    public void Dispose()
    {
        Chassis.Dispose();
        System.Dispose();
        Handle.Dispose();
    }
}

/// <summary>Base class for vehicle terrains.</summary>
public abstract class Terrain : IDisposable
{
    internal TerrainHandle Handle { get; }

    private protected Terrain(TerrainHandle handle) => Handle = NativeMethods.Check(handle);

    /// <summary>Terrain height below <paramref name="location"/>.</summary>
    public double GetHeight(Vec3 location) => NativeMethods.chn_terrain_get_height(Handle, location);

    public void Dispose() => Handle.Dispose();
}

/// <summary>Rigid terrain made of box patches (flat ground, ramps, steps).</summary>
public sealed class RigidTerrain : Terrain
{
    private bool _initialized;

    public RigidTerrain(ChSystem system) : base(NativeMethods.chn_terrain_create_rigid(system.Handle)) => System = system;

    public ChSystem System { get; }

    /// <summary>Adds a box patch whose top surface is the XY plane of <paramref name="frame"/>.</summary>
    public RigidTerrain AddPatch(Frame frame, double length, double width, double thickness = 1, float friction = 0.9f, float restitution = 0.01f)
    {
        if (_initialized) throw new InvalidOperationException("Patches must be added before Initialize().");
        NativeMethods.Check(NativeMethods.chn_terrain_rigid_add_patch(Handle, frame, length, width, thickness, friction, restitution));
        return this;
    }

    public RigidTerrain Initialize()
    {
        if (_initialized) return this;
        NativeMethods.Check(NativeMethods.chn_terrain_initialize(Handle));
        _initialized = true;
        System.SyncBodiesFromNative();
        return this;
    }

    /// <summary>Flat ground of the given size centered at the origin.</summary>
    public static RigidTerrain Flat(ChSystem system, double length = 200, double width = 200, float friction = 0.9f) =>
        new RigidTerrain(system).AddPatch(Frame.Identity, length, width, 1, friction).Initialize();
}

/// <summary>Bekker-Wong / Janosi-Hanamoto soil parameters for <see cref="ScmTerrain"/>.</summary>
public readonly record struct SoilParameters(double Kphi, double Kc, double N, double Cohesion, double FrictionAngleDeg,
    double JanosiShear, double ElasticStiffness, double Damping)
{
    public static readonly SoilParameters ClayeySoil = new(0.2e6, 0, 1.1, 0, 30, 0.01, 4e7, 3e4);
    public static readonly SoilParameters DrySand = new(1.5e6, 0.9e3, 1.1, 1e3, 28, 0.015, 2e8, 3e4);
    public static readonly SoilParameters Snow = new(0.2e5, 4.4e3, 1.6, 1e3, 20, 0.04, 2e7, 2e4);
}

/// <summary>Soil Contact Model deformable terrain (ruts, sinkage, bulldozing).</summary>
public sealed unsafe class ScmTerrain : Terrain
{
    public ScmTerrain(ChSystem system, double sizeX, double sizeY, double resolution, SoilParameters soil)
        : base(NativeMethods.chn_terrain_create_scm(system.Handle, sizeX, sizeY, resolution, soil.Kphi, soil.Kc, soil.N,
            soil.Cohesion, soil.FrictionAngleDeg, soil.JanosiShear, soil.ElasticStiffness, soil.Damping))
    {
        system.SyncBodiesFromNative();
    }

    /// <summary>Current deformed surface mesh (absolute coordinates).</summary>
    public TriangleMesh GetSurfaceMesh()
    {
        int nv = 0, nt = 0;
        NativeMethods.Check(NativeMethods.chn_terrain_get_mesh(Handle, null, ref nv, null, ref nt));
        var verts = new Vec3[nv];
        var idx = new int[nt * 3];
        fixed (Vec3* v = verts)
        fixed (int* i = idx)
            NativeMethods.Check(NativeMethods.chn_terrain_get_mesh(Handle, v, ref nv, i, ref nt));
        return new TriangleMesh(verts, idx);
    }

    /// <summary>Updates vertex positions of an existing surface mesh in place (topology is constant).</summary>
    public void UpdateSurfaceMesh(TriangleMesh mesh)
    {
        int nv = mesh.Vertices.Length, nt = mesh.TriangleCount;
        fixed (Vec3* v = mesh.Vertices)
        fixed (int* i = mesh.Indices)
            NativeMethods.Check(NativeMethods.chn_terrain_get_mesh(Handle, v, ref nv, i, ref nt));
    }
}
