using System.Runtime.InteropServices;
using Chrono.Net.Interop;

namespace Chrono.Net;

/// <summary>Base class of every constraint, motor and force element connecting two bodies.</summary>
public abstract class ChLink : IDisposable
{
    private string _name = string.Empty;

    internal LinkHandle Handle { get; }

    public ChBody Body1 { get; }
    public ChBody Body2 { get; }
    public ChSystem? System { get; internal set; }
    public object? Tag { get; set; }

    private protected ChLink(LinkHandle handle, ChBody body1, ChBody body2)
    {
        Handle = NativeMethods.Check(handle);
        Body1 = body1;
        Body2 = body2;
    }

    public string Name
    {
        get => _name;
        set { _name = value; NativeMethods.chn_link_set_name(Handle, value); }
    }

    /// <summary>Disabled links are ignored by the solver.</summary>
    public bool Disabled { set => NativeMethods.chn_link_set_disabled(Handle, value ? 1 : 0); }

    /// <summary>Link frames on body 1 and body 2, in absolute coordinates.</summary>
    public (Frame Frame1, Frame Frame2) Frames
    {
        get
        {
            NativeMethods.Check(NativeMethods.chn_link_get_frames(Handle, out var f1, out var f2));
            return (f1, f2);
        }
    }

    /// <summary>Reaction force and torque acting on body 2, in absolute coordinates.</summary>
    public (Vec3 Force, Vec3 Torque) Reaction
    {
        get
        {
            NativeMethods.Check(NativeMethods.chn_link_get_reaction(Handle, out var f, out var t));
            return (f, t);
        }
    }

