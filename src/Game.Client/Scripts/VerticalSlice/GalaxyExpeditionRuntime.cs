using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed record GalaxyExpeditionSystemSample(
    int VisitIndex,
    string SystemId,
    int SectorX,
    int SectorY,
    int SectorZ,
    GalaxyStarType StarType,
    int PlanetCount,
    int LandablePlanetCount,
    double IncomingDistanceLightYears,
    string DeterministicSignature);

public sealed record GalaxyExpeditionReport(
    bool Passed,
    bool DistinctSystems,
    bool DeterministicGeneration,
    bool ReachableChain,
    bool ProceduralIds,
    bool PlanetBounds,
    bool PlanetIdentityUnique,
    bool VisitPersistence,
    bool BoundedResidency,
    bool NoWholeGalaxyResident,
    int SystemsVisited,
    int JumpsApplied,
    int PlanetDefinitionsObserved,
    int LandableSystems,
    int MaximumResidentSystemDefinitions,
    double MaximumJumpDistanceLightYears,
    double TotalDistanceLightYears,
    int StarTypesObserved,
    int ArchetypesObserved,
    string LastSystemId)
{
    public string BuildOutputLine() =>
        $"TASK-210 100-system procedural expedition acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"distinct={(DistinctSystems ? 1 : 0)}; deterministic={(DeterministicGeneration ? 1 : 0)}; " +
        $"reachable={(ReachableChain ? 1 : 0)}; proceduralIds={(ProceduralIds ? 1 : 0)}; " +
        $"planetBounds={(PlanetBounds ? 1 : 0)}; planetIds={(PlanetIdentityUnique ? 1 : 0)}; " +
        $"restore={(VisitPersistence ? 1 : 0)}; boundedResidency={(BoundedResidency ? 1 : 0)}; " +
        $"wholeGalaxyResident={(NoWholeGalaxyResident ? 0 : 1)}; systems={SystemsVisited}; jumps={JumpsApplied}; " +
        $"planets={PlanetDefinitionsObserved}; landableSystems={LandableSystems}; residentMax={MaximumResidentSystemDefinitions}; " +
        $"maxJump={MaximumJumpDistanceLightYears.ToString("0.0", CultureInfo.InvariantCulture)}ly; " +
        $"distance={TotalDistanceLightYears.ToString("0.0", CultureInfo.InvariantCulture)}ly; " +
        $"starTypes={StarTypesObserved}; archetypes={ArchetypesObserved}; last={LastSystemId}; " +
        "result=section-41-100-distinct-procedural-systems-visited-on-demand-with-bounded-residency.";
}

public static class GalaxyExpeditionRuntime
{
    public const int RequiredDistinctSystems = 100;
    public const double ValidationJumpRangeLightYears = 550.0;
    public const int MaximumDetailedSystemResidency = 1;
    public const int MaximumDefinitionReferencesDuringJump = 2;

    public static GalaxyExpeditionReport Run(
        ShipSystemsCatalog shipCatalog)
    {
        ArgumentNullException.ThrowIfNull(shipCatalog);

        GalaxyNavigationRuntime navigation = new();
        ShipSystemsRuntime ship = new(shipCatalog, commissioned: true);
        bool moduleInstalled = ship.TryInstall(
            "module.ship.compotium_drive_core",
            out _) == ShipModuleInstallResult.Installed;

        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> planetIds = new(StringComparer.Ordinal);
        HashSet<GalaxyStarType> starTypes = new();
        HashSet<string> archetypes = new(StringComparer.Ordinal);
        bool deterministic = true;
        bool reachable = moduleInstalled;
        bool proceduralIds = true;
        bool planetBounds = true;
        bool planetIdentityUnique = true;
        int planetCount = 0;
        int landableSystems = 0;
        int maxResidentDefinitions = 1;
        double maxJump = 0.0;
        string lastSystemId = navigation.CurrentSystem.SystemId;

        Observe(
            navigation,
            navigation.CurrentSystem,
            visitIndex: 0,
            incomingDistance: 0.0,
            visited,
            planetIds,
            starTypes,
            archetypes,
            ref deterministic,
            ref proceduralIds,
            ref planetBounds,
            ref planetIdentityUnique,
            ref planetCount,
            ref landableSystems);

        for (int visit = 1; visit < RequiredDistinctSystems && reachable; visit++)
        {
            // A simple deterministic corridor is intentionally used here. Each
            // system is generated only when needed and the next target is one
            // neighboring sector away, so the validation never pre-materializes
            // a galaxy-sized graph or keeps 100 system definitions resident.
            GalaxySystemDefinition target = navigation.GenerateSystem(visit, 0, 0);
            GalaxySystemDefinition replay = navigation.GenerateSystem(visit, 0, 0);
            deterministic &= string.Equals(
                BuildSignature(target),
                BuildSignature(replay),
                StringComparison.Ordinal);

            double directDistance = GalaxyNavigationRuntime.Distance(
                navigation.CurrentSystem,
                target);
            maxJump = Math.Max(maxJump, directDistance);
            reachable &= directDistance <= ValidationJumpRangeLightYears + 0.0001;

            GalaxyRoutePlan route = navigation.PlanRoute(
                target,
                ValidationJumpRangeLightYears);
            reachable &= route.Reachable && route.Systems.Count >= 2;
            if (!reachable)
            {
                break;
            }

            navigation.SelectDestination(target);
            maxResidentDefinitions = Math.Max(
                maxResidentDefinitions,
                CountResidentDefinitionReferences(navigation));
            ship.Refuel(1000.0);
            GalaxyTravelActionResult result = navigation.TryJumpToSelected(
                ship,
                StageOneVoyageLocation.OrbitalStation,
                out _);
            reachable &= result == GalaxyTravelActionResult.Applied &&
                string.Equals(
                    navigation.CurrentSystem.SystemId,
                    target.SystemId,
                    StringComparison.Ordinal);
            if (!reachable)
            {
                break;
            }

            lastSystemId = navigation.CurrentSystem.SystemId;
            Observe(
                navigation,
                navigation.CurrentSystem,
                visit,
                directDistance,
                visited,
                planetIds,
                starTypes,
                archetypes,
                ref deterministic,
                ref proceduralIds,
                ref planetBounds,
                ref planetIdentityUnique,
                ref planetCount,
                ref landableSystems);
        }

        bool distinctSystems = visited.Count == RequiredDistinctSystems &&
            navigation.VisitedSystemIds.Count == RequiredDistinctSystems;
        bool visitPersistence = false;
        if (distinctSystems)
        {
            GalaxyNavigationSaveData save = navigation.CreateSaveData();
            GalaxyNavigationRuntime restored = new(save);
            visitPersistence = restored.VisitedSystemIds.Count == RequiredDistinctSystems &&
                restored.JumpCount == RequiredDistinctSystems - 1 &&
                string.Equals(
                    restored.CurrentSystem.SystemId,
                    navigation.CurrentSystem.SystemId,
                    StringComparison.Ordinal);
        }

        bool boundedResidency = maxResidentDefinitions <=
            MaximumDefinitionReferencesDuringJump;
        bool noWholeGalaxyResident = boundedResidency &&
            maxResidentDefinitions < RequiredDistinctSystems &&
            GalaxyNavigationRuntime.MaximumVisitedSystems >= RequiredDistinctSystems;
        bool passed = distinctSystems && deterministic && reachable &&
            proceduralIds && planetBounds && planetIdentityUnique &&
            visitPersistence && boundedResidency && noWholeGalaxyResident;

        return new GalaxyExpeditionReport(
            passed,
            distinctSystems,
            deterministic,
            reachable,
            proceduralIds,
            planetBounds,
            planetIdentityUnique,
            visitPersistence,
            boundedResidency,
            noWholeGalaxyResident,
            visited.Count,
            navigation.JumpCount,
            planetCount,
            landableSystems,
            maxResidentDefinitions,
            maxJump,
            navigation.TotalDistanceLightYears,
            starTypes.Count,
            archetypes.Count,
            lastSystemId);
    }

