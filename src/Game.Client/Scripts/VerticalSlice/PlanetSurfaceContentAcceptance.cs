using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetSurfaceContentAcceptanceReport(
    bool Passed,
    int StarterPlanets,
    int DistinctBiomeProfiles,
    int DistinctRegions,
    bool EcologyClimateAware,
    bool AquaticPolicy,
    bool PoiClimateAware,
    bool PoiDeterministic,
    bool PerPlanetPersistence,
    bool LegacyStarterCompatible,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planets={StarterPlanets}/4 biomes={DistinctBiomeProfiles}/4 " +
          $"regions={DistinctRegions}/4 ecology=1 pois=1 persistence=1 legacy=1"
        : $"FAIL {Result}";

    public string BuildOutputLine() =>
        "TASK-154 multi-planet surface content acceptance " +
        (Passed ? "PASS" : "FAIL") + ": " +
        $"starterPlanets={StarterPlanets}/4; " +
        $"biomeProfiles={DistinctBiomeProfiles}/4; " +
        $"regions={DistinctRegions}/4; " +
        $"ecologyClimateAware={(EcologyClimateAware ? 1 : 0)}; " +
        $"aquaticPolicy={(AquaticPolicy ? 1 : 0)}; " +
        $"poiClimateAware={(PoiClimateAware ? 1 : 0)}; " +
        $"poiDeterministic={(PoiDeterministic ? 1 : 0)}; " +
        $"perPlanetPersistence={(PerPlanetPersistence ? 1 : 0)}; " +
        $"legacyStarter={(LegacyStarterCompatible ? 1 : 0)}; " +
        $"result={Result}";
}