    public virtual void Dispose()
    {
        Handle.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Kinematic joint. Revolute, prismatic and cylindrical joints act along the Z axis of the joint frame —
/// use <see cref="Frame.AlongAxis"/> to build one from a point and a direction.
/// </summary>
public sealed class ChJoint : ChLink
{
    public JointKind Kind { get; }

    public ChJoint(JointKind kind, ChBody body1, ChBody body2, Frame frame)
        : base(NativeMethods.chn_link_create_joint((int)kind, body1.Handle, body2.Handle, frame), body1, body2) => Kind = kind;

    public static ChJoint Revolute(ChBody b1, ChBody b2, Vec3 point, Vec3 axis) => new(JointKind.Revolute, b1, b2, Frame.AlongAxis(point, axis));
    public static ChJoint Prismatic(ChBody b1, ChBody b2, Vec3 point, Vec3 axis) => new(JointKind.Prismatic, b1, b2, Frame.AlongAxis(point, axis));
    public static ChJoint Cylindrical(ChBody b1, ChBody b2, Vec3 point, Vec3 axis) => new(JointKind.Cylindrical, b1, b2, Frame.AlongAxis(point, axis));
    public static ChJoint Spherical(ChBody b1, ChBody b2, Vec3 point) => new(JointKind.Spherical, b1, b2, new Frame(point));
    public static ChJoint Lock(ChBody b1, ChBody b2, Vec3 point) => new(JointKind.Lock, b1, b2, new Frame(point));
    /// <summary>Universal (Cardan) joint: the two rotation axes are the X and Y axes of <paramref name="frame"/>.</summary>
    public static ChJoint Universal(ChBody b1, ChBody b2, Frame frame) => new(JointKind.Universal, b1, b2, frame);
    /// <summary>Planar joint: body 1 slides on the XY plane of <paramref name="frame"/> and rotates about its Z.</summary>
    public static ChJoint Planar(ChBody b1, ChBody b2, Frame frame) => new(JointKind.Planar, b1, b2, frame);
    public static ChJoint PointOnPlane(ChBody b1, ChBody b2, Frame frame) => new(JointKind.PointPlane, b1, b2, frame);
    public static ChJoint PointOnLine(ChBody b1, ChBody b2, Frame frame) => new(JointKind.PointLine, b1, b2, frame);

    /// <summary>Activates a limit on one degree of freedom (lock-family joints).</summary>
    public ChJoint SetLimit(JointAxis axis, double min, double max)
    {
        NativeMethods.Check(NativeMethods.chn_link_set_limits(Handle, (int)axis, min, max));
        return this;
    }
}

/// <summary>Keeps the distance between two points (one on each body) constant.</summary>
public sealed class ChDistanceConstraint(ChBody body1, ChBody body2, Vec3 point1, Vec3 point2, bool local = false)
    : ChLink(NativeMethods.chn_link_create_distance(body1.Handle, body2.Handle, point1, point2, local ? 1 : 0), body1, body2);

/// <summary>
/// Gear pair between two bodies that rotate about the Z axes of the shaft frames (relative to each body).
/// <paramref name="ratio"/> = ω2/ω1 = radius1/radius2. Set <paramref name="internalGear"/> for ring (epicyclic) gears.
/// </summary>
public sealed class ChGear(ChBody body1, ChBody body2, double ratio, Frame shaft1, Frame shaft2, bool internalGear = false)
    : ChLink(NativeMethods.chn_link_create_gear(body1.Handle, body2.Handle, ratio, internalGear ? 1 : 0, shaft1, shaft2), body1, body2);

/// <summary>Rack-and-pinion coupling. Frames are relative to the pinion and rack bodies respectively.</summary>
public sealed class ChRackPinion(ChBody pinion, ChBody rack, double pinionRadius, Frame pinionFrame, Frame rackFrame)
    : ChLink(NativeMethods.chn_link_create_rack_pinion(pinion.Handle, rack.Handle, pinionRadius, pinionFrame, rackFrame), pinion, rack);

/// <summary>
/// Rotational or linear motor between two bodies, driven by a <see cref="ChFunction"/> of time
/// (angle/speed/torque for rotation about frame Z; position/speed/force for translation along frame Z).
/// </summary>
public sealed class ChMotor : ChLink
{
    private ChFunction? _function;

    public MotorKind Kind { get; }

    public ChMotor(MotorKind kind, ChBody body1, ChBody body2, Frame frame, ChFunction? function = null)
        : base(NativeMethods.chn_link_create_motor((int)kind, body1.Handle, body2.Handle, frame, function?.Handle ?? FunctionHandle.Null), body1, body2)
    {
        Kind = kind;
        _function = function;
    }

    public static ChMotor RotationSpeed(ChBody rotor, ChBody stator, Vec3 point, Vec3 axis, ChFunction speed) =>
        new(MotorKind.RotationSpeed, rotor, stator, Frame.AlongAxis(point, axis), speed);

    public static ChMotor RotationSpeed(ChBody rotor, ChBody stator, Vec3 point, Vec3 axis, double radPerSec) =>
        RotationSpeed(rotor, stator, point, axis, ChFunction.Constant(radPerSec));

    public static ChMotor RotationAngle(ChBody rotor, ChBody stator, Vec3 point, Vec3 axis, ChFunction angle) =>
        new(MotorKind.RotationAngle, rotor, stator, Frame.AlongAxis(point, axis), angle);

    public static ChMotor RotationTorque(ChBody rotor, ChBody stator, Vec3 point, Vec3 axis, ChFunction torque) =>
        new(MotorKind.RotationTorque, rotor, stator, Frame.AlongAxis(point, axis), torque);

    public static ChMotor LinearPosition(ChBody slider, ChBody guide, Vec3 point, Vec3 axis, ChFunction position) =>
        new(MotorKind.LinearPosition, slider, guide, Frame.AlongAxis(point, axis), position);

    public static ChMotor LinearSpeed(ChBody slider, ChBody guide, Vec3 point, Vec3 axis, ChFunction speed) =>
        new(MotorKind.LinearSpeed, slider, guide, Frame.AlongAxis(point, axis), speed);

    public static ChMotor LinearForce(ChBody slider, ChBody guide, Vec3 point, Vec3 axis, ChFunction force) =>
        new(MotorKind.LinearForce, slider, guide, Frame.AlongAxis(point, axis), force);

    public bool IsRotational => Kind <= MotorKind.RotationTorque;

    /// <summary>Driving law. Replacing it does not dispose the previous function.</summary>
    public ChFunction? Function
    {
        get => _function;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            NativeMethods.Check(NativeMethods.chn_motor_set_function(Handle, value.Handle));
            _function = value;
        }
    }

    /// <summary>Angle (rad) for rotational motors, position (m) for linear motors.</summary>
    public double Value { get { Read(out var v, out _, out _); return v; } }

    /// <summary>Angular speed (rad/s) or linear speed (m/s).</summary>
    public double Speed { get { Read(out _, out var s, out _); return s; } }

    /// <summary>Torque (N·m) or force (N) delivered by the motor.</summary>
    public double Effort { get { Read(out _, out _, out var e); return e; } }

    /// <summary>Mechanical power = effort × speed (W).</summary>
    public double Power { get { Read(out _, out var s, out var e); return s * e; } }

    private void Read(out double value, out double speed, out double effort) =>
        NativeMethods.Check(NativeMethods.chn_motor_get(Handle, out value, out speed, out effort));
}

/// <summary>Delegate for custom translational spring-damper-actuator force laws.</summary>
public delegate double SpringForceLaw(double time, double restLength, double length, double velocity);

/// <summary>Delegate for custom rotational spring-damper-actuator torque laws.</summary>
public delegate double TorqueLaw(double time, double restAngle, double angle, double velocity);

/// <summary>
/// Translational spring-damper-actuator (TSDA) between two points.
/// Default law: F = -k (L - L0) - c dL/dt + F_act. Provide <see cref="ForceLaw"/> for nonlinear behavior.
/// </summary>
public sealed unsafe class ChSpring : ChLink
{
    private GCHandle _callback;
    private SpringForceLaw? _law;