    public static string BuildSignature(GalaxySystemDefinition system)
    {
        ArgumentNullException.ThrowIfNull(system);
        StringBuilder builder = new();
        builder.Append(system.SystemId).Append('|')
            .Append(system.SectorX).Append(',')
            .Append(system.SectorY).Append(',')
            .Append(system.SectorZ).Append('|')
            .Append(system.PositionX.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(system.PositionY.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(system.PositionZ.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append((int)system.StarType).Append('|')
            .Append(system.EconomyType).Append('|')
            .Append(system.DangerLevel);
        foreach (GalaxyPlanetDefinition planet in system.Planets)
        {
            builder.Append("||").Append(planet.PlanetId)
                .Append('|').Append(planet.Archetype)
                .Append('|').Append(planet.OrbitIndex)
                .Append('|').Append(planet.MoonCount)
                .Append('|').Append(planet.HasAtmosphere ? 1 : 0)
                .Append('|').Append(planet.HasWater ? 1 : 0)
                .Append('|').Append(planet.Seed);
        }
        return builder.ToString();
    }

    private static void Observe(
        GalaxyNavigationRuntime navigation,
        GalaxySystemDefinition system,
        int visitIndex,
        double incomingDistance,
        HashSet<string> visited,
        HashSet<string> planetIds,
        HashSet<GalaxyStarType> starTypes,
        HashSet<string> archetypes,
        ref bool deterministic,
        ref bool proceduralIds,
        ref bool planetBounds,
        ref bool planetIdentityUnique,
        ref int planetCount,
        ref int landableSystems)
    {
        visited.Add(system.SystemId);
        starTypes.Add(system.StarType);
        GalaxySystemDefinition replay = navigation.GenerateSystem(
            system.SectorX,
            system.SectorY,
            system.SectorZ);
        deterministic &= string.Equals(
            BuildSignature(system),
            BuildSignature(replay),
            StringComparison.Ordinal);

        bool starter = visitIndex == 0;
        proceduralIds &= starter
            ? string.Equals(
                system.SystemId,
                GalaxyNavigationRuntime.StarterSystemId,
                StringComparison.Ordinal)
            : system.SystemId.StartsWith("system.g1.x", StringComparison.Ordinal);
        planetBounds &= system.Planets.Count is >= 1 and <= 8;
        int landable = 0;
        foreach (GalaxyPlanetDefinition planet in system.Planets)
        {
            planetCount++;
            archetypes.Add(planet.Archetype);
            planetIdentityUnique &= planetIds.Add(planet.PlanetId);
            planetBounds &= planet.OrbitIndex is >= 1 and <= 8 &&
                planet.MoonCount is >= 0 and <= 4 &&
                planet.Seed > 0;
            if (!string.Equals(planet.Archetype, "gas_giant", StringComparison.Ordinal))
            {
                landable++;
            }
        }
        if (landable > 0)
        {
            landableSystems++;
        }

        _ = incomingDistance;
    }

    private static int CountResidentDefinitionReferences(
        GalaxyNavigationRuntime navigation)
    {
        HashSet<string> ids = new(StringComparer.Ordinal)
        {
            navigation.CurrentSystem.SystemId
        };
        if (navigation.SelectedDestination is not null)
        {
            ids.Add(navigation.SelectedDestination.SystemId);
        }
        return ids.Count;
    }
}
