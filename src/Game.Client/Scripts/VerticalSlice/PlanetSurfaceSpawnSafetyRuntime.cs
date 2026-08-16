using System;

/// <summary>
/// TASK-174.1 cold-start safety policy for a CharacterBody3D standing on the
/// semantic terrain surface. The curved patch converts this semantic height to
/// physical radial Y afterwards, so the policy remains independent of the
/// current curvature anchor / floating origin.
/// </summary>
public static class PlanetSurfaceSpawnSafetyRuntime
{
    public const double MinimumBodyCenterClearanceMeters = 1.02;

    public static double RequiredSemanticHeight(
        PlanetSurfaceTerrainProfile profile,
        double logicalEastMeters,
        double logicalNorthMeters,
        double requestedSemanticHeightMeters)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double terrainHeight = PlanetSurfaceTerrainRuntime.SampleHeight(
            profile, logicalEastMeters, logicalNorthMeters);
        return Math.Max(
            requestedSemanticHeightMeters,
            terrainHeight + MinimumBodyCenterClearanceMeters);
    }
}