    /// <param name="restLength">Free length; negative = use the initial distance.</param>
    public ChSpring(ChBody body1, ChBody body2, Vec3 point1, Vec3 point2, double stiffness, double damping,
        double restLength = -1, double actuatorForce = 0, bool local = false)
        : base(NativeMethods.chn_link_create_spring(body1.Handle, body2.Handle, point1, point2, local ? 1 : 0,
            stiffness, damping, restLength, actuatorForce), body1, body2)
    {
    }

    public double Length { get { Read(out var l, out _, out _); return l; } }
    public double Velocity { get { Read(out _, out var v, out _); return v; } }
    public double Force { get { Read(out _, out _, out var f); return f; } }

    /// <summary>Custom force law (positive force pushes the bodies apart). Invoked on the solver thread.</summary>
    public SpringForceLaw? ForceLaw
    {
        get => _law;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _law = value;
            if (!_callback.IsAllocated) _callback = GCHandle.Alloc(this);
            NativeMethods.Check(NativeMethods.chn_spring_set_force_callback(Handle, &Trampoline, GCHandle.ToIntPtr(_callback)));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static double Trampoline(double time, double rest, double length, double vel, nint user)
    {
        try { return GCHandle.FromIntPtr(user).Target is ChSpring s && s._law is { } law ? law(time, rest, length, vel) : 0; }
        catch { return 0; }
    }

    private void Read(out double l, out double v, out double f) =>
        NativeMethods.Check(NativeMethods.chn_spring_get(Handle, out l, out v, out f));

    public override void Dispose()
    {
        base.Dispose();
        if (_callback.IsAllocated) _callback.Free();
    }
}

/// <summary>Rotational spring-damper-actuator (RSDA) acting about the Z axis of <paramref name="frame"/>.</summary>
public sealed unsafe class ChRotationalSpring : ChLink
{
    private GCHandle _callback;
    private TorqueLaw? _law;

    public ChRotationalSpring(ChBody body1, ChBody body2, Frame frame, double stiffness, double damping,
        double restAngle = 0, double actuatorTorque = 0)
        : base(NativeMethods.chn_link_create_rot_spring(body1.Handle, body2.Handle, frame, stiffness, damping, restAngle, actuatorTorque), body1, body2)
    {
    }

    public double Angle { get { Read(out var a, out _, out _); return a; } }
    public double Velocity { get { Read(out _, out var v, out _); return v; } }
    public double Torque { get { Read(out _, out _, out var t); return t; } }

    public TorqueLaw? TorqueLaw
    {
        get => _law;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _law = value;
            if (!_callback.IsAllocated) _callback = GCHandle.Alloc(this);
            NativeMethods.Check(NativeMethods.chn_rot_spring_set_torque_callback(Handle, &Trampoline, GCHandle.ToIntPtr(_callback)));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static double Trampoline(double time, double rest, double angle, double vel, nint user)
    {
        try { return GCHandle.FromIntPtr(user).Target is ChRotationalSpring s && s._law is { } law ? law(time, rest, angle, vel) : 0; }
        catch { return 0; }
    }

    private void Read(out double a, out double v, out double t) =>
        NativeMethods.Check(NativeMethods.chn_rot_spring_get(Handle, out a, out v, out t));

    public override void Dispose()
    {
        base.Dispose();
        if (_callback.IsAllocated) _callback.Free();
    }
}
