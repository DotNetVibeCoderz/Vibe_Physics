using Chrono.Net.Fea;

namespace Chrono.Net.Samples.Catalog;

public sealed class CantileverBeam : Sample
{
    public override string Id => "fea-cantilever";
    public override string Title => "Flexible Cantilever (FEA)";
    public override string Category => "FEA";
    public override string Description => "An aluminium Euler-beam cantilever with a rigid tip mass oscillates under gravity. Finite elements, implicit integration and node-to-body attachment.";
    public override string DescriptionId => "Batang kantilever aluminium (elemen Euler) dengan massa ujung kaku berosilasi karena gravitasi. Elemen hingga, integrasi implisit dan sambungan node-ke-benda.";
    public override string[] Tags => ["FEA", "Euler beam", "implicit integration", "flexible body"];
    public override double TimeStep => 0.002;

    public override SampleRun Create()
    {
        var sys = new ChSystem(ContactMethod.Smc);
        sys.SetSolver(SolverType.SparseQr);
        sys.SetTimestepper(TimestepperType.EulerImplicitLinearized);

        var wall = sys.Add(ChBody.CreateBox(new(0.2, 1.2, 0.8), 2000, collide: false).At(-0.1, 1.5, 0).Fixed().Colored(Color.Slate));
        var mesh = sys.Add(new FeaMesh());
        var nodes = mesh.BuildEulerBeam(new Vec3(0, 1.5, 0), new Vec3(2.0, 1.5, 0), 16, BeamSection.Aluminium(0.03, 0.06));
        mesh.FixNode(nodes.Start.Value);
        int tip = nodes.End.Value - 1;

        var tipMass = sys.Add(ChBody.CreateBox(new(0.15, 0.15, 0.15), 7800, collide: false).At(2.0, 1.5, 0).Colored(Color.Orange));
        mesh.AttachNode(tip, tipMass);

        return new SampleRun(sys) { Camera = new(new Vec3(1, 1.2, 0), 4, 20, 15) }
            .Probe("Tip deflection", "m", () => mesh.GetNodePosition(tip).Y - 1.5)
            .Probe("Tip velocity", "m/s", () => tipMass.LinearVelocity.Y);
    }
}
