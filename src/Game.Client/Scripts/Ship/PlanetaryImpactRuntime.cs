using System;

/// <summary>
/// TASK-180.2 impact policy. Surface assistance prevents ordinary pilot mistakes,
/// but a deliberate high-energy dive is not converted into an invulnerable hover.
/// </summary>
public static class PlanetaryImpactRuntime
{
    public const float SurfaceSafetyMaximumRecoverableInwardSpeed = 12.0f;
    public const float LethalNormalImpactSpeed = 16.0f;
    public const float LethalTotalImpactSpeed = 30.0f;

    public static bool IsLethalSurfaceImpact(
        float normalClosingSpeed,
        float totalSpeed)
    {
        if (!float.IsFinite(normalClosingSpeed) ||
            !float.IsFinite(totalSpeed) ||
            normalClosingSpeed < 0.0f || totalSpeed < 0.0f)
        {
            return false;
        }

        return normalClosingSpeed >= LethalNormalImpactSpeed ||
            (normalClosingSpeed >= SurfaceSafetyMaximumRecoverableInwardSpeed &&
             totalSpeed >= LethalTotalImpactSpeed);
    }
}
