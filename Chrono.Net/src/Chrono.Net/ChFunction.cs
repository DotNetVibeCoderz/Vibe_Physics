using System.Runtime.InteropServices;
using Chrono.Net.Interop;

namespace Chrono.Net;

/// <summary>
/// A scalar function y = f(x) (usually of time) used to drive motors and imposed motions.
/// Built-in laws are evaluated natively; <see cref="FromDelegate"/> calls back into managed code.
/// </summary>
public class ChFunction : IDisposable
{
    internal FunctionHandle Handle { get; }

    private protected ChFunction(FunctionHandle handle) => Handle = NativeMethods.Check(handle);

    /// <summary>y = value.</summary>
    public static ChFunction Constant(double value) => new(NativeMethods.chn_function_const(value));

    /// <summary>y = y0 + slope * x.</summary>
    public static ChFunction Ramp(double y0, double slope) => new(NativeMethods.chn_function_ramp(y0, slope));

    /// <summary>y = amplitude * sin(2π f x + phase) + shift.</summary>
    public static ChFunction Sine(double amplitude, double frequency, double phase = 0, double shift = 0) =>
        new(NativeMethods.chn_function_sine(amplitude, frequency, phase, shift));

    /// <summary>Smooth 3-4-5 polynomial step from 0 to <paramref name="height"/> over <paramref name="width"/>.</summary>
    public static ChFunction Poly345(double height, double width) => new(NativeMethods.chn_function_poly345(height, width));

    /// <summary>A setpoint you update every step (e.g. from a controller or RL agent).</summary>
    public static SetpointFunction Setpoint() => new();

    /// <summary>Arbitrary managed law. The delegate is invoked from the solver thread during stepping.</summary>
    public static ChFunction FromDelegate(Func<double, double> function) => new DelegateFunction(function);

    /// <summary>Evaluates the function natively.</summary>
    public double Evaluate(double x) => NativeMethods.chn_function_eval(Handle, x);

    public virtual void Dispose()
    {
        Handle.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Function whose value is set explicitly, typically once per step by a controller.</summary>
public sealed class SetpointFunction : ChFunction
{
    private double _value;

    internal SetpointFunction() : base(NativeMethods.chn_function_setpoint()) { }

    public double Value
    {
        get => _value;
        set { _value = value; NativeMethods.chn_function_setpoint_set(Handle, value); }
    }
}

internal sealed unsafe class DelegateFunction : ChFunction
{
    private GCHandle _self;
    private readonly Func<double, double> _function;

    public DelegateFunction(Func<double, double> function) : this(function, GCHandle.Alloc(null)) { }

    private DelegateFunction(Func<double, double> function, GCHandle self)
        : base(NativeMethods.chn_function_callback(&Trampoline, GCHandle.ToIntPtr(self)))
    {
        _function = function;
        _self = self;
        _self.Target = this;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static double Trampoline(double x, nint user)
    {
        try
        {
            return GCHandle.FromIntPtr(user).Target is DelegateFunction f ? f._function(x) : 0;
        }
        catch
        {
            return 0; // exceptions must never cross into native code
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_self.IsAllocated) _self.Free();
    }
}
