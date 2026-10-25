namespace Chrono.Net.Blazor;

/// <summary>Camera placement for <see cref="ChronoViewport"/>.</summary>
public sealed record ViewportCamera(Vec3 Target, double Distance, double YawDeg = 40, double PitchDeg = 25, int FollowBody = -1)
{
    internal object ToJs() => new
    {
        target = new[] { Target.X, Target.Y, Target.Z },
        distance = Distance,
        yaw = YawDeg,
        pitch = PitchDeg,
        follow = FollowBody,
    };
}
