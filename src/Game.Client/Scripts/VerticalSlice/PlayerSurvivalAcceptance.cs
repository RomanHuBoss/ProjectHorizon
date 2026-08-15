using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record PlayerSurvivalAcceptanceReport(
    bool Passed,
    string Result,
    int SuitModules,
    int MultitoolModules,
    int Consumables,
    int Environments,
    bool CatalogCoverage,
    bool ProtectionRuntime,
    bool HazardRuntime,
    bool OxygenRuntime,
    bool MovementResources,
    bool MultitoolRuntime,
    bool DamageRuntime,
    bool ConsumablesRuntime,
    bool EquipmentSlots,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool RepeatedSave,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class PlayerSurvivalAcceptanceRunner
{
    public static async Task<PlayerSurvivalAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        PlayerSurvivalCatalog catalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        SaveDatabaseDiagnostics emptyDiagnostics = new(
            SaveDatabase.CurrentSchemaVersion,
            "unknown",
            false,
            0,
            0,
            "not-run",
            0,
            0,
            0,
            0,
            0,
            0);
        string mismatch = "not-run";
        try
        {
            bool catalogCoverage =
                catalog.SuitModules.Count == PlayerSurvivalCatalog.ExpectedSuitModuleCount &&
                catalog.MultitoolModules.Count == PlayerSurvivalCatalog.ExpectedMultitoolModuleCount &&
                catalog.Consumables.Count == PlayerSurvivalCatalog.ExpectedConsumableCount &&
                catalog.Environments.Count == PlayerSurvivalCatalog.ExpectedEnvironmentCount;

            PlayerSurvivalRuntime protectedRuntime = new(catalog);
            bool equipmentSlots = true;
            foreach (string moduleId in catalog.SuitModules.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                equipmentSlots &= protectedRuntime.InstallSuitModule(moduleId, out _) ==
                    PlayerEquipmentMutationResult.Applied;
            }
            foreach (string moduleId in catalog.MultitoolModules.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                equipmentSlots &= protectedRuntime.InstallMultitoolModule(moduleId, out _) ==
                    PlayerEquipmentMutationResult.Applied;
            }
            string firstSuit = catalog.SuitModules.Keys.OrderBy(id => id, StringComparer.Ordinal).First();
            equipmentSlots &= protectedRuntime.InstallSuitModule(firstSuit, out _) ==
                PlayerEquipmentMutationResult.AlreadyInstalled;

            PlayerSurvivalEffectiveStats protectedStats = protectedRuntime.GetEffectiveStats();
            bool protectionRuntime = protectedStats.TemperatureProtection > catalog.BaseStats.TemperatureProtection &&
                protectedStats.RadiationProtection > catalog.BaseStats.RadiationProtection &&
                protectedStats.ToxicProtection > catalog.BaseStats.ToxicProtection &&
                protectedStats.MaximumHazardProtection > catalog.BaseStats.HazardProtection;

            PlayerEnvironmentDefinition toxic = catalog.GetEnvironment("toxic");
            PlayerSurvivalRuntime unprotected = new(catalog);
            PlayerSurvivalRuntime toxicProtected = new(catalog);
            toxicProtected.InstallSuitModule("module.suit.toxic_filter", out _);
            for (int index = 0; index < 60; index++)
            {
                unprotected.Tick(toxic, 0.5, activeOnFoot: true);
                toxicProtected.Tick(toxic, 0.5, activeOnFoot: true);
            }
            bool hazardRuntime = toxicProtected.HazardProtection > unprotected.HazardProtection &&
                toxicProtected.Health >= unprotected.Health;

            PlayerSurvivalRuntime oxygenRuntimeState = new(catalog);
            PlayerEnvironmentDefinition barren = catalog.GetEnvironment("barren");
            for (int index = 0; index < 30; index++)
            {
                oxygenRuntimeState.Tick(barren, 1.0, activeOnFoot: true);
            }
            double oxygenBeforeConsumable = oxygenRuntimeState.Oxygen;
            bool oxygenRuntime = oxygenBeforeConsumable < catalog.BaseStats.Oxygen &&
                oxygenRuntimeState.UseConsumable("consumable.oxygen_canister", out _) ==
                    PlayerEquipmentMutationResult.Applied &&
                oxygenRuntimeState.Oxygen > oxygenBeforeConsumable;

            PlayerSurvivalRuntime movement = new(catalog);
            double staminaBefore = movement.Stamina;
            double jetpackBefore = movement.JetpackEnergy;
            bool movementResources = movement.TryConsumeStamina(1.0) &&
                movement.TryConsumeJetpackEnergy(1.0) &&
                movement.Stamina < staminaBefore &&
                movement.JetpackEnergy < jetpackBefore;
            movement.RecoverMovementResources(2.0, sprinting: false, jetpacking: false);
            movementResources &= movement.Stamina > staminaBefore - 17.0 &&
                movement.JetpackEnergy > jetpackBefore - 24.0;

            PlayerSurvivalRuntime multitoolBase = new(catalog);
            multitoolBase.TryUseMultitool(PlayerMultitoolFunction.Scanner, out double baseScannerCost);
            PlayerSurvivalRuntime multitoolUpgraded = new(catalog);
            multitoolUpgraded.InstallMultitoolModule("tool.scanner_upgrade", out _);
            double multitoolBefore = multitoolUpgraded.MultitoolEnergy;
            bool multitoolRuntime = multitoolUpgraded.TryUseMultitool(
                    PlayerMultitoolFunction.Scanner,
                    out double upgradedScannerCost) &&
                upgradedScannerCost < baseScannerCost &&
                multitoolUpgraded.MultitoolEnergy < multitoolBefore &&
                multitoolUpgraded.GetMultitoolEffectiveness(PlayerMultitoolFunction.Scanner) > 1.0;

            PlayerSurvivalRuntime damaged = new(catalog);
            damaged.ApplyDamage(85.0);
            bool damageRuntime = damaged.Shield < catalog.BaseStats.Shield &&
                damaged.Health < catalog.BaseStats.Health;
            double healthBeforeMed = damaged.Health;
            bool consumablesRuntime = damaged.UseConsumable("consumable.med_gel", out _) ==
                    PlayerEquipmentMutationResult.Applied &&
                damaged.Health > healthBeforeMed;
            double shieldBeforeFoam = damaged.Shield;
            consumablesRuntime &= damaged.UseConsumable("consumable.repair_foam", out _) ==
                    PlayerEquipmentMutationResult.Applied &&
                damaged.Shield > shieldBeforeFoam;

            PlayerSurvivalSaveData persisted = protectedRuntime.CreateSaveData();
            string? directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
            string legacyAutosaveLog = Path.Combine(
                directory ?? ".",
                Path.GetFileNameWithoutExtension(databasePath) + ".autosave.log");
            if (File.Exists(legacyAutosaveLog))
            {
                File.Delete(legacyAutosaveLog);
            }

            StarterRepairSession session = new(
                repairRecipe,
                static _ => true,
                Array.Empty<CraftingRecipeDefinition>());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(25));
            await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot first = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                0.0,
                1.0,
                0.0,
                playerSurvival: persisted);
            await autosave.FlushAsync(
                AutosaveTrigger.PlayerChanged,
                first,
                cancellationToken).ConfigureAwait(false);

            PlayerSurvivalRuntime changed = new(catalog, persisted);
            changed.ApplyDamage(17.0);
            changed.TryUseMultitool(PlayerMultitoolFunction.Mining, out _);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 2,
                session,
                0.0,
                1.0,
                0.0,
                playerSurvival: changed.CreateSaveData());
            await autosave.FlushAsync(
                AutosaveTrigger.PlayerChanged,
                expected,
                cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out mismatch);
            PlayerSurvivalRuntime restored = new(catalog, loaded?.PlayerSurvival);
            bool coldRestore = loaded?.PlayerSurvival is not null && exactRoundTrip &&
                string.Equals(
                    JsonSerializer.Serialize(changed.CreateSaveData()),
                    JsonSerializer.Serialize(restored.CreateSaveData()),
                    StringComparison.Ordinal);
            bool repeatedSave = loaded?.Revision == 2 &&
                loaded.PlayerSurvival is not null;

            PlayerSurvivalRuntime legacy = new(catalog, saveData: null);
            bool legacyFallback = legacy.InstalledSuitModules.Count == 0 &&
                legacy.InstalledMultitoolModules.Count == 0 &&
                Math.Abs(legacy.Health - catalog.BaseStats.Health) < 0.001 &&
                Math.Abs(legacy.Oxygen - catalog.BaseStats.Oxygen) < 0.001;

            SaveDatabaseDiagnostics diagnostics = await database.ReadDiagnosticsAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            string autosaveLog = autosave.AutosaveLogPath;
            bool logWritten = File.Exists(autosaveLog) &&
                File.ReadAllText(autosaveLog).Contains(
                    "PlayerChanged",
                    StringComparison.Ordinal);

            bool passed = catalogCoverage && protectionRuntime && hazardRuntime &&
                oxygenRuntime && movementResources && multitoolRuntime &&
                damageRuntime && consumablesRuntime && equipmentSlots &&
                coldRestore && legacyFallback && exactRoundTrip && repeatedSave &&
                logWritten && diagnostics.MaximumConcurrentWriters <= 1 &&
                string.Equals(diagnostics.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase);
            stopwatch.Stop();
            string result = passed
                ? "exosuit protection, hazards, oxygen, movement resources, multitool energy, damage, consumables and repeated persistence completed exactly"
                : $"player survival acceptance failed; mismatch={mismatch}";
            return new PlayerSurvivalAcceptanceReport(
                passed,
                result,
                catalog.SuitModules.Count,
                catalog.MultitoolModules.Count,
                catalog.Consumables.Count,
                catalog.Environments.Count,
                catalogCoverage,
                protectionRuntime,
                hazardRuntime,
                oxygenRuntime,
                movementResources,
                multitoolRuntime,
                damageRuntime,
                consumablesRuntime,
                equipmentSlots,
                coldRestore,
                legacyFallback,
                exactRoundTrip,
                repeatedSave,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new PlayerSurvivalAcceptanceReport(
                false,
                exception.Message,
                catalog.SuitModules.Count,
                catalog.MultitoolModules.Count,
                catalog.Consumables.Count,
                catalog.Environments.Count,
                false, false, false, false, false, false, false, false, false,
                false, false, false, false, false,
                emptyDiagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
