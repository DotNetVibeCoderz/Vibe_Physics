using Xunit;

namespace Chrono.Net.Tests;

public class MathTests
{
    [Fact]
    public void QuaternionRotatesVectorAboutZ()
    {
        var q = Quat.FromAngleZ(Math.PI / 2);
        var v = q.Rotate(Vec3.UnitX);
        Assert.Equal(0, v.X, 9);
        Assert.Equal(1, v.Y, 9);
        Assert.Equal(0, v.Z, 9);
    }

    [Fact]
    public void FromToMapsDirections()
    {
        var q = Quat.FromTo(Vec3.UnitZ, new Vec3(1, 1, 0));
        var v = q.Rotate(Vec3.UnitZ);
        Assert.Equal(Math.Sqrt(0.5), v.X, 9);
        Assert.Equal(Math.Sqrt(0.5), v.Y, 9);
        Assert.Equal(0, v.Z, 9);
    }

    [Fact]
    public void FromToHandlesOppositeDirections()
    {
        var v = Quat.FromTo(Vec3.UnitZ, -Vec3.UnitZ).Rotate(Vec3.UnitZ);
        Assert.Equal(-1, v.Z, 9);
    }

    [Fact]
    public void EulerRoundTrip()
    {
        var q = Quat.FromEuler(0.3, -0.2, 1.1);
        var e = q.ToEuler();
        Assert.Equal(0.3, e.X, 9);
        Assert.Equal(-0.2, e.Y, 9);
        Assert.Equal(1.1, e.Z, 9);
    }

    [Fact]
    public void ComposeMatchesManualTransform()
    {
        var parent = new Frame(new Vec3(1, 2, 3), Quat.FromAngleY(0.5));
        var child = new Frame(new Vec3(0.5, 0, 0), Quat.FromAngleX(0.2));
        var c = parent.Compose(child);
        var p = new Vec3(0.1, 0.2, 0.3);
        var direct = c.TransformPoint(p);
        var manual = parent.TransformPoint(child.TransformPoint(p));
        Assert.Equal(manual.X, direct.X, 9);
        Assert.Equal(manual.Y, direct.Y, 9);
        Assert.Equal(manual.Z, direct.Z, 9);
    }

    [Fact]
    public void ColorHexRoundTrip()
    {
        var c = Color.FromHex("#F28C28");
        Assert.Equal("#F28C28", c.ToHex());
    }
}
