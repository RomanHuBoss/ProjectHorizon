using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetSurfaceTerrainAcceptanceReport(
    bool Passed,
    int StarterPlanets,
    int DistinctMorphologies,
    bool Deterministic,
    bool CentralTerrace,
    bool GeometryBounds,
    bool WalkableCoverage,
    bool WaterBasinPolicy,
    bool EcologyGrounded,
    bool PoiTerrainAware,
    bool LegacyIdentitySafe,
    int Vertices,
    int Triangles,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planets={StarterPlanets}/4 morphology={DistinctMorphologies}/4 " +
          $"terrain=1 nav=1 ecology=1 pois=1 geometry={Vertices}/{Triangles}"
        : $"FAIL {Result}";

    public string BuildOutputLine() =>
        "TASK-156 planet terrain acceptance " +
        (Passed ? "PASS" : "FAIL") + ": " +
        $"starterPlanets={StarterPlanets}/4; " +
        $"distinctMorphology={DistinctMorphologies}/4; " +
        $"deterministic={(Deterministic ? 1 : 0)}; " +
        $"centralTerrace={(CentralTerrace ? 1 : 0)}; " +
        $"geometryBounds={(GeometryBounds ? 1 : 0)}; " +
        $"walkableCoverage={(WalkableCoverage ? 1 : 0)}; " +
        $"waterBasinPolicy={(WaterBasinPolicy ? 1 : 0)}; " +
        $"ecologyGrounded={(EcologyGrounded ? 1 : 0)}; " +
        $"poiTerrainAware={(PoiTerrainAware ? 1 : 0)}; " +
        $"legacyIdentitySafe={(LegacyIdentitySafe ? 1 : 0)}; " +
        $"vertices={Vertices}; triangles={Triangles}; " +
        $"result={Result}";
}

