using Chrono.Net.Rendering;
using Chrono.Net.Vehicles;

namespace Chrono.Net.Samples.Catalog;

/// <summary>Shared plumbing for vehicle samples (Z-up world, chassis-following camera, telemetry probes).</summary>
public abstract class VehicleSampleBase : Sample
{
    public override string Category => "Vehicles";
    public override bool UpIsZ => true;
    public override bool RequiresVehicle => true;
    public override double TimeStep => 0.002;
    public override double Duration => 15;

    protected static SampleRun Drive(Vehicle vehicle, Terrain terrain, Func<double, VehicleTelemetry, DriverInputs> driver)
    {
        // Settings recommended by Chrono's vehicle demos for NSC systems.
        if (vehicle.System.ContactMethod == ContactMethod.Nsc)
            vehicle.System.SetSolver(SolverType.BarzilaiBorwein, 60).SetTimestepper(TimestepperType.EulerImplicitLinearized);
        vehicle.System.SetThreads(Math.Min(4, Environment.ProcessorCount), Math.Min(4, Environment.ProcessorCount));
        var run = new SampleRun(vehicle.System)
        {
            Follow = vehicle.Chassis,
            Camera = new(vehicle.Chassis.Position, 9, -150, 18),
        };
        run.Own(terrain);
        var last = vehicle.Telemetry;
        run.StepOverride = dt =>
        {
            var inputs = driver(vehicle.System.Time, last);
            vehicle.Step(terrain, inputs, dt);
            last = vehicle.Telemetry;
        };
        run.Own(vehicle);
        run.Probe("Speed", "km/h", () => last.SpeedKmh)
           .Probe("Engine", "rpm", () => last.EngineRpm)
           .Probe("Gear", "", () => last.Gear);
        return run;
    }
}

public sealed class HmmwvRamp : VehicleSampleBase
{
    public override string Id => "vehicle-hmmwv";
    public override string Title => "HMMWV Off-Road Ramp";
    public override string Description => "Full HMMWV model (AWD, double wishbones, TMeasy tires) accelerates over a ramp and a step. Watch suspension travel and gear shifts.";
    public override string DescriptionId => "Model HMMWV lengkap (AWD, suspensi double wishbone, ban TMeasy) berakselerasi melewati tanjakan dan undakan. Perhatikan gerak suspensi dan perpindahan gigi.";
    public override string[] Tags => ["Chrono::Vehicle", "TMeasy tire", "powertrain", "rigid terrain"];

    public override SampleRun Create()
    {
        var car = Vehicle.Create(VehicleModel.Hmmwv, new Vec3(-20, 0, 0.6));
        var terrain = new RigidTerrain(car.System)
            .AddPatch(Frame.Identity, 200, 40)
            .AddPatch(new Frame(new Vec3(10, 0, -0.3), Quat.FromAngleY(-12 * Math.PI / 180)), 10, 8)
            .AddPatch(new Frame(new Vec3(30, 0, -0.85)), 6, 10)
            .Initialize();
        return Drive(car, terrain, (t, tel) => new DriverInputs(0, t < 1 ? 0 : 0.6, 0));
    }
}

public sealed class SedanLaneChange : VehicleSampleBase
{
    public override string Id => "vehicle-sedan";
    public override string Title => "Sedan Lane Change";
    public override string Description => "A passenger sedan performs a double lane change at highway speed with a simple steering controller written in C#.";
    public override string DescriptionId => "Sedan penumpang melakukan manuver pindah jalur ganda pada kecepatan jalan tol dengan pengendali kemudi sederhana yang ditulis di C#.";
    public override string[] Tags => ["Chrono::Vehicle", "driver controller", "handling"];

