using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chrono.Net.Interop;

/// <summary>Raw P/Invoke surface of <c>chrono_native</c>. Prefer the high-level API in <see cref="Chrono.Net"/>.</summary>
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Mirrors the C ABI")]
public static unsafe partial class NativeMethods
{
    public const string Lib = "chrono_native";

    static NativeMethods() => NativeLibraryLoader.EnsureRegistered();

    // ---------------------------------------------------------------- library
    [LibraryImport(Lib)] public static partial int chn_abi_version();
    [LibraryImport(Lib)] public static partial nint chn_chrono_version();
    [LibraryImport(Lib)] public static partial int chn_last_error(byte* buffer, int capacity);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial void chn_set_data_path(string path);
    [LibraryImport(Lib)] public static partial void chn_set_default_envelope(double envelope, double margin);

    // ---------------------------------------------------------------- system
    [LibraryImport(Lib)] public static partial SystemHandle chn_system_create(int contactMethod);
    [LibraryImport(Lib)] public static partial void chn_system_release(nint sys);
    [LibraryImport(Lib)] public static partial int chn_system_contact_method(SystemHandle sys);
    [LibraryImport(Lib)] public static partial void chn_system_set_gravity(SystemHandle sys, Vec3 g);
    [LibraryImport(Lib)] public static partial Vec3 chn_system_get_gravity(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_system_set_solver(SystemHandle sys, int type, int maxIterations, double tolerance);
    [LibraryImport(Lib)] public static partial int chn_system_set_timestepper(SystemHandle sys, int type);
    [LibraryImport(Lib)] public static partial int chn_system_set_collision_system(SystemHandle sys, int type);
    [LibraryImport(Lib)] public static partial void chn_system_set_num_threads(SystemHandle sys, int chrono, int collision, int eigen);
    [LibraryImport(Lib)] public static partial void chn_system_set_max_penetration_recovery_speed(SystemHandle sys, double v);
    [LibraryImport(Lib)] public static partial int chn_system_step(SystemHandle sys, double dt);
    [LibraryImport(Lib)] public static partial int chn_system_step_many(SystemHandle sys, double dt, int count);
    [LibraryImport(Lib)] public static partial int chn_system_do_static_linear(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_system_do_static_nonlinear(SystemHandle sys, int steps);
    [LibraryImport(Lib)] public static partial int chn_system_do_assembly(SystemHandle sys);
    [LibraryImport(Lib)] public static partial double chn_system_get_time(SystemHandle sys);
    [LibraryImport(Lib)] public static partial void chn_system_set_time(SystemHandle sys, double t);
    [LibraryImport(Lib)] public static partial double chn_system_get_step_timer(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_system_num_contacts(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_system_num_bodies(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_system_num_links(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_system_add_body(SystemHandle sys, BodyHandle body);
    [LibraryImport(Lib)] public static partial int chn_system_remove_body(SystemHandle sys, BodyHandle body);
    [LibraryImport(Lib)] public static partial int chn_system_add_link(SystemHandle sys, LinkHandle link);
    [LibraryImport(Lib)] public static partial int chn_system_remove_link(SystemHandle sys, LinkHandle link);
    [LibraryImport(Lib)] public static partial int chn_system_add_mesh(SystemHandle sys, MeshHandle mesh);
    [LibraryImport(Lib)] public static partial BodyHandle chn_system_get_body(SystemHandle sys, int index);
    [LibraryImport(Lib)] public static partial int chn_system_get_body_states(SystemHandle sys, BodyState* output, int capacity);
    [LibraryImport(Lib)] public static partial int chn_system_get_contacts(SystemHandle sys, ContactInfo* output, int capacity);
    [LibraryImport(Lib)] public static partial int chn_system_state_size(SystemHandle sys, out int npos, out int nvel);
    [LibraryImport(Lib)] public static partial int chn_system_state_get(SystemHandle sys, double* pos, int npos, double* vel, int nvel, out double time);
    [LibraryImport(Lib)] public static partial int chn_system_state_set(SystemHandle sys, double* pos, int npos, double* vel, int nvel, double time);

    // ---------------------------------------------------------------- materials
    [LibraryImport(Lib)] public static partial MaterialHandle chn_material_create(int contactMethod);
    [LibraryImport(Lib)] public static partial void chn_material_release(nint mat);
    [LibraryImport(Lib)] public static partial void chn_material_set_friction(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_restitution(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_rolling_friction(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_spinning_friction(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_compliance(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_cohesion(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_young_modulus(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_poisson_ratio(MaterialHandle mat, float v);
    [LibraryImport(Lib)] public static partial void chn_material_set_adhesion(MaterialHandle mat, float v);

    // ---------------------------------------------------------------- bodies
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create();
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create_box(Vec3 size, double density, int visual, int collide, MaterialHandle mat);
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create_sphere(double radius, double density, int visual, int collide, MaterialHandle mat);
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create_cylinder(int axis, double radius, double height, double density, int visual, int collide, MaterialHandle mat);
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create_ellipsoid(Vec3 semiAxes, double density, int visual, int collide, MaterialHandle mat);
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create_convex_hull(Vec3* points, int count, double density, int visual, int collide, MaterialHandle mat);
    [LibraryImport(Lib)] public static partial BodyHandle chn_body_create_mesh(Vec3* vertices, int nvert, int* indices, int ntri, double density, int computeMass, int visual, int collide, MaterialHandle mat, double sphereSwept);
    [LibraryImport(Lib)] public static partial void chn_body_release(nint body);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial void chn_body_set_name(BodyHandle body, string name);
    [LibraryImport(Lib)] public static partial int chn_body_get_name(BodyHandle body, byte* buffer, int capacity);
    [LibraryImport(Lib)] public static partial void chn_body_set_pos(BodyHandle body, Vec3 p);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_pos(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_set_rot(BodyHandle body, Quat q);
    [LibraryImport(Lib)] public static partial Quat chn_body_get_rot(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_set_lin_vel(BodyHandle body, Vec3 v);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_lin_vel(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_set_ang_vel(BodyHandle body, Vec3 w);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_ang_vel(BodyHandle body);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_lin_acc(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_get_state(BodyHandle body, out BodyState state);
    [LibraryImport(Lib)] public static partial void chn_body_set_mass(BodyHandle body, double mass);
    [LibraryImport(Lib)] public static partial double chn_body_get_mass(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_set_inertia_xx(BodyHandle body, Vec3 ixx);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_inertia_xx(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_set_inertia_xy(BodyHandle body, Vec3 ixy);
    [LibraryImport(Lib)] public static partial void chn_body_set_fixed(BodyHandle body, int fixedFlag);
    [LibraryImport(Lib)] public static partial int chn_body_is_fixed(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_enable_collision(BodyHandle body, int enable);
    [LibraryImport(Lib)] public static partial int chn_body_collision_enabled(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_set_sleeping_allowed(BodyHandle body, int allowed);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_contact_force(BodyHandle body);
    [LibraryImport(Lib)] public static partial Vec3 chn_body_get_contact_torque(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_clear_forces(BodyHandle body);
    [LibraryImport(Lib)] public static partial void chn_body_add_force(BodyHandle body, Vec3 force, Vec3 point, int local);
    [LibraryImport(Lib)] public static partial void chn_body_add_torque(BodyHandle body, Vec3 torque, int local);
    [LibraryImport(Lib)] public static partial int chn_body_add_collision_box(BodyHandle body, MaterialHandle mat, Vec3 size, Frame frame);
    [LibraryImport(Lib)] public static partial int chn_body_add_collision_sphere(BodyHandle body, MaterialHandle mat, double radius, Frame frame);
    [LibraryImport(Lib)] public static partial int chn_body_add_collision_cylinder(BodyHandle body, MaterialHandle mat, double radius, double height, Frame frame);
    [LibraryImport(Lib)] public static partial int chn_body_add_collision_capsule(BodyHandle body, MaterialHandle mat, double radius, double height, Frame frame);
    [LibraryImport(Lib)] public static partial int chn_body_add_collision_cone(BodyHandle body, MaterialHandle mat, double radius, double height, Frame frame);
    [LibraryImport(Lib)] public static partial int chn_body_add_collision_mesh(BodyHandle body, MaterialHandle mat, Vec3* vertices, int nvert, int* indices, int ntri, int isStatic, int isConvex, double sphereSwept, Frame frame);
    [LibraryImport(Lib)] public static partial int chn_body_add_visual_box(BodyHandle body, Vec3 size, Frame frame, float r, float g, float b);
    [LibraryImport(Lib)] public static partial int chn_body_add_visual_sphere(BodyHandle body, double radius, Frame frame, float r, float g, float b);
    [LibraryImport(Lib)] public static partial int chn_body_add_visual_cylinder(BodyHandle body, double radius, double height, Frame frame, float r, float g, float b);
    [LibraryImport(Lib)] public static partial int chn_body_add_visual_capsule(BodyHandle body, double radius, double height, Frame frame, float r, float g, float b);
    [LibraryImport(Lib)] public static partial int chn_body_add_visual_cone(BodyHandle body, double radius, double height, Frame frame, float r, float g, float b);
    [LibraryImport(Lib)] public static partial int chn_body_set_color(BodyHandle body, float r, float g, float b);
    [LibraryImport(Lib)] public static partial int chn_body_get_visual_shapes(BodyHandle body, VisualShape* output, int capacity);
    [LibraryImport(Lib)] public static partial int chn_body_get_visual_mesh(BodyHandle body, int shapeIndex, Vec3* vertices, ref int nvert, int* indices, ref int ntri);

    // ---------------------------------------------------------------- functions
    [LibraryImport(Lib)] public static partial FunctionHandle chn_function_const(double value);
    [LibraryImport(Lib)] public static partial FunctionHandle chn_function_ramp(double y0, double slope);
    [LibraryImport(Lib)] public static partial FunctionHandle chn_function_sine(double amplitude, double frequency, double phase, double shift);
    [LibraryImport(Lib)] public static partial FunctionHandle chn_function_poly345(double height, double width);
    [LibraryImport(Lib)] public static partial FunctionHandle chn_function_setpoint();
    [LibraryImport(Lib)] public static partial void chn_function_setpoint_set(FunctionHandle fn, double value);
    [LibraryImport(Lib)] public static partial FunctionHandle chn_function_callback(delegate* unmanaged[Cdecl]<double, nint, double> cb, nint user);
    [LibraryImport(Lib)] public static partial double chn_function_eval(FunctionHandle fn, double x);
    [LibraryImport(Lib)] public static partial void chn_function_release(nint fn);

    // ---------------------------------------------------------------- links
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_joint(int kind, BodyHandle body1, BodyHandle body2, Frame frame);
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_distance(BodyHandle body1, BodyHandle body2, Vec3 p1, Vec3 p2, int local);
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_gear(BodyHandle body1, BodyHandle body2, double ratio, int epicyclic, Frame shaft1, Frame shaft2);
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_rack_pinion(BodyHandle pinion, BodyHandle rack, double pinionRadius, Frame pinionFrame, Frame rackFrame);
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_motor(int kind, BodyHandle body1, BodyHandle body2, Frame frame, FunctionHandle fn);
    [LibraryImport(Lib)] public static partial int chn_motor_set_function(LinkHandle link, FunctionHandle fn);
    [LibraryImport(Lib)] public static partial int chn_motor_get(LinkHandle link, out double value, out double speed, out double effort);
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_spring(BodyHandle body1, BodyHandle body2, Vec3 p1, Vec3 p2, int local, double k, double c, double restLength, double actuatorForce);
    [LibraryImport(Lib)] public static partial int chn_spring_set_force_callback(LinkHandle link, delegate* unmanaged[Cdecl]<double, double, double, double, nint, double> cb, nint user);
    [LibraryImport(Lib)] public static partial int chn_spring_get(LinkHandle link, out double length, out double velocity, out double force);
    [LibraryImport(Lib)] public static partial LinkHandle chn_link_create_rot_spring(BodyHandle body1, BodyHandle body2, Frame frame, double k, double c, double restAngle, double actuatorTorque);
    [LibraryImport(Lib)] public static partial int chn_rot_spring_set_torque_callback(LinkHandle link, delegate* unmanaged[Cdecl]<double, double, double, double, nint, double> cb, nint user);
    [LibraryImport(Lib)] public static partial int chn_rot_spring_get(LinkHandle link, out double angle, out double velocity, out double torque);
    [LibraryImport(Lib)] public static partial int chn_link_set_limits(LinkHandle link, int axis, double min, double max);
    [LibraryImport(Lib)] public static partial void chn_link_release(nint link);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial void chn_link_set_name(LinkHandle link, string name);
    [LibraryImport(Lib)] public static partial int chn_link_get_frames(LinkHandle link, out Frame frame1, out Frame frame2);
    [LibraryImport(Lib)] public static partial int chn_link_get_reaction(LinkHandle link, out Vec3 force, out Vec3 torque);
    [LibraryImport(Lib)] public static partial void chn_link_set_disabled(LinkHandle link, int disabled);

    // ---------------------------------------------------------------- FEA
    [LibraryImport(Lib)] public static partial MeshHandle chn_mesh_create();
    [LibraryImport(Lib)] public static partial void chn_mesh_release(nint mesh);
    [LibraryImport(Lib)] public static partial void chn_mesh_set_automatic_gravity(MeshHandle mesh, int enable);
    [LibraryImport(Lib)] public static partial int chn_mesh_build_beam_euler(MeshHandle mesh, Vec3 a, Vec3 b, Vec3 ydir, int elements, double widthY, double widthZ, double young, double shear, double density, double rayleigh);
    [LibraryImport(Lib)] public static partial int chn_mesh_build_cable_ancf(MeshHandle mesh, Vec3 a, Vec3 b, int elements, double diameter, double young, double density, double rayleigh);
    [LibraryImport(Lib)] public static partial int chn_mesh_num_nodes(MeshHandle mesh);
    [LibraryImport(Lib)] public static partial int chn_mesh_num_elements(MeshHandle mesh);
    [LibraryImport(Lib)] public static partial int chn_mesh_get_node_positions(MeshHandle mesh, Vec3* output, int capacity);
    [LibraryImport(Lib)] public static partial int chn_mesh_get_line_elements(MeshHandle mesh, int* pairs, int capacityPairs);
    [LibraryImport(Lib)] public static partial int chn_mesh_node_set_fixed(MeshHandle mesh, int node, int fixedFlag);
    [LibraryImport(Lib)] public static partial int chn_mesh_node_set_force(MeshHandle mesh, int node, Vec3 force);
    [LibraryImport(Lib)] public static partial int chn_mesh_node_attach(SystemHandle sys, MeshHandle mesh, int node, BodyHandle body);

    // ---------------------------------------------------------------- vehicle
    [LibraryImport(Lib)] public static partial int chn_vehicle_supported();
    [LibraryImport(Lib)] public static partial VehicleHandle chn_vehicle_create(int model, Vec3 initPos, double initYaw, int contactMethod, int tireModel, double stepSize);
    [LibraryImport(Lib)] public static partial void chn_vehicle_release(nint vehicle);
    [LibraryImport(Lib)] public static partial SystemHandle chn_vehicle_get_system(VehicleHandle vehicle);
    [LibraryImport(Lib)] public static partial BodyHandle chn_vehicle_get_chassis(VehicleHandle vehicle);
    [LibraryImport(Lib)] public static partial int chn_vehicle_step(VehicleHandle vehicle, TerrainHandle terrain, double steering, double throttle, double braking, double dt);
    [LibraryImport(Lib)] public static partial int chn_vehicle_get_telemetry(VehicleHandle vehicle, out Vehicles.VehicleTelemetry telemetry);
    [LibraryImport(Lib)] public static partial int chn_vehicle_is_tracked(VehicleHandle vehicle);
    [LibraryImport(Lib)] public static partial TerrainHandle chn_terrain_create_rigid(SystemHandle sys);
    [LibraryImport(Lib)] public static partial int chn_terrain_rigid_add_patch(TerrainHandle terrain, Frame frame, double length, double width, double thickness, float friction, float restitution);
    [LibraryImport(Lib)] public static partial TerrainHandle chn_terrain_create_scm(SystemHandle sys, double sizeX, double sizeY, double resolution, double kphi, double kc, double n, double cohesion, double frictionDeg, double janosi, double elasticK, double dampingR);
    [LibraryImport(Lib)] public static partial int chn_terrain_initialize(TerrainHandle terrain);
    [LibraryImport(Lib)] public static partial double chn_terrain_get_height(TerrainHandle terrain, Vec3 location);
    [LibraryImport(Lib)] public static partial int chn_terrain_get_mesh(TerrainHandle terrain, Vec3* vertices, ref int nvert, int* indices, ref int ntri);
    [LibraryImport(Lib)] public static partial void chn_terrain_release(nint terrain);

    // ---------------------------------------------------------------- helpers

    /// <summary>Returns the last native error message for the calling thread.</summary>
    public static string LastError()
    {
        Span<byte> buffer = stackalloc byte[512];
        fixed (byte* p = buffer)
        {
            int len = chn_last_error(p, buffer.Length);
            return System.Text.Encoding.UTF8.GetString(buffer[..System.Math.Min(len, buffer.Length - 1)]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Check(int status, [CallerArgumentExpression(nameof(status))] string? call = null)
    {
        if (status < 0) throw new ChronoException($"{call}: {LastError()}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T Check<T>(T handle, [CallerArgumentExpression(nameof(handle))] string? call = null) where T : ChronoHandle
    {
        if (handle.IsInvalid) throw new ChronoException($"{call}: {LastError()}");
        return handle;
    }
}