public static class PlanetSurfaceTerrainAcceptanceRunner
{
    public static PlanetSurfaceTerrainAcceptanceReport Run(
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

            int distinctMorphologies = profiles
                .Select(profile =>
                    PlanetSurfaceTerrainRuntime.MorphologySignature(profile.Terrain))
                .Distinct(StringComparer.Ordinal)
                .Count();
            bool deterministic = profiles.All(profile =>
            {
                PlanetSurfaceTerrainSample left = PlanetSurfaceTerrainRuntime.Sample(
                    profile.Terrain, 27.25, -18.75);
                PlanetSurfaceTerrainSample right = PlanetSurfaceTerrainRuntime.Sample(
                    profile.Terrain, 27.25, -18.75);
                return left.Equals(right);
            });
            bool centralTerrace = profiles.All(profile =>
                Math.Abs(PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile.Terrain, 0.0, 0.0)) <= 0.001 &&
                Math.Abs(PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile.Terrain, 10.0, -7.0)) <= 0.001 &&
                PlanetSurfaceTerrainRuntime.Sample(
                    profile.Terrain, 7.0, 7.0).SlopeDegrees <= 0.15);

            bool geometryBounds = true;
            bool walkableCoverage = true;
            bool waterBasinPolicy = true;
            bool ecologyGrounded = true;
            bool poiTerrainAware = true;
            foreach (PlanetSurfaceContentProfile profile in profiles)
            {
                (double minimum, double maximum, double walkableRatio) =
                    SampleTerrainEnvelope(profile.Terrain);
                geometryBounds &= minimum >= -profile.Terrain.HeightAmplitude - 1.26 &&
                    maximum <= profile.Terrain.HeightAmplitude + 0.01 &&
                    maximum - minimum >= 0.55;
                walkableCoverage &= walkableRatio >= 0.70;

                double interactionBasin = PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile.Terrain, 22.0, 22.0);
                double habitatBasin = PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile.Terrain, -25.5, 25.5);
                waterBasinPolicy &= profile.WaterHabitatEnabled
                    ? interactionBasin <= -0.35 && habitatBasin <= -0.35
                    : !profile.Terrain.WaterBasinsEnabled;

                EcologyPlan ecology = surface.BuildEcologyPlan(profile);
                ecologyGrounded &= ecology.Flora.Take(24).All(placement =>
                    Math.Abs(placement.PositionY -
                        PlanetSurfaceTerrainRuntime.SampleHeight(
                            profile.Terrain,
                            placement.PositionX,
                            placement.PositionZ)) <= 0.000001);
                ecologyGrounded &= ecology.ActiveFauna
                    .Where(spawn => !string.Equals(
                        ecologyCatalog.GetFauna(spawn.FaunaId).MovementMode,
                        "Aquatic",
                        StringComparison.Ordinal))
                    .Take(12)
                    .All(spawn => double.IsFinite(spawn.PositionY));

                IReadOnlyList<PlanetaryPoiPlacement> pois =
                    surface.BuildPoiPlan(profile);
                poiTerrainAware &= pois.Count ==
                    PlanetaryPoiCatalog.ExpectedPoiTypeCount &&
                    pois.All(placement =>
                        double.IsFinite(placement.Environment.Height) &&
                        double.IsFinite(placement.Environment.SlopeDegrees) &&
                        PlanetaryPoiPlanner.MeetsDefinitionConstraints(
                            poiCatalog.GetDefinition(placement.PoiTypeId),
                            placement.Environment));
            }

            IReadOnlyList<PlanetaryPoiPlacement> legacyBefore =
                PlanetaryPoiPlanner.Plan(poiCatalog);
            IReadOnlyList<PlanetaryPoiPlacement> legacyAfter =
                PlanetaryPoiPlanner.Plan(poiCatalog);
            EcologyPlan legacyEcologyBefore = EcologyPlanner.Plan(ecologyCatalog);
            EcologyPlan legacyEcologyAfter = EcologyPlanner.Plan(ecologyCatalog);
            bool legacyIdentitySafe = legacyBefore.Select(item =>
                    $"{item.InstanceId}:{item.PositionX:0.000}:{item.PositionZ:0.000}")
                .SequenceEqual(legacyAfter.Select(item =>
                    $"{item.InstanceId}:{item.PositionX:0.000}:{item.PositionZ:0.000}")) &&
                legacyEcologyBefore.Flora.Select(item => item.InstanceId)
                    .SequenceEqual(legacyEcologyAfter.Flora.Select(item => item.InstanceId));

            int vertices = PlanetSurfaceTerrainRuntime.DefaultResolution *
                PlanetSurfaceTerrainRuntime.DefaultResolution;
            int triangles = (PlanetSurfaceTerrainRuntime.DefaultResolution - 1) *
                (PlanetSurfaceTerrainRuntime.DefaultResolution - 1) * 2;
            bool passed = profiles.Length == 4 &&
                distinctMorphologies == 4 &&
                deterministic &&
                centralTerrace &&
                geometryBounds &&
                walkableCoverage &&
                waterBasinPolicy &&
                ecologyGrounded &&
                poiTerrainAware &&
                legacyIdentitySafe;
            return new PlanetSurfaceTerrainAcceptanceReport(
                passed,
                profiles.Length,
                distinctMorphologies,
                deterministic,
                centralTerrace,
                geometryBounds,
                walkableCoverage,
                waterBasinPolicy,
                ecologyGrounded,
                poiTerrainAware,
                legacyIdentitySafe,
                vertices,
                triangles,
                passed
                    ? "planet-specific deterministic relief, terrain projection, walkability and compatibility verified across the starter system"
                    : "one or more planet-terrain invariants failed");
        }
        catch (Exception exception)
        {
            return new PlanetSurfaceTerrainAcceptanceReport(
                false,
                0,
                0,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                0,
                0,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static (double Minimum, double Maximum, double WalkableRatio)
        SampleTerrainEnvelope(PlanetSurfaceTerrainProfile profile)
    {
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        int walkable = 0;
        int total = 0;
        for (double z = -36.0; z <= 36.0; z += 3.0)
        {
            for (double x = -36.0; x <= 36.0; x += 3.0)
            {
                PlanetSurfaceTerrainSample sample =
                    PlanetSurfaceTerrainRuntime.Sample(profile, x, z);
                minimum = Math.Min(minimum, sample.Height);
                maximum = Math.Max(maximum, sample.Height);
                if (sample.SlopeDegrees <= profile.MaximumWalkableSlopeDegrees)
                {
                    walkable++;
                }
                total++;
            }
        }
        return (minimum, maximum, total == 0 ? 0.0 : walkable / (double)total);
    }
}
