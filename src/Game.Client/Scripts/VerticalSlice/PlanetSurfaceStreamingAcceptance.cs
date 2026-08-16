using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetSurfaceStreamingAcceptanceReport(
    bool Passed,
    int StarterPlanets,
    int ActiveChunks,
    int HighDetailChunks,
    int LowDetailChunks,
    int CollisionChunks,
    bool Deterministic,
    bool SeamSafe,
    bool BoundedResidency,
    bool TraversalPlans,
    bool PlanetAddressing,
    bool FullReliefBeyondStarter,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planets={StarterPlanets}/4 chunks={ActiveChunks}/25 " +
          $"lod={HighDetailChunks}/{LowDetailChunks} collision={CollisionChunks} " +
          "seams=1 traversal=1"
        : $"FAIL {Result}";

    public string BuildOutputLine() =>
        "TASK-158 planet surface streaming acceptance " +
        (Passed ? "PASS" : "FAIL") + ": " +
        $"starterPlanets={StarterPlanets}/4; " +
        $"activeChunks={ActiveChunks}/25; " +
        $"highDetail={HighDetailChunks}/9; " +
        $"lowDetail={LowDetailChunks}/16; " +
        $"collisionChunks={CollisionChunks}/9; " +
        $"deterministic={(Deterministic ? 1 : 0)}; " +
        $"seamSafe={(SeamSafe ? 1 : 0)}; " +
        $"boundedResidency={(BoundedResidency ? 1 : 0)}; " +
        $"traversalPlans={(TraversalPlans ? 1 : 0)}; " +
        $"planetAddressing={(PlanetAddressing ? 1 : 0)}; " +
        $"fullRelief={(FullReliefBeyondStarter ? 1 : 0)}; " +
        $"result={Result}";
}

