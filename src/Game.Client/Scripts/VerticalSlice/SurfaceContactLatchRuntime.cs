using System;

/// <summary>
/// TASK-190 log/physics hysteresis around the piloted terrain hard floor.
/// A recovery episode remains latched until the ship is clearly above terrain
/// for several consecutive physics frames, preventing correction/recovered
/// chatter while skimming the same surface envelope.
/// </summary>
public static class SurfaceContactLatchRuntime
{
    public const double ReleaseClearanceMeters = 4.35;
    public const int ReleaseStableFrames = 12;

    public static int UpdateReleaseFrames(int currentFrames, double clearanceMeters)
    {
        if (!double.IsFinite(clearanceMeters) ||
            clearanceMeters < ReleaseClearanceMeters)
        {
            return 0;
        }
        return Math.Min(ReleaseStableFrames, Math.Max(0, currentFrames) + 1);
    }

    public static bool ShouldRelease(int stableFrames) =>
        stableFrames >= ReleaseStableFrames;
}
