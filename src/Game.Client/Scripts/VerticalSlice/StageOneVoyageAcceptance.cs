using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record StageOneVoyageAcceptanceReport(
    bool Passed,
    string Result,
    bool DerivedStatsApplied,
    bool PreRepairBlocked,
    bool Takeoff,
    bool FuelDebited,
    bool Docking,
    bool StationVisited,
    bool Undock,
    bool Landing,
    bool LoopCompleted,
    bool ReadinessRejected,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class StageOneVoyageAcceptanceRunner
{
    public static async Task<StageOneVoyageAcceptanceReport> RunAsync(
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
            StageOneVoyageRuntime blockedVoyage = new();
            ShipSystemsRuntime blockedShip = new(shipCatalog);
            bool preRepairBlocked = blockedVoyage.TryBoard(
                    blockedShip,
                    out _) == StageOneVoyageActionResult.NotCommissioned &&
                !blockedShip.Commissioned &&
                !blockedShip.FlightReady;

            StarterRepairSession session = new(repairRecipe);
            bool repaired = session.TryCollect(
                    "acceptance.stage_one.salvage",
                    session.SalvageDefinitionId,
                    session.RequiredSalvage,
                    out _) &&
                session.TryRepair(out _) == StarterRepairResult.Repaired;
            ShipSystemsRuntime ship = new(shipCatalog, commissioned: repaired);
            StageOneVoyageRuntime voyage = new();
            StageOneVoyageFlightProfile profile =
                StageOneVoyageRuntime.CreateFlightProfile(ship);
            ShipEffectiveStats stats = ship.GetEffectiveStats();
            bool derivedStatsApplied =
                profile.Acceleration >= 4.0 &&
                profile.MaxSpeed >= 20.0 &&
                Math.Abs(profile.Acceleration -
                    Math.Clamp(stats.Acceleration, 4.0, 80.0)) < 0.001 &&
                Math.Abs(profile.MaxSpeed -
                    Math.Clamp(stats.MaxSpeed, 20.0, 180.0)) < 0.001 &&
                profile.BoostMaxSpeed > profile.MaxSpeed &&
                profile.PitchRateDegrees > 0.0 &&
                profile.YawRateDegrees > 0.0 &&
                profile.RollRateDegrees > 0.0 &&
                Math.Abs(profile.AtmosphericEfficiency -
                    stats.AtmosphericEfficiency) < 0.001;

            double fuelBefore = ship.Fuel;
            bool boarded = voyage.TryBoard(ship, out _) ==
                StageOneVoyageActionResult.Applied;
            bool takeoff = boarded && voyage.TryLaunch(ship, out _) ==
                    StageOneVoyageActionResult.Applied &&
                voyage.Location == StageOneVoyageLocation.OutboundFlight &&
                voyage.Piloted &&
                voyage.TakeoffCount == 1;
            bool launchFuelDebited = Math.Abs(
                ship.Fuel - (fuelBefore - StageOneVoyageRuntime.LaunchFuelCost)) <
                0.001;

            voyage.UpdateFlightState(
                0.0,
                22.0,
                -40.0,
                0.05,
                0.10,
                0.0,
                0.0,
                0.0,
                -5.0);

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);

            SaveGameSnapshot activeExpected =
                StarterRepairSnapshotFactory.Create(
                    slotId,
                    revision: 1,
                    session,
                    playerPositionX: 0.0,
                    playerPositionY: 1.05,
                    playerPositionZ: 5.5,
                    shipSystems: ship.CreateSaveData(),
                    stageOneVoyage: voyage.CreateSaveData());
            await autosave.FlushAsync(
                AutosaveTrigger.Takeoff,
                activeExpected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? activeLoaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool activeRoundTrip = SaveDatabase.SnapshotsEqual(
                activeExpected,
                activeLoaded,
                out string activeMismatch);
            StageOneVoyageRuntime activeRestoredVoyage = new(
                activeLoaded?.StageOneVoyage);
            ShipSystemsRuntime activeRestoredShip = new(
                shipCatalog,
                activeLoaded?.ShipSystems,
                commissioned: session.ShipRepaired);
            bool activeColdRestore = activeLoaded?.StageOneVoyage is not null &&
                activeRoundTrip &&
                string.Equals(
                    JsonSerializer.Serialize(voyage.CreateSaveData()),
                    JsonSerializer.Serialize(
                        activeRestoredVoyage.CreateSaveData()),
                    StringComparison.Ordinal) &&
                activeRestoredVoyage.Location ==
                    StageOneVoyageLocation.OutboundFlight &&
                activeRestoredVoyage.Piloted &&
                activeRestoredShip.Commissioned &&
                activeRestoredShip.FlightReady &&
                Math.Abs(activeRestoredShip.Fuel - ship.Fuel) < 0.001;

            bool rangeRejected = voyage.TryDock(
                    ship,
                    StageOneVoyageRuntime.DockingRangeMeters + 1.0,
                    0.0,
                    out _) == StageOneVoyageActionResult.OutsideApproach;
            bool speedRejected = voyage.TryDock(
                    ship,
                    0.0,
                    StageOneVoyageRuntime.MaximumDockingSpeed + 1.0,
                    out _) == StageOneVoyageActionResult.TooFast;
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
            bool docking = rangeRejected && speedRejected &&
                voyage.TryDock(ship, 0.0, 0.0, out _) ==
                    StageOneVoyageActionResult.Applied &&
                voyage.Location == StageOneVoyageLocation.OrbitalStation &&
                voyage.DockingCount == 1;
            bool stationVisited = voyage.StationVisited &&
                voyage.StationVisitedThisLoop;
            bool undock = voyage.TryUndock(ship, out _) ==
                    StageOneVoyageActionResult.Applied &&
                voyage.Location == StageOneVoyageLocation.InboundFlight;

            ship.ApplyDamage("ship.system.landing", 1000.0, out _);
            StageOneVoyageActionResult landingRejection = voyage.TryLand(
                ship,
                0.0,
                0.0,
                out _);
            bool rejectedByReadiness =
                landingRejection == StageOneVoyageActionResult.FlightNotReady ||
                landingRejection ==
                    StageOneVoyageActionResult.LandingSystemOffline;
            bool readinessRejected = !ship.FlightReady &&
                rejectedByReadiness;
            bool landingRepaired = ship.Repair(
                    "ship.system.landing",
                    1000.0,
                    out _) == ShipSystemMutationResult.Applied &&
                ship.FlightReady;
            voyage.UpdateFlightState(
                StageOneVoyageRuntime.SurfacePositionX,
                StageOneVoyageRuntime.LaunchPositionY,
                StageOneVoyageRuntime.SurfacePositionZ,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0);
            bool landing = landingRepaired &&
                voyage.TryLand(ship, 0.0, 0.0, out _) ==
                    StageOneVoyageActionResult.Applied &&
                voyage.Location == StageOneVoyageLocation.PlanetSurface &&
                voyage.LandingCount == 1;
            double expectedFinalFuel = fuelBefore -
                StageOneVoyageRuntime.LaunchFuelCost -
                StageOneVoyageRuntime.DockFuelCost -
                StageOneVoyageRuntime.UndockFuelCost -
                StageOneVoyageRuntime.LandingFuelCost;
            bool fuelDebited = launchFuelDebited &&
                Math.Abs(ship.Fuel - expectedFinalFuel) < 0.001;
            bool loopCompleted = landing && voyage.CompletedLoops == 1 &&
                voyage.TryDisembark(out _) ==
                    StageOneVoyageActionResult.Applied &&
                !voyage.Piloted;

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 2,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.05,
                playerPositionZ: 5.5,
                shipSystems: ship.CreateSaveData(),
                stageOneVoyage: voyage.CreateSaveData());
            await autosave.FlushAsync(
                AutosaveTrigger.Landing,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            StageOneVoyageRuntime restoredVoyage = new(
                loaded?.StageOneVoyage);
            ShipSystemsRuntime restoredShip = new(
                shipCatalog,
                loaded?.ShipSystems,
                commissioned: session.ShipRepaired);
            bool finalRestore = loaded?.StageOneVoyage is not null &&
                string.Equals(
                    JsonSerializer.Serialize(voyage.CreateSaveData()),
                    JsonSerializer.Serialize(restoredVoyage.CreateSaveData()),
                    StringComparison.Ordinal) &&
                restoredShip.Commissioned &&
                restoredShip.FlightReady &&
                restoredVoyage.CompletedLoops == 1 &&
                !restoredVoyage.Piloted &&
                Math.Abs(restoredShip.Fuel - expectedFinalFuel) < 0.001;
            bool coldRestore = activeColdRestore && finalRestore;

            StageOneVoyageRuntime legacy = new(saveData: null);
            bool legacyFallback =
                legacy.Location == StageOneVoyageLocation.PlanetSurface &&
                !legacy.Piloted &&
                legacy.TakeoffCount == 0 &&
                legacy.DockingCount == 0 &&
                legacy.LandingCount == 0 &&
                legacy.CompletedLoops == 0;
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
                    nameof(AutosaveTrigger.Takeoff),
                    StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.Landing),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed = derivedStatsApplied && preRepairBlocked && takeoff &&
                fuelDebited && docking && stationVisited && undock && landing &&
                loopCompleted && readinessRejected && coldRestore &&
                legacyFallback && exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            string result = passed
                ? "the repaired starter ship used live derived statistics, restored an active outbound flight exactly, docked at the orbital station, returned, landed and restored the completed Stage 1 loop"
                : $"derived={(derivedStatsApplied ? 1 : 0)}, blocked={(preRepairBlocked ? 1 : 0)}, takeoff={(takeoff ? 1 : 0)}, fuel={(fuelDebited ? 1 : 0)}, dock={(docking ? 1 : 0)}, visit={(stationVisited ? 1 : 0)}, undock={(undock ? 1 : 0)}, landing={(landing ? 1 : 0)}, loop={(loopCompleted ? 1 : 0)}, readiness={(readinessRejected ? 1 : 0)}, restore={(coldRestore ? 1 : 0)}, activeRoundTrip={(activeRoundTrip ? 1 : 0)}({activeMismatch}), legacy={(legacyFallback ? 1 : 0)}, roundTrip={(exactRoundTrip ? 1 : 0)}({mismatch}), log={(logWritten ? 1 : 0)}, integrity={diagnostics.IntegrityResult}";
            stopwatch.Stop();
            return new StageOneVoyageAcceptanceReport(
                passed,
                result,
                derivedStatsApplied,
                preRepairBlocked,
                takeoff,
                fuelDebited,
                docking,
                stationVisited,
                undock,
                landing,
                loopCompleted,
                readinessRejected,
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
                "TASK-112 Stage 1 voyage acceptance failed.",
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
            Path.Combine(directory, "logs", baseName + ".autosave.log")
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