    public override SampleRun Create()
    {
        var car = Vehicle.Create(VehicleModel.Sedan, new Vec3(-60, 0, 0.5));
        var terrain = RigidTerrain.Flat(car.System, 400, 60);
        return Drive(car, terrain, (t, tel) =>
        {
            // Speed hold ~70 km/h, then a double lane change between x = 0 and x = 60.
            double throttle = Math.Clamp((70 - tel.SpeedKmh) * 0.08, 0, 1);
            double x = tel.ChassisPosition.X;
            double targetY = x is > 0 and < 30 ? 3.5 * Math.Sin(Math.PI * x / 30) : x is >= 30 and < 60 ? -3.5 * Math.Sin(Math.PI * (x - 30) / 30) : 0;
            double heading = tel.ChassisRotation.ToEuler().Z;
            double steer = Math.Clamp(0.25 * (targetY - tel.ChassisPosition.Y) - 1.2 * heading, -1, 1);
            return new DriverInputs(steer, throttle, 0);
        });
    }
}

public sealed class M113Tracked : VehicleSampleBase
{
    public override string Id => "vehicle-m113";
    public override string Title => "M113 Tracked Vehicle";
    public override string Description => "The M113 armored personnel carrier with single-pin track shoes, sprockets and road wheels — hundreds of contacting bodies.";
    public override string DescriptionId => "Kendaraan pengangkut personel lapis baja M113 dengan sepatu track single-pin, sprocket dan roda jalan — ratusan benda yang saling bersentuhan.";
    public override string[] Tags => ["tracked vehicle", "track shoes", "many contacts"];
    // Chrono's own M113 demo uses smooth (SMC) contact with a 0.5 ms step: far cheaper than NSC for hundreds of
    // track-shoe contacts.
    public override double TimeStep => 5e-4;

    public override SampleRun Create()
    {
        var tank = Vehicle.Create(VehicleModel.M113, new Vec3(-10, 0, 1.1), contact: ContactMethod.Smc, stepSize: TimeStep);
        var terrain = RigidTerrain.Flat(tank.System, 200, 40);
        return Drive(tank, terrain, (t, tel) => new DriverInputs(t > 4 ? 0.3 : 0, t < 0.5 ? 0 : 0.7, 0));
    }
}

public sealed class GatorSoftSoil : VehicleSampleBase
{
    public override string Id => "vehicle-scm";
    public override string Title => "Gator on Deformable Soil";
    public override string Description => "A utility vehicle drives across SCM deformable terrain, leaving ruts. Soil Contact Model with Bekker-Wong pressure-sinkage.";
    public override string DescriptionId => "Kendaraan utilitas melintasi medan deformable SCM dan meninggalkan jejak roda. Soil Contact Model dengan hubungan tekanan-ambles Bekker-Wong.";
    public override string[] Tags => ["SCM terrain", "terramechanics", "Chrono::Vehicle"];
    public override double Duration => 10;

    public override SampleRun Create()
    {
        var car = Vehicle.Create(VehicleModel.Gator, new Vec3(-8, 0, 0.5), tire: TireModel.Rigid);
        var soil = new ScmTerrain(car.System, 24, 6, 0.06, SoilParameters.ClayeySoil);
        var run = Drive(car, soil, (t, tel) => new DriverInputs(0, t < 0.5 ? 0 : 0.5, 0));

        TriangleMesh? surface = null;
        RenderItem? item = null;
        run.DecorateWorld = world =>
        {
            surface = soil.GetSurfaceMesh();
            item = new RenderItem { Mesh = RenderMesh.FromTriangleMesh(surface, smooth: true), Color = new(0.55f, 0.43f, 0.30f), IsGround = true, CastsShadow = false };
            world.ExtraItems.Add(item);
        };
        run.UpdateWorld = world =>
        {
            if (surface is null || item is null) return;
            soil.UpdateSurfaceMesh(surface);
            world.ExtraItems.Remove(item);
            item = new RenderItem { Mesh = RenderMesh.FromTriangleMesh(surface, smooth: true), Color = item.Color, IsGround = true, CastsShadow = false };
            world.ExtraItems.Add(item);
        };
        return run;
    }
}
