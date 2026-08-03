using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record BaseConstructionAcceptanceReport(
    bool Passed,
    string Result,
    int CatalogModules,
    int Categories,
    int PlacedModules,
    bool AnchorRule,
    bool Snapping,
    bool CollisionRejected,
    bool DisconnectedRejected,
    bool PowerGraph,
    bool Battery,
    bool Toggle,
    bool RemovalRefund,
    bool Limits,
    bool Stress500,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class BaseConstructionAcceptanceRunner
{
    public static async Task<BaseConstructionAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog contentCatalog,
        BaseConstructionCatalog constructionCatalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ArgumentNullException.ThrowIfNull(constructionCatalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            SaveDatabase.RegisterKnownInventoryDefinitions(
                contentCatalog.Items.Keys);
            BaseConstructionRuntime runtime = new(constructionCatalog);
            string anchorId = constructionCatalog.Modules.Values
                .Single(module => module.IsAnchor).ModuleId;
            string[] remaining = constructionCatalog.Modules.Keys
                .Where(moduleId => !string.Equals(
                    moduleId,
                    anchorId,
                    StringComparison.Ordinal))
                .OrderBy(moduleId => moduleId, StringComparer.Ordinal)
                .ToArray();

            BasePlacementResult firstRejected = runtime.TryPlace(
                remaining[0],
                0,
                0,
                0,
                out _,
                out _);
            bool anchorRule = firstRejected == BasePlacementResult.AnchorRequired;
            EnsurePlaced(runtime, anchorId, 0, 0, 0);
            BasePlacementResult collisionResult = runtime.TryPlace(
                remaining[0],
                0,
                0,
                0,
                out _,
                out _);
            BasePlacementResult disconnectedResult = runtime.TryPlace(
                remaining[0],
                20,
                20,
                0,
                out _,
                out _);
            bool collisionRejected =
                collisionResult == BasePlacementResult.Overlap;
            bool disconnectedRejected =
                disconnectedResult == BasePlacementResult.NotSnapped;

            for (int index = 0; index < remaining.Length; index++)
            {
                EnsurePlaced(runtime, remaining[index], index + 1, 0, index % 4);
            }

            bool snapping = runtime.ModuleCount ==
                BaseConstructionCatalog.ExpectedModuleCount &&
                runtime.Power.ConnectedComponents == 1;
            BasePowerNetworkSnapshot powered = runtime.Power;
            bool powerGraph = powered.Generation > powered.Consumption &&
                powered.PoweredConsumers == powered.EnabledConsumers &&
                powered.ConnectedComponents == 1;
            runtime.Tick(2.0);
            bool battery = powered.BatteryCapacity > 0.0 &&
                runtime.StoredEnergy > 0.0 &&
                runtime.StoredEnergy <= runtime.Power.BatteryCapacity;

            BaseModulePlacement solar = runtime.Placements.Single(placement =>
                string.Equals(
                    placement.ModuleId,
                    "module.solar_array",
                    StringComparison.Ordinal));
            bool toggledOff = runtime.TryToggle(solar.InstanceId, out _) &&
                !runtime.Placements.Single(placement =>
                    placement.InstanceId == solar.InstanceId).Enabled;
            bool toggledOn = runtime.TryToggle(solar.InstanceId, out _) &&
                runtime.Placements.Single(placement =>
                    placement.InstanceId == solar.InstanceId).Enabled;
            bool toggle = toggledOff && toggledOn;

            BaseModulePlacement middle = runtime.Placements.Single(placement =>
                placement.GridX == 4 && placement.GridZ == 0);
            bool middleRejected = !runtime.TryRemove(middle.InstanceId, out _);
            BaseModulePlacement end = runtime.Placements.Single(placement =>
                placement.GridX == remaining.Length && placement.GridZ == 0);
            int stockBefore = runtime.GetStock(end.ModuleId);
            bool endRemoved = runtime.TryRemove(end.InstanceId, out _) &&
                runtime.GetStock(end.ModuleId) == stockBefore + 1;
            bool removalRefund = middleRejected && endRemoved;
            EnsurePlaced(
                runtime,
                end.ModuleId,
                end.GridX,
                end.GridZ,
                end.RotationQuarterTurns);

            BaseConstructionLimits limitsDefinition = constructionCatalog.Limits;
            BaseConstructionRuntime stressRuntime = new(constructionCatalog);
            EnsurePlaced(stressRuntime, anchorId, 0, 0, 0);
            Queue<string> stressPalette = new(
                constructionCatalog.Modules.Values
                    .Where(module =>
                        !module.IsAnchor &&
                        module.InteractiveDevices == 0 &&
                        module.ActivePhysicsObjects == 0 &&
                        module.DynamicLights == 0 &&
                        module.PowerGeneration == 0.0 &&
                        module.PowerConsumption == 0.0 &&
                        module.BatteryCapacity == 0.0)
                    .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
                    .SelectMany(module => Enumerable.Repeat(
                        module.ModuleId,
                        module.StarterStock)));
            for (int gridX = 1;
                 gridX < limitsDefinition.MaximumModules;
                 gridX++)
            {
                if (stressPalette.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Construction catalog lacks stock for the 500-module stress path.");
                }

                EnsurePlaced(
                    stressRuntime,
                    stressPalette.Dequeue(),
                    gridX,
                    0,
                    gridX % 4);
            }

            if (stressPalette.Count == 0)
            {
                throw new InvalidOperationException(
                    "Construction catalog lacks spare stock for limit rejection.");
            }

            BasePlacementResult moduleLimitRejected = stressRuntime.TryPlace(
                stressPalette.Dequeue(),
                limitsDefinition.MaximumModules,
                0,
                0,
                out _,
                out _);
            bool stress500 = stressRuntime.ModuleCount ==
                    limitsDefinition.MaximumModules &&
                stressRuntime.Power.ConnectedComponents == 1 &&
                moduleLimitRejected == BasePlacementResult.LimitExceeded;

            BaseConstructionStockSaveData[] limitStock =
                constructionCatalog.Modules.Values
                    .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
                    .Select(module => new BaseConstructionStockSaveData(
                        module.ModuleId,
                        1))
                    .ToArray();
            BaseConstructionModuleSaveData[] limitModules = Enumerable.Range(
                    0,
                    limitsDefinition.MaximumInteractiveDevices)
                .Select(index => new BaseConstructionModuleSaveData(
                    $"base.limit.{index + 1:000000}",
                    index == 0 ? anchorId : "module.solar_array",
                    index,
                    0,
                    0,
                    Enabled: true))
                .ToArray();
            BaseConstructionRuntime limitRuntime = new(
                constructionCatalog,
                new BaseConstructionSaveData(
                    "base.limit.acceptance",
                    limitsDefinition.MaximumInteractiveDevices + 1,
                    0.0,
                    limitStock,
                    limitModules));
            BasePlacementResult limitRejected = limitRuntime.TryPlace(
                "module.solar_array",
                limitsDefinition.MaximumInteractiveDevices,
                0,
                0,
                out _,
                out _);
            bool limits = limitsDefinition.MaximumModules == 500 &&
                limitsDefinition.MaximumInteractiveDevices == 100 &&
                limitsDefinition.MaximumActivePhysicsObjects == 200 &&
                limitsDefinition.MaximumDynamicLights == 20 &&
                runtime.Power.Modules <= limitsDefinition.MaximumModules &&
                runtime.Power.InteractiveDevices <=
                    limitsDefinition.MaximumInteractiveDevices &&
                runtime.Power.ActivePhysicsObjects <=
                    limitsDefinition.MaximumActivePhysicsObjects &&
                runtime.Power.DynamicLights <=
                    limitsDefinition.MaximumDynamicLights &&
                limitRejected == BasePlacementResult.LimitExceeded &&
                stress500;

            StarterRepairSession session = new(repairRecipe);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 1.0,
                playerPositionY: 2.0,
                playerPositionZ: 3.0,
                baseConstruction: runtime.CreateSaveData());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            await autosave.FlushAsync(
                AutosaveTrigger.BaseChanged,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            BaseConstructionRuntime restored = new(
                constructionCatalog,
                loaded?.BaseConstruction);
            BaseConstructionSaveData expectedBase = runtime.CreateSaveData();
            BaseConstructionSaveData restoredBase = restored.CreateSaveData();
            bool coldRestore = loaded?.BaseConstruction is not null &&
                string.Equals(
                    expectedBase.BaseId,
                    restoredBase.BaseId,
                    StringComparison.Ordinal) &&
                expectedBase.NextSequence == restoredBase.NextSequence &&
                Math.Abs(expectedBase.StoredEnergy - restoredBase.StoredEnergy) <
                    0.000001 &&
                expectedBase.Stock.SequenceEqual(restoredBase.Stock) &&
                expectedBase.Modules.SequenceEqual(restoredBase.Modules);
            BaseConstructionRuntime legacy = new(
                constructionCatalog,
                saveData: null);
            bool legacyFallback = legacy.ModuleCount == 0 &&
                legacy.Power.Modules == 0 &&
                constructionCatalog.Modules.Values.All(definition =>
                    legacy.GetStock(definition.ModuleId) ==
                        definition.StarterStock);

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
                    nameof(AutosaveTrigger.BaseChanged),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            int categories = constructionCatalog.Modules.Values
                .Select(module => module.Category)
                .Distinct(StringComparer.Ordinal)
                .Count();
            bool passed =
                constructionCatalog.Modules.Count ==
                    BaseConstructionCatalog.ExpectedModuleCount &&
                categories == 17 &&
                anchorRule && snapping && collisionRejected &&
                disconnectedRejected && powerGraph && battery && toggle &&
                removalRefund && limits && stress500 && coldRestore && legacyFallback &&
                exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            List<string> failures = new();
            if (!anchorRule) failures.Add("anchor=0");
            if (!snapping) failures.Add("snapping=0");
            if (!collisionRejected) failures.Add("collision=0");
            if (!disconnectedRejected) failures.Add("disconnected=0");
            if (!powerGraph) failures.Add("power=0");
            if (!battery) failures.Add("battery=0");
            if (!toggle) failures.Add("toggle=0");
            if (!removalRefund) failures.Add("refund=0");
            if (!limits) failures.Add("limits=0");
            if (!stress500) failures.Add("stress500=0");
            if (!coldRestore) failures.Add("restore=0");
            if (!legacyFallback) failures.Add("legacy=0");
            if (!exactRoundTrip) failures.Add($"roundTrip=0({mismatch})");
            if (!logWritten) failures.Add("log=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add("writers=0");
            if (!integrityOk) failures.Add("integrity=0");
            string result = passed
                ? "fifty data-driven base modules across all PDF construction " +
                  "categories snapped into one persistent graph with collision " +
                  "rejection, an exact 500-module stress boundary, power, " +
                  "battery storage, device toggles and safe dismantle refund"
                : string.Join(", ", failures);
            stopwatch.Stop();
            return new BaseConstructionAcceptanceReport(
                passed,
                result,
                constructionCatalog.Modules.Count,
                categories,
                runtime.ModuleCount,
                anchorRule,
                snapping,
                collisionRejected,
                disconnectedRejected,
                powerGraph,
                battery,
                toggle,
                removalRefund,
                limits,
                stress500,
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
            return new BaseConstructionAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                constructionCatalog.Modules.Count,
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

    private static void EnsurePlaced(
        BaseConstructionRuntime runtime,
        string moduleId,
        int gridX,
        int gridZ,
        int rotation)
    {
        BasePlacementResult result = runtime.TryPlace(
            moduleId,
            gridX,
            gridZ,
            rotation,
            out _,
            out string message);
        if (result != BasePlacementResult.Placed)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        foreach (string path in new[]
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + ".backup",
            databasePath + ".backup-wal",
            databasePath + ".backup-shm",
            databasePath + ".autosave.log"
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