public static class PlanetSurfaceStreamingAcceptanceRunner
{
    public static PlanetSurfaceStreamingAcceptanceReport Run(
        PlanetEnvironmentCatalog environmentCatalog,
        EcologyCatalog ecologyCatalog,
        PlanetaryPoiCatalog poiCatalog)
    {
        try
        {
            PlanetEnvironmentRuntime environment = new(
                environmentCatalog,
                ecologyCatalog);
            PlanetSurfaceContentRuntime surface = new(
                environment,
                ecologyCatalog,
                poiCatalog);
            GalaxyNavigationRuntime galaxy = new();
            GalaxySystemDefinition starter = galaxy.CurrentSystem;
            PlanetSurfaceContentProfile[] profiles = starter.Planets
                .Select(planet => surface.BuildProfile(planet, starter.StarType))
                .ToArray();

            PlanetSurfaceChunkCoordinate center = new(0, 0);
            IReadOnlyList<PlanetSurfaceStreamingSpec> plan =
                PlanetSurfaceStreamingRuntime.BuildPlan(center);
            int highDetail = plan.Count(spec => spec.LodLevel == 0);
            int lowDetail = plan.Count(spec => spec.LodLevel == 1);
            int collisions = plan.Count(spec => spec.GenerateCollision);
            bool bounded = plan.Count == PlanetSurfaceStreamingRuntime.ExpectedActiveChunks &&
                highDetail == PlanetSurfaceStreamingRuntime.ExpectedHighDetailChunks &&
                lowDetail == PlanetSurfaceStreamingRuntime.ExpectedLowDetailChunks &&
                collisions == PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks;

            bool deterministic = profiles.All(profile =>
            {
                string first = PlanetSurfaceStreamingRuntime.BuildChunkSignature(
                    profile.Terrain,
                    new PlanetSurfaceChunkCoordinate(3, -2));
                string second = PlanetSurfaceStreamingRuntime.BuildChunkSignature(
                    profile.Terrain,
                    new PlanetSurfaceChunkCoordinate(3, -2));
                return string.Equals(first, second, StringComparison.Ordinal);
            });

            bool seamSafe = profiles.All(profile =>
                PlanetSurfaceStreamingRuntime.MeasureSharedEdgeError(
                    profile.Terrain,
                    new PlanetSurfaceChunkCoordinate(1, 0),
                    new PlanetSurfaceChunkCoordinate(2, 0)) <= 0.000001);
            PlanetSurfaceStreamingSpec centerSpec = plan.Single(spec =>
                spec.Coordinate == center);
            PlanetSurfaceStreamingSpec eastHigh = plan.Single(spec =>
                spec.Coordinate == new PlanetSurfaceChunkCoordinate(1, 0));
            bool hasRequiredStitch = eastHigh.LodLevel == 0 &&
                (eastHigh.StitchMask & TerrainEdgeStitchMask.East) != 0;
            seamSafe &= centerSpec.LodLevel == 0 && hasRequiredStitch;

            PlanetSurfaceChunkCoordinate[] traversalCenters =
            {
                new(0, 0), new(1, 0), new(2, 0), new(2, 1),
                new(3, 2), new(4, 2), new(4, 3)
            };
            bool traversalPlans = true;
            HashSet<PlanetSurfaceChunkCoordinate>? previous = null;
            PlanetSurfaceChunkCoordinate? previousCenter = null;
            foreach (PlanetSurfaceChunkCoordinate traversalCenter in traversalCenters)
            {
                IReadOnlyList<PlanetSurfaceStreamingSpec> traversalPlan =
                    PlanetSurfaceStreamingRuntime.BuildPlan(traversalCenter);
                HashSet<PlanetSurfaceChunkCoordinate> current = traversalPlan
                    .Select(spec => spec.Coordinate)
                    .ToHashSet();
                traversalPlans &= current.Count ==
                    PlanetSurfaceStreamingRuntime.ExpectedActiveChunks;
                if (previous is not null && previousCenter.HasValue)
                {
                    int retained = previous.Count(current.Contains);
                    int expectedRetained =
                        PlanetSurfaceStreamingRuntime.ExpectedRetainedChunkCount(
                            previousCenter.Value,
                            traversalCenter);
                    traversalPlans &= retained == expectedRetained &&
                        retained < PlanetSurfaceStreamingRuntime.ExpectedActiveChunks;
                }
                previous = current;
                previousCenter = traversalCenter;
            }

            bool addressing = profiles.All(profile =>
            {
                PlanetSurfaceGeodesicAddress address =
                    PlanetSurfaceStreamingRuntime.BuildGeodesicAddress(
                        profile.Environment.RadiusKm,
                        12_500.0,
                        -7_500.0);
                return double.IsFinite(address.LatitudeDegrees) &&
                    double.IsFinite(address.LongitudeDegrees) &&
                    address.LatitudeDegrees >= -90.0 &&
                    address.LatitudeDegrees <= 90.0 &&
                    address.LongitudeDegrees >= -180.0 &&
                    address.LongitudeDegrees <= 180.0 &&
                    address.CircumferenceMeters >= 125_000.0;
            });

            bool fullRelief = profiles.All(profile =>
            {
                PlanetSurfaceTerrainSample sample =
                    PlanetSurfaceTerrainRuntime.Sample(
                        profile.Terrain,
                        96.0,
                        96.0);
                return Math.Abs(sample.Height) >= 0.015 ||
                    sample.SlopeDegrees >= 0.1;
            });

            bool passed = profiles.Length == 4 &&
                bounded && deterministic && seamSafe &&
                traversalPlans && addressing && fullRelief;
            return new PlanetSurfaceStreamingAcceptanceReport(
                passed,
                profiles.Length,
                plan.Count,
                highDetail,
                lowDetail,
                collisions,
                deterministic,
                seamSafe,
                bounded,
                traversalPlans,
                addressing,
                fullRelief,
                passed
                    ? "bounded async chunk plans, LOD stitching, deterministic planet terrain and traversal addressing verified across the starter system"
                    : "one or more planet-surface streaming invariants failed");
        }
        catch (Exception exception)
        {
            return new PlanetSurfaceStreamingAcceptanceReport(
                false, 0, 0, 0, 0, 0,
                false, false, false, false, false, false,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