public static class PlanetSurfaceContentAcceptanceRunner
{
    public static PlanetSurfaceContentAcceptanceReport Run(
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

            int biomeProfiles = profiles
                .Select(profile => string.Join("|", profile.ActiveBiomeIds))
                .Distinct(StringComparer.Ordinal)
                .Count();
            int regions = profiles
                .Select(profile => profile.RegionKey)
                .Distinct(StringComparer.Ordinal)
                .Count();

            bool ecologyClimateAware = true;
            bool aquaticPolicy = true;
            bool poiClimateAware = true;
            bool poiDeterministic = true;
            List<string> poiChecksums = new();
            foreach (PlanetSurfaceContentProfile profile in profiles)
            {
                EcologyPlan plan = surface.BuildEcologyPlan(profile);
                ecologyClimateAware &= plan.Flora.Count is >= 180 and <= 360 &&
                    plan.ActiveFauna.Count <= ecologyCatalog.ActiveFaunaLimit &&
                    plan.SimplifiedFauna.Count <= ecologyCatalog.SimplifiedFaunaLimit &&
                    plan.Flora.All(item => profile.ActiveBiomeIds.Contains(
                        item.BiomeId,
                        StringComparer.Ordinal)) &&
                    plan.ActiveFauna.Concat(plan.SimplifiedFauna).All(item =>
                        profile.ActiveBiomeIds.Contains(
                            item.BiomeId,
                            StringComparer.Ordinal));

                bool hasAquatic = plan.ActiveFauna.Concat(plan.SimplifiedFauna)
                    .Select(item => ecologyCatalog.GetFauna(item.FaunaId))
                    .Any(fauna => string.Equals(
                        fauna.MovementMode,
                        "Aquatic",
                        StringComparison.Ordinal));
                aquaticPolicy &= profile.WaterHabitatEnabled || !hasAquatic;

                IReadOnlyList<PlanetaryPoiPlacement> left =
                    surface.BuildPoiPlan(profile);
                IReadOnlyList<PlanetaryPoiPlacement> right =
                    surface.BuildPoiPlan(profile);
                poiClimateAware &= left.Count ==
                        PlanetaryPoiCatalog.ExpectedPoiTypeCount &&
                    left.All(item => profile.ActiveBiomeIds.Contains(
                        item.Environment.BiomeId,
                        StringComparer.Ordinal));
                string leftChecksum = PoiChecksum(left);
                string rightChecksum = PoiChecksum(right);
                poiDeterministic &= string.Equals(
                    leftChecksum,
                    rightChecksum,
                    StringComparison.Ordinal);
                poiChecksums.Add(leftChecksum);
            }
            poiClimateAware &= poiChecksums.Distinct(StringComparer.Ordinal).Count() ==
                profiles.Length;

            bool perPlanetPersistence = false;
            if (profiles.Length >= 2)
            {
                PlanetSurfaceContentProfile profile = profiles[1];
                EcologyPlan plan = surface.BuildEcologyPlan(profile);
                EcologyRuntime ecology = new(
                    ecologyCatalog,
                    plan,
                    profile.WorldSeed,
                    profile.RegionKey);
                ecology.TryScanFlora(
                    plan.Flora[0].InstanceId,
                    out _,
                    out _);
                EcologySaveData ecologySave = ecology.CreateSaveData();
                EcologyRuntime ecologyRestore = new(
                    ecologyCatalog,
                    plan,
                    profile.WorldSeed,
                    profile.RegionKey,
                    ecologySave);

                IReadOnlyList<PlanetaryPoiPlacement> placements =
                    surface.BuildPoiPlan(profile);
                PlanetaryExplorationRuntime exploration = new(
                    poiCatalog,
                    placements,
                    profile.WorldSeed,
                    profile.RegionKey);
                exploration.Scan(placements[0].InstanceId, out _);
                PlanetaryExplorationSaveData explorationSave =
                    exploration.CreateSaveData();
                PlanetaryExplorationRuntime explorationRestore = new(
                    poiCatalog,
                    placements,
                    profile.WorldSeed,
                    profile.RegionKey,
                    explorationSave);
                perPlanetPersistence = ecologyRestore.DiscoveredFloraCount == 1 &&
                    explorationRestore.DiscoveredCount == 1 &&
                    ecologySave.WorldSeed == profile.WorldSeed &&
                    explorationSave.WorldSeed == profile.WorldSeed &&
                    string.Equals(
                        ecologySave.RegionKey,
                        profile.RegionKey,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        explorationSave.RegionKey,
                        profile.RegionKey,
                        StringComparison.Ordinal);
            }

            EcologyPlan legacyEcology = EcologyPlanner.Plan(ecologyCatalog);
            IReadOnlyList<PlanetaryPoiPlacement> legacyPois =
                PlanetaryPoiPlanner.Plan(poiCatalog);
            bool legacyStarterCompatible =
                legacyEcology.Flora.Count == EcologyPlanner.GameplayFloraInstanceCount &&
                legacyEcology.ActiveFauna.Count == ecologyCatalog.ActiveFaunaLimit &&
                legacyEcology.SimplifiedFauna.Count == ecologyCatalog.SimplifiedFaunaLimit &&
                legacyPois.Count == PlanetaryPoiCatalog.ExpectedPoiTypeCount;

            bool passed = profiles.Length == 4 &&
                biomeProfiles == 4 &&
                regions == 4 &&
                ecologyClimateAware &&
                aquaticPolicy &&
                poiClimateAware &&
                poiDeterministic &&
                perPlanetPersistence &&
                legacyStarterCompatible;
            return new PlanetSurfaceContentAcceptanceReport(
                passed,
                profiles.Length,
                biomeProfiles,
                regions,
                ecologyClimateAware,
                aquaticPolicy,
                poiClimateAware,
                poiDeterministic,
                perPlanetPersistence,
                legacyStarterCompatible,
                passed
                    ? "planet-scoped biomes, ecology, POIs and delta persistence verified across the starter system"
                    : "one or more planet-scoped surface-content invariants failed");
        }
        catch (Exception exception)
        {
            return new PlanetSurfaceContentAcceptanceReport(
                false, 0, 0, 0, false, false, false, false, false, false,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string PoiChecksum(
        IReadOnlyList<PlanetaryPoiPlacement> placements) =>
        string.Join(
            "|",
            placements.Select(item =>
                $"{item.PoiTypeId}:{item.PositionX:0.000}:{item.PositionZ:0.000}:" +
                item.Environment.BiomeId));
}
