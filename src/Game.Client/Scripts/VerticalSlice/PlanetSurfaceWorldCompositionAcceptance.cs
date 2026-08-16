using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetSurfaceWorldCompositionAcceptanceReport(
    bool Passed,
    int StarterPlanets,
    int SkyProfiles,
    int ResourcePlacements,
    bool VisibleStar,
    bool AtmosphereProfiles,
    bool CloudPolicy,
    bool ResourceDeterministic,
    bool StarterReserveClear,
    bool PlanetScopedIdentity,
    bool ColdRestoreDepletion,
    bool UntouchedDeltaEmpty,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planets={StarterPlanets}/4 sky={SkyProfiles}/4 " +
          $"resources={ResourcePlacements} persistence=1"
        : $"FAIL {Result}";

    public string BuildOutputLine() =>
        "TASK-160 planet surface world composition acceptance " +
        (Passed ? "PASS" : "FAIL") + ": " +
        $"starterPlanets={StarterPlanets}/4; " +
        $"skyProfiles={SkyProfiles}/4; " +
        $"resourcePlacements={ResourcePlacements}; " +
        $"visibleStar={(VisibleStar ? 1 : 0)}; " +
        $"atmosphereProfiles={(AtmosphereProfiles ? 1 : 0)}; " +
        $"cloudPolicy={(CloudPolicy ? 1 : 0)}; " +
        $"resourceDeterministic={(ResourceDeterministic ? 1 : 0)}; " +
        $"starterReserveClear={(StarterReserveClear ? 1 : 0)}; " +
        $"planetScopedIdentity={(PlanetScopedIdentity ? 1 : 0)}; " +
        $"coldRestoreDepletion={(ColdRestoreDepletion ? 1 : 0)}; " +
        $"untouchedDeltaEmpty={(UntouchedDeltaEmpty ? 1 : 0)}; " +
        $"result={Result}";
}

public static class PlanetSurfaceWorldCompositionAcceptanceRunner
{
    public static PlanetSurfaceWorldCompositionAcceptanceReport Run(
        GameContentCatalog contentCatalog,
        PlanetEnvironmentCatalog environmentCatalog,
        EcologyCatalog ecologyCatalog,
        PlanetaryPoiCatalog poiCatalog,
        CraftingRecipeDefinition repairRecipe,
        params CraftingRecipeDefinition[] stationRecipes)
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
            PlanetSurfaceSkyProfile[] skies = profiles
                .Select(profile =>
                    PlanetSurfaceWorldCompositionRuntime.BuildSkyProfile(
                        profile.Environment,
                        starter.StarType))
                .ToArray();

            bool visibleStar = skies.All(sky =>
                sky.SunEnergy >= 0.8 &&
                sky.SunElevationDegrees is >= 20.0 and <= 75.0 &&
                sky.SunAngularDiameterDegrees > 0.1);
            bool atmosphereProfiles = skies.All(sky =>
                sky.AtmosphereEnabled &&
                sky.FogDensity > 0.0 &&
                sky.SkyTopColor != sky.SkyHorizonColor);
            bool cloudPolicy = profiles.Zip(skies).All(pair =>
                pair.First.Environment.CloudLayerCount <= 0
                    ? pair.Second.CloudClusterCount == 0
                    : pair.Second.CloudClusterCount >= 6 &&
                      pair.Second.CloudOpacity > 0.0);

