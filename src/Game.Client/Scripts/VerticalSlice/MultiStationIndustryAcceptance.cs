using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record MultiStationIndustryAcceptanceReport(
    bool Passed,
    string Result,
    int PhysicalStations,
    int RuntimeRecipes,
    bool WrongStationRejected,
    bool RepeatableProcess,
    bool ChainedProduction,
    bool EnergyRecharge,
    bool PropertiesPersisted,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class MultiStationIndustryAcceptanceRunner
{
    private static readonly string[] RecipeIds =
    {
        "recipe.refining.refined_ferrite",
        "recipe.refining.purified_water",
        "recipe.chemistry.paraffinium_fraction",
        "recipe.chemistry.paraffinium_lubricant",
        "recipe.chemistry.raw_compotium_solution",
        "recipe.chemistry.compotium_concentrate"
    };

    private static readonly string[] StationIds =
    {
        "station.smelter",
        "station.refinery",
        "station.distillation_column",
        "station.chemical_processor"
    };

    public static async Task<MultiStationIndustryAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
        Stopwatch stopwatch = Stopwatch.StartNew();
        SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);
        try
        {
            DeleteTestArtifacts(databasePath);
            CraftingRecipeDefinition repairRecipe = catalog.GetRecipe(
                StarterRepairContentIds.RecipeId);
            CraftingRecipeDefinition[] runtimeRecipes = catalog.Recipes.Values
                .Where(recipe => recipe.RuntimeEnabled &&
                    string.Equals(
                        recipe.Application.Type,
                        "StoreOutputs",
                        StringComparison.Ordinal))
                .OrderBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
                .ToArray();
            CraftingRecipeDefinition[] selectedRecipes = RecipeIds
                .Select(catalog.GetRecipe)
                .ToArray();
            if (selectedRecipes.Any(recipe => !recipe.RuntimeEnabled))
            {
                throw new InvalidOperationException(
                    "TASK-096 acceptance recipe is not runtime-enabled.");
            }

            StarterRepairSession session = new(
                repairRecipe,
                static _ => true,
                runtimeRecipes);
            session.GrantInventory(
                repairRecipe.Inputs[0].DefinitionId,
                repairRecipe.Inputs[0].Quantity);
            if (session.TryRepair(out string repairResult) !=
                StarterRepairResult.Repaired)
            {
                throw new InvalidOperationException(repairResult);
            }

            ProductionNetworkRuntime network = ProductionNetworkRuntime.Create(
                catalog.Stations,
                runtimeRecipes.ToDictionary(
                    recipe => recipe.RecipeId,
                    StringComparer.Ordinal),
                StationIds,
                session.AvailableInventory,
                static _ => true);
            Grant(session, network, "resource.ferric_ore", 2);
            Grant(session, network, "resource.ice_water", 2);
            Grant(session, network, "resource.paraffinium", 2);
            Grant(session, network, "resource.raw_compotium", 2);
            Grant(session, network, "resource.acidic_brine", 2);
            Grant(session, network, "resource.catalytic_dust", 1);

            ProductionQueueCommandReport wrongStation =
                network.GetQueue("station.smelter").Enqueue(
                    "recipe.chemistry.raw_compotium_solution",
                    ItemPropertyRuntime.CreateNominalEnvironment(
                        catalog.GetRecipe(
                            "recipe.chemistry.raw_compotium_solution")),
                    requestedBatches: 1);
            bool wrongStationRejected =
                wrongStation.Result ==
                ProductionQueueCommandResult.ValidationFailed &&
                wrongStation.ValidationResult == IndustryProcessResult.WrongStation;

            double recharged = 0.0;
            ExecuteRecipe(
                catalog.GetRecipe("recipe.refining.refined_ferrite"),
                session,
                network,
                ref recharged);
            ExecuteRecipe(
                catalog.GetRecipe("recipe.refining.purified_water"),
                session,
                network,
                ref recharged);
            ExecuteRecipe(
                catalog.GetRecipe("recipe.chemistry.paraffinium_fraction"),
                session,
                network,
                ref recharged);
            ExecuteRecipe(
                catalog.GetRecipe("recipe.chemistry.raw_compotium_solution"),
                session,
                network,
                ref recharged);
            ExecuteRecipe(
                catalog.GetRecipe("recipe.chemistry.raw_compotium_solution"),
                session,
                network,
                ref recharged);
            bool repeatableProcess = session.GetCraftedQuantity(
                "chemical.raw_compotium_solution") == 2;
            ExecuteRecipe(
                catalog.GetRecipe("recipe.chemistry.paraffinium_lubricant"),
                session,
                network,
                ref recharged);
            ExecuteRecipe(
                catalog.GetRecipe("recipe.chemistry.compotium_concentrate"),
                session,
                network,
                ref recharged);

            bool chainedProduction =
                session.GetCraftedQuantity(
                    "chemical.paraffinium_lubricant") == 1 &&
                session.GetCraftedQuantity(
                    "chemical.compotium_concentrate") == 1 &&
                session.GetAvailableQuantity(
                    "chemical.raw_compotium_solution") == 0 &&
                session.GetAvailableQuantity("material.purified_water") == 0;
            bool energyRecharge = recharged > 0.0;
            IndustryItemProperties expectedProperties =
                session.GetItemProperties("chemical.compotium_concentrate");

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 6.0,
                technologyProgress: null,
                productionQueue: null,
                productionQueueNetwork: network.CreateSaveData());
            await autosave.FlushAsync(
                AutosaveTrigger.QuestCompleted,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            StarterRepairSession restoredSession =
                StarterRepairSession.FromSnapshot(
                    loaded,
                    new Dictionary<string, ResourceNodeBinding>(
                        StringComparer.Ordinal),
                    repairRecipe,
                    static _ => true,
                    runtimeRecipes);
            ProductionNetworkRuntime restoredNetwork =
                ProductionNetworkRuntime.Create(
                    catalog.Stations,
                    runtimeRecipes.ToDictionary(
                        recipe => recipe.RecipeId,
                        StringComparer.Ordinal),
                    StationIds,
                    restoredSession.AvailableInventory,
                    static _ => true,
                    loaded?.ProductionQueueNetwork,
                    loaded?.ProductionQueue);
            IndustryItemProperties restoredProperties =
                restoredSession.GetItemProperties(
                    "chemical.compotium_concentrate");
            bool propertiesPersisted =
                restoredProperties == expectedProperties &&
                restoredNetwork.StationIds.Count == StationIds.Length;

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
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            bool passed =
                selectedRecipes.Length == RecipeIds.Length &&
                network.StationIds.Count == StationIds.Length &&
                wrongStationRejected &&
                repeatableProcess &&
                chainedProduction &&
                energyRecharge &&
                propertiesPersisted &&
                exactRoundTrip &&
                logWritten &&
                autosave.CompletedBatches == 1 &&
                autosave.FailedBatches == 0 &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase);

            List<string> failures = new();
            if (!wrongStationRejected) failures.Add("wrongStation=0");
            if (!repeatableProcess) failures.Add("repeatable=0");
            if (!chainedProduction) failures.Add("chain=0");
            if (!energyRecharge) failures.Add("recharge=0");
            if (!propertiesPersisted) failures.Add("properties=0");
            if (!exactRoundTrip) failures.Add($"roundTrip={mismatch}");
            if (!logWritten) failures.Add("log=0");
            stopwatch.Stop();
            return new MultiStationIndustryAcceptanceReport(
                passed,
                passed
                    ? "four physical production station types executed a repeatable Paraffinium and Compotium starter chain with shared inventory, rechargeable energy and exact network persistence"
                    : "multi-station industry criteria failed: " +
                      string.Join(", ", failures),
                network.StationIds.Count,
                selectedRecipes.Length,
                wrongStationRejected,
                repeatableProcess,
                chainedProduction,
                energyRecharge,
                propertiesPersisted,
                exactRoundTrip,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new MultiStationIndustryAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                0,
                RecipeIds.Length,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void ExecuteRecipe(
        CraftingRecipeDefinition recipe,
        StarterRepairSession session,
        ProductionNetworkRuntime network,
        ref double recharged)
    {
        ProductionQueueRuntime queue = network.GetQueue(recipe.RequiredStation);
        if (queue.EnergyRemaining + 0.000001 < recipe.EnergyCost)
        {
            recharged += queue.RechargeEnergy(
                queue.EnergyCapacity - queue.EnergyRemaining);
        }

        ProductionQueueCommandReport enqueued = queue.Enqueue(
            recipe.RecipeId,
            ItemPropertyRuntime.CreateNominalEnvironment(recipe),
            requestedBatches: 1);
        if (enqueued.Result != ProductionQueueCommandResult.Enqueued)
        {
            throw new InvalidOperationException(enqueued.ResultText);
        }

        IReadOnlyList<CraftingStackDefinition> reservations = recipe.Inputs
            .Concat(recipe.Catalysts.Select(catalyst =>
                new CraftingStackDefinition(
                    catalyst.DefinitionId,
                    catalyst.Quantity)))
            .ToArray();
        foreach (CraftingStackDefinition stack in reservations)
        {
            if (!session.TryConsumeInventory(
                    stack.DefinitionId,
                    stack.Quantity,
                    out string sessionResult))
            {
                throw new InvalidOperationException(sessionResult);
            }

            if (!network.TryConsumeInventoryAllExcept(
                    queue.StationId,
                    stack.DefinitionId,
                    stack.Quantity,
                    out string networkResult))
            {
                throw new InvalidOperationException(networkResult);
            }
        }

        ProductionQueueAdvanceReport advance = queue.Advance(
            recipe.CraftTimeSeconds + 0.001);
        IndustryProcessExecutionReport process = advance.CompletedProcesses
            .Single();
        IndustryItemProperties outputProperties =
            ItemPropertyRuntime.CreateOutputProperties(
                recipe,
                process.ProcessSequence,
                ItemPropertyRuntime.CreateNominalEnvironment(recipe));
        foreach (CraftingStackDefinition catalyst in process.RetainedCatalysts)
        {
            session.GrantInventory(catalyst.DefinitionId, catalyst.Quantity);
            network.AddInventoryAllExcept(
                queue.StationId,
                catalyst.DefinitionId,
                catalyst.Quantity);
        }

        foreach (CraftingStackDefinition output in process.Outputs)
        {
            session.GrantInventory(
                output.DefinitionId,
                output.Quantity,
                outputProperties);
            network.AddInventoryAllExcept(
                queue.StationId,
                output.DefinitionId,
                output.Quantity);
        }

        foreach (CraftingStackDefinition byproduct in process.Byproducts)
        {
            session.GrantInventory(
                byproduct.DefinitionId,
                byproduct.Quantity);
            network.AddInventoryAllExcept(
                queue.StationId,
                byproduct.DefinitionId,
                byproduct.Quantity);
        }
    }

    private static void Grant(
        StarterRepairSession session,
        ProductionNetworkRuntime network,
        string definitionId,
        int quantity)
    {
        session.GrantInventory(definitionId, quantity);
        network.AddInventoryAll(definitionId, quantity);
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        foreach (string path in Directory.EnumerateFiles(
            directory,
            $"{baseName}*",
            SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }

        string logPath = Path.Combine(
            directory,
            "logs",
            $"{baseName}.autosave.log");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
    }
}
