using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record GalaxyNavigationAcceptanceReport(
    bool Passed,
    string Result,
    bool DeterministicGeneration,
    bool CoordinateHierarchy,
    bool StarCoverage,
    bool PlanetBounds,
    bool RoutePlanning,
    bool Preconditions,
    bool HyperspaceJump,
    bool FuelDebited,
    bool VisitedPersistence,
    bool Stress100,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class GalaxyNavigationAcceptanceRunner
{
    public static async Task<GalaxyNavigationAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        ShipSystemsCatalog shipCatalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(shipCatalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            GalaxyNavigationRuntime generatorA = new();
            GalaxyNavigationRuntime generatorB = new();
            bool deterministicGeneration = true;
            bool coordinateHierarchy = true;
            HashSet<GalaxyStarType> starTypes = new();
            bool planetBounds = true;
            for (int index = 0; index < 1000; index++)
            {
                int x = index % 19 - 9;
                int y = index / 19 % 11 - 5;
                int z = index / (19 * 11) - 2;
                GalaxySystemDefinition left = generatorA.GenerateSystem(x, y, z);
                GalaxySystemDefinition right = generatorB.GenerateSystem(x, y, z);
                deterministicGeneration &= string.Equals(
                        JsonSerializer.Serialize(left),
                        JsonSerializer.Serialize(right),
                        StringComparison.Ordinal) &&
                    left.SystemId == right.SystemId;
                coordinateHierarchy &= string.Equals(
                        left.GalaxyId,
                        GalaxyNavigationRuntime.PrimaryGalaxyId,
                        StringComparison.Ordinal) &&
                    left.SectorX == x &&
                    left.SectorY == y &&
                    left.SectorZ == z &&
                    left.SystemId.StartsWith("system.", StringComparison.Ordinal) &&
                    left.Planets.All(planet =>
                        planet.PlanetId.StartsWith("planet.", StringComparison.Ordinal));
                starTypes.Add(left.StarType);
                planetBounds &= left.Planets.Count is >= 1 and <= 8 &&
                    left.Planets.Select(planet => planet.PlanetId)
                        .Distinct(StringComparer.Ordinal).Count() ==
                        left.Planets.Count;
            }

            bool starCoverage = starTypes.Count == 6;
            StarterRepairSession session = new(repairRecipe);
            bool repaired = session.TryCollect(
                    "acceptance.galaxy.salvage",
                    session.SalvageDefinitionId,
                    session.RequiredSalvage,
                    out _) &&
                session.TryRepair(out _) == StarterRepairResult.Repaired;
            ShipSystemsRuntime ship = new(shipCatalog, commissioned: repaired);
            StageOneVoyageRuntime voyage = new();
            GalaxyNavigationRuntime navigation = new();
            GalaxySystemDefinition destination = navigation.GenerateSystem(2, 1, 0);
            navigation.SelectDestination(destination);

            ShipSystemsRuntime blockedShip = new(shipCatalog);
            GalaxyNavigationRuntime blockedNavigation = new();
            blockedNavigation.SelectDestination(
                blockedNavigation.GenerateSystem(1, 0, 0));
            bool uncommissionedRejected =
                blockedNavigation.TryJumpToSelected(
                    blockedShip,
                    StageOneVoyageLocation.OrbitalStation,
                    out _) == GalaxyTravelActionResult.NotCommissioned;
            bool noDriveRejected = navigation.TryJumpToSelected(
                    ship,
                    StageOneVoyageLocation.OrbitalStation,
                    out _) == GalaxyTravelActionResult.HyperspaceNotReady;
            bool invalidLocationRejected = false;
            bool moduleInstalled = ship.TryInstall(
                    "module.ship.hyperspace_core",
                    out _) == ShipModuleInstallResult.Installed &&
                ship.HyperspaceReady;
            if (moduleInstalled)
            {
                invalidLocationRejected = navigation.TryJumpToSelected(
                        ship,
                        StageOneVoyageLocation.PlanetSurface,
                        out _) == GalaxyTravelActionResult.InvalidLocation;
            }

            bool preconditions = uncommissionedRejected && noDriveRejected &&
                moduleInstalled && invalidLocationRejected;
            bool boarded = voyage.TryBoard(ship, out _) ==
                StageOneVoyageActionResult.Applied;
            bool launched = boarded && voyage.TryLaunch(ship, out _) ==
                StageOneVoyageActionResult.Applied;
            voyage.UpdateFlightState(
                StageOneVoyageRuntime.StationDockPositionX,
                StageOneVoyageRuntime.StationDockPositionY,
                StageOneVoyageRuntime.StationDockPositionZ,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0);
            bool docked = launched && voyage.TryDock(ship, 0.0, 0.0, out _) ==
                StageOneVoyageActionResult.Applied;
            GalaxyRoutePlan route = navigation.PlanRoute(
                destination,
                ship.GetEffectiveStats().HyperdriveRange);
            bool routePlanning = route.Reachable && route.Systems.Count >= 2 &&
                route.Systems[0].SystemId == GalaxyNavigationRuntime.StarterSystemId &&
                route.Systems[^1].SystemId == destination.SystemId &&
                route.Systems.Zip(route.Systems.Skip(1), (left, right) =>
                    GalaxyNavigationRuntime.Distance(left, right)).All(distance =>
                        distance <= ship.GetEffectiveStats().HyperdriveRange + 0.001);

            double fuelBefore = ship.Fuel;
            GalaxyTravelActionResult jumpResult = docked
                ? navigation.TryJumpToSelected(
                    ship,
                    voyage.Location,
                    out _)
                : GalaxyTravelActionResult.InvalidLocation;
            double jumpDistance = navigation.TotalDistanceLightYears;
            double expectedFuel = fuelBefore -
                GalaxyNavigationRuntime.CalculateFuelCost(jumpDistance);
            bool hyperspaceJump = jumpResult == GalaxyTravelActionResult.Applied &&
                navigation.JumpCount == 1 &&
                navigation.CurrentSystem.SystemId !=
                    GalaxyNavigationRuntime.StarterSystemId;
            bool fuelDebited = Math.Abs(ship.Fuel - expectedFuel) < 0.001;
            bool visitedPersistence = navigation.VisitedSystemIds.Count == 2 &&
                navigation.VisitedSystemIds.Contains(
                    GalaxyNavigationRuntime.StarterSystemId) &&
                navigation.VisitedSystemIds.Contains(
                    navigation.CurrentSystem.SystemId);

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            voyage.ArriveAtOrbitalStationFromHyperspace();
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.05,
                playerPositionZ: 5.5,
                shipSystems: ship.CreateSaveData(),
                stageOneVoyage: voyage.CreateSaveData(),
                galaxyNavigation: navigation.CreateSaveData());
            await autosave.FlushAsync(
                AutosaveTrigger.Hyperspace,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            GalaxyNavigationRuntime restored = new(loaded?.GalaxyNavigation);
            bool coldRestore = loaded?.GalaxyNavigation is not null &&
                exactRoundTrip &&
                string.Equals(
                    JsonSerializer.Serialize(navigation.CreateSaveData()),
                    JsonSerializer.Serialize(restored.CreateSaveData()),
                    StringComparison.Ordinal) &&
                restored.JumpCount == 1 &&
                restored.VisitedSystemIds.Count == 2;

            GalaxyNavigationRuntime legacy = new(saveData: null);
            bool legacyFallback =
                legacy.CurrentSystem.SystemId ==
                    GalaxyNavigationRuntime.StarterSystemId &&
                legacy.JumpCount == 0 &&
                legacy.VisitedSystemIds.Count == 1;

            GalaxyNavigationRuntime stressNavigation = new();
            ShipSystemsRuntime stressShip = new(shipCatalog, commissioned: true);
            bool stressModule = stressShip.TryInstall(
                    "module.ship.compotium_drive_core",
                    out _) == ShipModuleInstallResult.Installed;
            bool stress100 = stressModule;
            for (int jump = 0; jump < 100 && stress100; jump++)
            {
                int targetX = jump % 2 == 0 ? 1 : 0;
                GalaxySystemDefinition target = stressNavigation.GenerateSystem(
                    targetX,
                    0,
                    0);
                stressNavigation.SelectDestination(target);
                stressShip.Refuel(1000.0);
                stress100 &= stressNavigation.TryJumpToSelected(
                        stressShip,
                        StageOneVoyageLocation.OrbitalStation,
                        out _) == GalaxyTravelActionResult.Applied;
            }

            stress100 &= stressNavigation.JumpCount == 100 &&
                stressNavigation.VisitedSystemIds.Count == 2 &&
                stressNavigation.TotalDistanceLightYears > 0.0;
            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten = logText.Contains(
                    "AUTOSAVE_COMPLETED",
                    StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.Hyperspace),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed = deterministicGeneration && coordinateHierarchy &&
                starCoverage && planetBounds && routePlanning && preconditions &&
                hyperspaceJump && fuelDebited && visitedPersistence && stress100 &&
                coldRestore && legacyFallback && exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            string result = passed
                ? "deterministic on-demand galaxy generation, hierarchical coordinates, route planning and one hundred hyperspace jumps preserved exact discovery and persistence state"
                : $"deterministic={(deterministicGeneration ? 1 : 0)}, coordinates={(coordinateHierarchy ? 1 : 0)}, stars={starTypes.Count}, planets={(planetBounds ? 1 : 0)}, route={(routePlanning ? 1 : 0)}, preconditions={(preconditions ? 1 : 0)}, jump={(hyperspaceJump ? 1 : 0)}, fuel={(fuelDebited ? 1 : 0)}, visited={(visitedPersistence ? 1 : 0)}, stress100={(stress100 ? 1 : 0)}, restore={(coldRestore ? 1 : 0)}, fallback={(legacyFallback ? 1 : 0)}, roundTrip={(exactRoundTrip ? 1 : 0)}({mismatch}), log={(logWritten ? 1 : 0)}, integrity={diagnostics.IntegrityResult}";
            stopwatch.Stop();
            return new GalaxyNavigationAcceptanceReport(
                passed,
                result,
                deterministicGeneration,
                coordinateHierarchy,
                starCoverage,
                planetBounds,
                routePlanning,
                preconditions,
                hyperspaceJump,
                fuelDebited,
                visitedPersistence,
                stress100,
                coldRestore,
                legacyFallback,
                exactRoundTrip,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            throw new InvalidOperationException(
                "TASK-114 galaxy navigation acceptance failed.",
                exception);
        }
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        string directory = Path.GetDirectoryName(databasePath) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(databasePath);
        string[] paths =
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + ".bak",
            Path.Combine(directory, baseName + ".autosave.log"),
            Path.Combine(directory, baseName + ".recovery.log")
        };
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
