using Chrono.Net.Interop;

namespace Chrono.Net;

/// <summary>
/// Surface contact material. NSC materials use friction/restitution/compliance; SMC materials additionally use
/// Young's modulus, Poisson ratio and adhesion. Materials can be shared between many bodies.
/// </summary>
public sealed class ContactMaterial : IDisposable
{
    private float _friction = 0.6f, _restitution, _rolling, _spinning, _compliance, _cohesion;
    private float _young = 2e5f, _poisson = 0.3f, _adhesion;

    internal MaterialHandle Handle { get; }

    public ContactMaterial(ContactMethod method = ContactMethod.Nsc)
    {
        ChronoEngine.EnsureAvailable();
        Method = method;
        Handle = NativeMethods.Check(NativeMethods.chn_material_create((int)method));
        NativeMethods.chn_material_set_friction(Handle, _friction);
    }

    public ContactMethod Method { get; }

    /// <summary>Coulomb friction coefficient (static = sliding).</summary>
    public float Friction { get => _friction; set { _friction = value; NativeMethods.chn_material_set_friction(Handle, value); } }

    /// <summary>Coefficient of restitution, 0 (plastic) .. 1 (elastic).</summary>
    public float Restitution { get => _restitution; set { _restitution = value; NativeMethods.chn_material_set_restitution(Handle, value); } }

    /// <summary>Rolling friction coefficient (length units). Requires NSC with rolling contacts.</summary>
    public float RollingFriction { get => _rolling; set { _rolling = value; NativeMethods.chn_material_set_rolling_friction(Handle, value); } }

    /// <summary>Spinning (torsional) friction coefficient.</summary>
    public float SpinningFriction { get => _spinning; set { _spinning = value; NativeMethods.chn_material_set_spinning_friction(Handle, value); } }

    /// <summary>NSC normal compliance (inverse stiffness, m/N).</summary>
    public float Compliance { get => _compliance; set { _compliance = value; NativeMethods.chn_material_set_compliance(Handle, value); } }

    /// <summary>NSC cohesion force (N) — the force needed to separate touching bodies.</summary>
    public float Cohesion { get => _cohesion; set { _cohesion = value; NativeMethods.chn_material_set_cohesion(Handle, value); } }

    /// <summary>SMC Young's modulus (Pa).</summary>
    public float YoungModulus { get => _young; set { _young = value; NativeMethods.chn_material_set_young_modulus(Handle, value); } }

    /// <summary>SMC Poisson ratio.</summary>
    public float PoissonRatio { get => _poisson; set { _poisson = value; NativeMethods.chn_material_set_poisson_ratio(Handle, value); } }

    /// <summary>SMC constant adhesion force (N).</summary>
    public float Adhesion { get => _adhesion; set { _adhesion = value; NativeMethods.chn_material_set_adhesion(Handle, value); } }

    public void Dispose() => Handle.Dispose();
}