            PlanetSurfaceChunkCoordinate center = new(4, -3);
            List<IReadOnlyList<PlanetSurfaceResourcePlacement>> plans = new();
            bool deterministic = true;
            bool reserveClear = true;
            bool validDefinitions = true;
            foreach (PlanetSurfaceContentProfile profile in profiles)
            {
                IReadOnlyList<PlanetSurfaceResourcePlacement> first =
                    PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                        profile,
                        contentCatalog.Resources,
                        center);
                IReadOnlyList<PlanetSurfaceResourcePlacement> second =
                    PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                        profile,
                        contentCatalog.Resources,
                        center);
                plans.Add(first);
                deterministic &= BuildPlanSignature(first) ==
                    BuildPlanSignature(second);
                reserveClear &= first.All(placement =>
                    PlanetSurfaceWorldCompositionRuntime.IsOutsideStarterReserve(
                        placement.PositionX,
                        placement.PositionZ) &&
                    placement.SlopeDegrees <=
                        PlanetSurfaceWorldCompositionRuntime.MaximumResourceSlopeDegrees);
                validDefinitions &= first.All(placement =>
                    contentCatalog.Resources.ContainsKey(
                        placement.ResourceDefinitionId));
            }

            int placements = plans.Sum(plan => plan.Count);
            HashSet<string> allIds = plans
                .SelectMany(plan => plan)
                .Select(placement => placement.ResourceNodeId)
                .ToHashSet(StringComparer.Ordinal);
            bool planetScopedIdentity = allIds.Count == placements &&
                profiles.Select(profile =>
                        PlanetSurfaceWorldCompositionRuntime.BuildResourceNodeId(
                            profile.PlanetId,
                            center,
                            0))
                    .Distinct(StringComparer.Ordinal)
                    .Count() == profiles.Length;

            bool coldRestore = false;
            bool untouchedDeltaEmpty = false;
            PlanetSurfaceResourcePlacement? sample = plans
                .SelectMany(plan => plan)
                .FirstOrDefault();
            if (sample is not null &&
                contentCatalog.Resources.TryGetValue(
                    sample.ResourceDefinitionId,
                    out GameResourceDefinition? definition) &&
                definition is not null)
            {
                StarterRepairSession mined = new(
                    repairRecipe,
                    static _ => true,
                    stationRecipes);
                mined.TryCollect(
                    sample.ResourceNodeId,
                    definition.ItemDefinitionId,
                    definition.GetDeterministicYield(),
                    out _);
                SaveGameSnapshot minedSnapshot = StarterRepairSnapshotFactory.Create(
                    "save_1",
                    1,
                    mined,
                    0.0,
                    0.0,
                    0.0);
                StarterRepairSession restored =
                    StarterRepairSession.FromSnapshotWithDynamicResources(
                        minedSnapshot,
                        new Dictionary<string, ResourceNodeBinding>(
                            StringComparer.Ordinal),
                        repairRecipe,
                        static _ => true,
                        (nodeId, itemDefinitionId) =>
                            ResolveBinding(
                                contentCatalog,
                                nodeId,
                                itemDefinitionId),
                        stationRecipes);
                coldRestore = restored.CollectedNodeIds.Contains(
                    sample.ResourceNodeId,
                    StringComparer.Ordinal);

                StarterRepairSession untouched = new(
                    repairRecipe,
                    static _ => true,
                    stationRecipes);
                SaveGameSnapshot untouchedSnapshot =
                    StarterRepairSnapshotFactory.Create(
                        "save_1",
                        1,
                        untouched,
                        0.0,
                        0.0,
                        0.0);
                untouchedDeltaEmpty = untouchedSnapshot.Inventory.All(item =>
                    !item.ItemId.StartsWith(
                        "item.surface_resource.",
                        StringComparison.Ordinal));
            }

            bool passed = profiles.Length == 4 &&
                skies.Length == 4 &&
                visibleStar && atmosphereProfiles && cloudPolicy &&
                deterministic && reserveClear && validDefinitions &&
                placements >= 8 && planetScopedIdentity &&
                coldRestore && untouchedDeltaEmpty;
            return new PlanetSurfaceWorldCompositionAcceptanceReport(
                passed,
                profiles.Length,
                skies.Length,
                placements,
                visibleStar,
                atmosphereProfiles,
                cloudPolicy,
                deterministic && validDefinitions,
                reserveClear,
                planetScopedIdentity,
                coldRestore,
                untouchedDeltaEmpty,
                passed
                    ? "planet sky/star presentation, distributed chunk resources and delta-only depletion persistence verified"
                    : "one or more surface-world composition invariants failed");
        }
        catch (Exception exception)
        {
            return new PlanetSurfaceWorldCompositionAcceptanceReport(
                false, 0, 0, 0,
                false, false, false, false, false, false, false, false,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static ResourceNodeBinding? ResolveBinding(
        GameContentCatalog catalog,
        string nodeId,
        string itemDefinitionId)
    {
        if (!nodeId.StartsWith("surface_resource.", StringComparison.Ordinal))
        {
            return null;
        }
        GameResourceDefinition? resource = catalog.Resources.Values
            .FirstOrDefault(candidate => string.Equals(
                candidate.ItemDefinitionId,
                itemDefinitionId,
                StringComparison.Ordinal));
        return resource is null
            ? null
            : new ResourceNodeBinding(
                nodeId,
                itemDefinitionId,
                resource.GetDeterministicYield());
    }

    private static string BuildPlanSignature(
        IEnumerable<PlanetSurfaceResourcePlacement> placements) =>
        string.Join(
            "|",
            placements.Select(placement =>
                $"{placement.ResourceNodeId}:{placement.ResourceDefinitionId}:" +
                $"{placement.PositionX:0.000}:{placement.PositionY:0.000}:" +
                $"{placement.PositionZ:0.000}"));
}
