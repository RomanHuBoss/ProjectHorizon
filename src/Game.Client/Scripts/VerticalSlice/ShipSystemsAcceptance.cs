using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record ShipSystemsAcceptanceReport(
    bool Passed,
    string Result,
    int ShipClasses,
    int Systems,
    int Modules,
    bool CatalogCoverage,
    bool ClassStats,
    bool InstallAll,
    bool SlotLimits,
    bool DuplicateRejected,
    bool DerivedStats,
    bool DamageLifecycle,
    bool RepairLifecycle,
    bool ModuleDisable,
    bool FlightReadiness,
    bool HyperspaceReadiness,
    bool FuelLifecycle,
    bool InventoryConservation,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ShipSystemsAcceptanceRunner
{
    public const string FuelDefinitionId = "chemical.high_energy_fuel";

    public static async Task<ShipSystemsAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog contentCatalog,
        ShipSystemsCatalog shipCatalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ArgumentNullException.ThrowIfNull(shipCatalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            bool catalogCoverage = HasExactCatalogCoverage(
                contentCatalog,
                shipCatalog);
            bool classStats = shipCatalog.Classes.Values.All(definition =>
                definition.BaseStats.Hull > 0.0 &&
                definition.BaseStats.FuelCapacity > 0.0 &&
                definition.BaseStats.MaxSpeed > 0.0 &&
                definition.BaseStats.TechnologySlots > 0);

            bool installAll = shipCatalog.Modules.Values.All(module =>
            {
                ShipSystemsRuntime isolated = new(shipCatalog);
                return isolated.TryInstall(module.ModuleId, out _) ==
                    ShipModuleInstallResult.Installed &&
                    isolated.IsInstalled(module.ModuleId);
            });

            ShipSystemsRuntime slotRuntime = new(shipCatalog);
            ShipModuleDefinition[] technologyModules = shipCatalog.Modules.Values
                .Where(module => string.Equals(
                    module.SlotType,
                    "Technology",
                    StringComparison.Ordinal))
                .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
                .ToArray();
            int technologyCapacity = shipCatalog.GetClass(
                shipCatalog.StarterClassId).BaseStats.TechnologySlots;
            bool slotsFilled = true;
            for (int index = 0; index < technologyCapacity; index++)
            {
                slotsFilled &= slotRuntime.TryInstall(
                    technologyModules[index].ModuleId,
                    out _) == ShipModuleInstallResult.Installed;
            }

            bool slotLimits = slotsFilled &&
                slotRuntime.TryInstall(
                    technologyModules[technologyCapacity].ModuleId,
                    out _) == ShipModuleInstallResult.SlotUnavailable;
            string duplicateModule = technologyModules[0].ModuleId;
            bool duplicateRejected = slotRuntime.TryInstall(
                duplicateModule,
                out _) == ShipModuleInstallResult.AlreadyInstalled;

            ShipSystemsRuntime runtime = new(shipCatalog);
            ShipEffectiveStats baseline = runtime.GetEffectiveStats();
            string[] selectedModules =
            {
                "module.ship.hull_reinforcement",
                "module.ship.shield_emitter",
                "module.ship.cargo_expander",
                "module.ship.hyperspace_core",
                "module.ship.mining_laser_head"
            };
            Dictionary<string, int> inventory = selectedModules.ToDictionary(
                id => id,
                _ => 1,
                StringComparer.Ordinal);
            bool inventoryConservation = true;
            foreach (string moduleId in selectedModules)
            {
                if (inventory[moduleId] <= 0 ||
                    runtime.TryInstall(moduleId, out _) !=
                        ShipModuleInstallResult.Installed)
                {
                    inventoryConservation = false;
                    continue;
                }

                inventory[moduleId]--;
            }

            ShipEffectiveStats upgraded = runtime.GetEffectiveStats();
            bool derivedStats =
                upgraded.Hull > baseline.Hull &&
                upgraded.Shield > baseline.Shield &&
                upgraded.CargoCapacity > baseline.CargoCapacity &&
                upgraded.HyperdriveRange > baseline.HyperdriveRange;
            bool flightBeforeDamage = runtime.FlightReady;
            bool hyperspaceBeforeDamage = runtime.HyperspaceReady;

            double shieldWithModule = upgraded.Shield;
            runtime.ApplyDamage(
                "ship.system.shield",
                1000.0,
                out _);
            double shieldDisabled = runtime.GetEffectiveStats().Shield;
            bool moduleDisable = shieldDisabled < shieldWithModule;

            string[] systemIds = shipCatalog.Systems.Keys
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            bool damageLifecycle = true;
            foreach (string systemId in systemIds)
            {
                ShipSystemMutationResult damage = runtime.ApplyDamage(
                    systemId,
                    1000.0,
                    out _);
                damageLifecycle &= damage is ShipSystemMutationResult.Applied or
                    ShipSystemMutationResult.AlreadyOffline;
                damageLifecycle &= runtime.GetSystemHealth(systemId) <= 0.0;
            }

            bool flightReadiness = flightBeforeDamage && !runtime.FlightReady;
            bool hyperspaceReadiness = hyperspaceBeforeDamage &&
                !runtime.HyperspaceReady;
            bool repairLifecycle = true;
            foreach (ShipSystemDefinition system in shipCatalog.Systems.Values)
            {
                int guard = 0;
                while (runtime.GetSystemHealth(system.SystemId) + 0.0001 <
                    runtime.GetSystemMaximumHealth(system.SystemId) &&
                    guard++ < 10)
                {
                    repairLifecycle &= runtime.Repair(
                        system.SystemId,
                        system.RepairPerUnit,
                        out _) == ShipSystemMutationResult.Applied;
                }

                repairLifecycle &= Math.Abs(
                    runtime.GetSystemHealth(system.SystemId) -
                    runtime.GetSystemMaximumHealth(system.SystemId)) < 0.001;
            }

            moduleDisable &= runtime.GetEffectiveStats().Shield >=
                shieldWithModule - 0.001;
            flightReadiness &= runtime.FlightReady;
            hyperspaceReadiness &= runtime.HyperspaceReady;

            double initialFuel = runtime.Fuel;
            bool consumedFuel = runtime.TryConsumeFuel(10.0, out _);
            double fuelAfterConsumption = runtime.Fuel;
            double restoredFuel = runtime.Refuel(10000.0);
            bool fuelLifecycle = consumedFuel &&
                Math.Abs(fuelAfterConsumption - (initialFuel - 10.0)) < 0.001 &&
                restoredFuel > 0.0 &&
                Math.Abs(
                    runtime.Fuel - runtime.GetEffectiveStats().FuelCapacity) <
                    0.001;

            string uninstallTarget = "module.ship.mining_laser_head";
            bool uninstalled = runtime.TryUninstall(
                uninstallTarget,
                out _) == ShipModuleUninstallResult.Uninstalled;
            if (uninstalled)
            {
                inventory[uninstallTarget]++;
            }

            inventoryConservation &= uninstalled &&
                inventory[uninstallTarget] == 1 &&
                selectedModules.Where(id => !string.Equals(
                    id,
                    uninstallTarget,
                    StringComparison.Ordinal)).All(id => inventory[id] == 0);

            runtime.ApplyDamage(
                "ship.system.landing",
                17.0,
                out _);
            StarterRepairSession session = new(repairRecipe);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 1.0,
                playerPositionY: 2.0,
                playerPositionZ: 3.0,
                shipSystems: runtime.CreateSaveData());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            await autosave.FlushAsync(
                AutosaveTrigger.ShipChanged,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            ShipSystemsRuntime restored = new(
                shipCatalog,
                loaded?.ShipSystems);
            bool coldRestore = loaded?.ShipSystems is not null &&
                string.Equals(
                    JsonSerializer.Serialize(runtime.CreateSaveData()),
                    JsonSerializer.Serialize(restored.CreateSaveData()),
                    StringComparison.Ordinal) &&
                restored.InstalledModuleCount == runtime.InstalledModuleCount &&
                restored.DisabledSystemCount == runtime.DisabledSystemCount;
            ShipSystemsRuntime legacy = new(shipCatalog, saveData: null);
            bool legacyFallback =
                legacy.ShipClassId == shipCatalog.StarterClassId &&
                legacy.InstalledModuleCount == 0 &&
                legacy.DisabledSystemCount == 0 &&
                Math.Abs(legacy.Fuel - 35.0) < 0.001;

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
                    nameof(AutosaveTrigger.ShipChanged),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed = shipCatalog.Classes.Count ==
                    ShipSystemsCatalog.ExpectedClassCount &&
                shipCatalog.Systems.Count ==
                    ShipSystemsCatalog.ExpectedSystemCount &&
                shipCatalog.Modules.Count ==
                    ShipSystemsCatalog.ExpectedModuleCount &&
                catalogCoverage && classStats && installAll && slotLimits &&
                duplicateRejected && derivedStats && damageLifecycle &&
                repairLifecycle && moduleDisable && flightReadiness &&
                hyperspaceReadiness && fuelLifecycle && inventoryConservation &&
                coldRestore && legacyFallback && exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            List<string> failures = new();
            if (!catalogCoverage) failures.Add("coverage=0");
            if (!classStats) failures.Add("classStats=0");
            if (!installAll) failures.Add("installAll=0");
            if (!slotLimits) failures.Add("slotLimits=0");
            if (!duplicateRejected) failures.Add("duplicate=0");
            if (!derivedStats) failures.Add("stats=0");
            if (!damageLifecycle) failures.Add("damage=0");
            if (!repairLifecycle) failures.Add("repair=0");
            if (!moduleDisable) failures.Add("disable=0");
            if (!flightReadiness) failures.Add("flightReady=0");
            if (!hyperspaceReadiness) failures.Add("hyperReady=0");
            if (!fuelLifecycle) failures.Add("fuel=0");
            if (!inventoryConservation) failures.Add("inventory=0");
            if (!coldRestore) failures.Add("restore=0");
            if (!legacyFallback) failures.Add("legacy=0");
            if (!exactRoundTrip) failures.Add($"roundTrip=0({mismatch})");
            if (!logWritten) failures.Add("log=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add("writers=0");
            if (!integrityOk) failures.Add("integrity=0");
            string result = passed
                ? "six ship classes, eighteen catalog modules and seven " +
                  "damageable systems enforced slot limits, derived stats, " +
                  "damage, repair, readiness, fuel and exact persistence"
                : string.Join(", ", failures);
            stopwatch.Stop();
            return new ShipSystemsAcceptanceReport(
                passed,
                result,
                shipCatalog.Classes.Count,
                shipCatalog.Systems.Count,
                shipCatalog.Modules.Count,
                catalogCoverage,
                classStats,
                installAll,
                slotLimits,
                duplicateRejected,
                derivedStats,
                damageLifecycle,
                repairLifecycle,
                moduleDisable,
                flightReadiness,
                hyperspaceReadiness,
                fuelLifecycle,
                inventoryConservation,
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
            return new ShipSystemsAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                shipCatalog.Classes.Count,
                shipCatalog.Systems.Count,
                shipCatalog.Modules.Count,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0,
                    "unknown",
                    false,
                    0,
                    0,
                    "error",
                    0,
                    0,
                    0,
                    0,
                    0,
                    0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            DeleteTestArtifacts(databasePath);
        }
    }

    private static bool HasExactCatalogCoverage(
        GameContentCatalog contentCatalog,
        ShipSystemsCatalog shipCatalog)
    {
        string[] recipeOutputs = contentCatalog.Recipes.Values
            .Where(recipe => string.Equals(
                recipe.Category,
                "ShipModule",
                StringComparison.Ordinal))
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] modules = shipCatalog.Modules.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return recipeOutputs.Length == ShipSystemsCatalog.ExpectedModuleCount &&
            recipeOutputs.SequenceEqual(modules, StringComparer.Ordinal) &&
            contentCatalog.Items.ContainsKey(FuelDefinitionId) &&
            shipCatalog.Systems.Values.All(system =>
                contentCatalog.Items.ContainsKey(system.RepairDefinitionId));
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        string[] suffixes =
        {
            string.Empty,
            "-wal",
            "-shm",
            ".bak",
            ".bak-wal",
            ".bak-shm",
            ".autosave.log"
        };
        foreach (string suffix in suffixes)
        {
            string path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
