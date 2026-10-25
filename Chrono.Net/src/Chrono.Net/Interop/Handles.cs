using System.Runtime.InteropServices;

namespace Chrono.Net.Interop;

/// <summary>Base class for handles that own one native <c>std::shared_ptr</c> reference.</summary>
public abstract class ChronoHandle : SafeHandle
{
    protected ChronoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;
}

public sealed class SystemHandle : ChronoHandle
{
    protected override bool ReleaseHandle() { NativeMethods.chn_system_release(handle); return true; }
}

public sealed class BodyHandle : ChronoHandle
{
    protected override bool ReleaseHandle() { NativeMethods.chn_body_release(handle); return true; }
}

public sealed class LinkHandle : ChronoHandle
{
    protected override bool ReleaseHandle() { NativeMethods.chn_link_release(handle); return true; }
}

public sealed class MaterialHandle : ChronoHandle
{
    /// <summary>Invalid handle passed to native code to mean "no material" (use the default).</summary>
    public static readonly MaterialHandle Null = new();

    protected override bool ReleaseHandle() { NativeMethods.chn_material_release(handle); return true; }
}

public sealed class FunctionHandle : ChronoHandle
{
    public static readonly FunctionHandle Null = new();

    protected override bool ReleaseHandle() { NativeMethods.chn_function_release(handle); return true; }
}

public sealed class MeshHandle : ChronoHandle
{
    protected override bool ReleaseHandle() { NativeMethods.chn_mesh_release(handle); return true; }
}

public sealed class VehicleHandle : ChronoHandle
{
    protected override bool ReleaseHandle() { NativeMethods.chn_vehicle_release(handle); return true; }
}

public sealed class TerrainHandle : ChronoHandle
{
    protected override bool ReleaseHandle() { NativeMethods.chn_terrain_release(handle); return true; }
}
