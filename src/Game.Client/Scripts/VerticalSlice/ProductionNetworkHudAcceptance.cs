using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record ProductionNetworkHudAcceptanceReport(
    bool Passed,
    string Result,
    int PhysicalStations,
    bool AggregateCounts,
    bool AggregateEnergy,
    bool SimultaneousRunning,
    bool PauseResume,
    bool Cancel,
    bool Completion,
    bool EnergyRecharge,
    bool ColdRestore,
    bool LegacyFallback,
    bool FalseUnavailable,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ProductionNetworkHudAcceptanceRunner
{
    private static readonly string[] StationIds =
    {
        "station.portable_fabricator",
        "station.smelter",
        "station.refinery",
        "station.distillation_column",
        "station.chemical_processor"
    };

    public static async Task<ProductionNetworkHudAcceptanceReport> RunAsync(
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
            IReadOnlyDictionary<string, CraftingRecipeDefinition> recipeMap =
                runtimeRecipes.ToDictionary(
                    recipe => recipe.RecipeId,
                    StringComparer.Ordinal);
            StarterRepairSession session = CreateRepairedSession(
                repairRecipe,
                runtimeRecipes);
            ProductionNetworkRuntime network = ProductionNetworkRuntime.Create(
                catalog.Stations,
                recipeMap,
                StationIds,
                session.AvailableInventory,
                static _ => true);

            ProductionNetworkHudSnapshot emptyProjection =
                ProductionNetworkHudModel.Build(network);
            string emptyText =
                ProductionNetworkHudModel.FormatAggregate(emptyProjection);
            bool falseUnavailable =
                emptyProjection.IsAvailable &&
                emptyProjection.Stations == StationIds.Length &&
                emptyProjection.Jobs == 0 &&
                !emptyText.Contains("unavailable", StringComparison.OrdinalIgnoreCase) &&
                !emptyText.Contains(
                    "Production queue: unavailable",
                    StringComparison.Ordinal);

            CraftingRecipeDefinition smelterRecipe = catalog.GetRecipe(
                "recipe.refining.refined_ferrite");
            CraftingRecipeDefinition refineryRecipe = catalog.GetRecipe(
                "recipe.refining.purified_water");
            Grant(session, network, "resource.ferric_ore", 8);
            Grant(session, network, "resource.ice_water", 2);

            List<ProductionQueueCommandReport> smelterJobs = new();
            for (int index = 0; index < 4; index++)
            {
                smelterJobs.Add(EnqueueAndReserve(
                    smelterRecipe,
                    session,
                    network));
            }

            ProductionQueueCommandReport firstRefineryJob = EnqueueAndReserve(
                refineryRecipe,
                session,
                network);
            ProductionNetworkHudSnapshot initial =
                ProductionNetworkHudModel.Build(network);
            ProductionNetworkHudStationRow smelterInitial = initial.StationRows
                .Single(row => string.Equals(
                    row.StationId,
                    "station.smelter",
                    StringComparison.Ordinal));
            ProductionNetworkHudStationRow refineryInitial = initial.StationRows
                .Single(row => string.Equals(
                    row.StationId,
                    "station.refinery",
                    StringComparison.Ordinal));
            bool aggregateCounts =
                initial.Stations == StationIds.Length &&
                initial.Jobs == 5 &&
                initial.RunningJobs == 3 &&
                initial.QueuedJobs == 2 &&
                initial.PausedJobs == 0 &&
                initial.Jobs == initial.StationRows.Sum(row => row.Jobs) &&
                initial.RunningJobs == initial.StationRows.Sum(
                    row => row.RunningJobs) &&
                initial.QueuedJobs == initial.StationRows.Sum(
                    row => row.QueuedJobs) &&
                initial.PausedJobs == initial.StationRows.Sum(
                    row => row.PausedJobs);
            bool aggregateEnergy =
                NearlyEqual(
                    initial.EnergyRemaining,
                    initial.StationRows.Sum(row => row.EnergyRemaining)) &&
                NearlyEqual(
                    initial.EnergyCapacity,
                    initial.StationRows.Sum(row => row.EnergyCapacity)) &&
                initial.EnergyRemaining < initial.EnergyCapacity;
            bool simultaneousRunning =
                smelterInitial.RunningJobs == 2 &&
                refineryInitial.RunningJobs == 1 &&
                initial.StationRows.Count(row => row.RunningJobs > 0) == 2;

            ProductionQueueRuntime smelter = network.GetQueue(
                "station.smelter");
            ProductionQueueCommandReport pause = smelter.Pause(
                smelterJobs[0].JobId);
            ProductionNetworkHudSnapshot paused =
                ProductionNetworkHudModel.Build(network);
            bool pauseState =
                pause.Result == ProductionQueueCommandResult.Paused &&
                paused.RunningJobs == 3 &&
                paused.QueuedJobs == 1 &&
                paused.PausedJobs == 1;
            ProductionQueueCommandReport resume = smelter.Resume(
                smelterJobs[0].JobId);
            ProductionNetworkHudSnapshot resumed =
                ProductionNetworkHudModel.Build(network);
            bool resumeState =
                resume.Result == ProductionQueueCommandResult.Resumed &&
                resumed.RunningJobs == 3 &&
                resumed.QueuedJobs == 2 &&
                resumed.PausedJobs == 0;
            ProductionQueueCommandReport secondPause = smelter.Pause(
                smelterJobs[1].JobId);
            ProductionNetworkHudSnapshot secondPaused =
                ProductionNetworkHudModel.Build(network);
            bool pauseResume =
                pauseState &&
                resumeState &&
                secondPause.Result == ProductionQueueCommandResult.Paused &&
                secondPaused.RunningJobs == 3 &&
                secondPaused.QueuedJobs == 1 &&
                secondPaused.PausedJobs == 1;

            int ferriteBeforeCancel = session.GetAvailableQuantity(
                "resource.ferric_ore");
            double smelterEnergyBeforeCancel = smelter.EnergyRemaining;
            ProductionQueueCommandReport cancel = smelter.Cancel(
                smelterJobs[0].JobId);
            ApplyRefund(session, network, smelter, cancel);
            bool cancelExact =
                cancel.Result == ProductionQueueCommandResult.Cancelled &&
                cancel.RefundedInputs.Count == 1 &&
                cancel.RefundedInputs[0].DefinitionId ==
                    "resource.ferric_ore" &&
                cancel.RefundedInputs[0].Quantity == 2 &&
                NearlyEqual(cancel.RefundedEnergy, smelterRecipe.EnergyCost) &&
                session.GetAvailableQuantity("resource.ferric_ore") ==
                    ferriteBeforeCancel + 2 &&
                NearlyEqual(
                    smelter.EnergyRemaining,
                    smelterEnergyBeforeCancel + smelterRecipe.EnergyCost);
            ProductionQueueCommandReport replacementJob = EnqueueAndReserve(
                smelterRecipe,
                session,
                network);
            ProductionNetworkHudSnapshot afterReplacement =
                ProductionNetworkHudModel.Build(network);
            bool cancelTransition =
                cancelExact &&
                replacementJob.Result == ProductionQueueCommandResult.Enqueued &&
                afterReplacement.RunningJobs == 3 &&
                afterReplacement.QueuedJobs == 1 &&
                afterReplacement.PausedJobs == 1;

            ProductionQueueRuntime refinery = network.GetQueue(
                "station.refinery");
            int jobsBeforeCompletion = network.TotalJobs;
            ProductionQueueAdvanceReport completionAdvance = refinery.Advance(
                refineryRecipe.CraftTimeSeconds + 0.001);
            IndustryProcessExecutionReport completedProcess =
                completionAdvance.CompletedProcesses.Single();
            ApplyCompletion(
                refineryRecipe,
                completedProcess,
                session,
                network,
                refinery);
            ProductionNetworkHudSnapshot afterCompletion =
                ProductionNetworkHudModel.Build(network);
            bool completion =
                firstRefineryJob.Result == ProductionQueueCommandResult.Enqueued &&
                network.TotalJobs == jobsBeforeCompletion - 1 &&
                afterCompletion.RunningJobs == 2 &&
                completedProcess.Outputs.Count > 0;

            Grant(session, network, "resource.ice_water", 2);
            ProductionQueueCommandReport replacementRefineryJob =
                EnqueueAndReserve(refineryRecipe, session, network);
            double energyBeforeRecharge =
                ProductionNetworkHudModel.Build(network).EnergyRemaining;
            double restoredEnergy = network.RechargeAll(
                elapsedSeconds: 3.0,
                fullRechargeSeconds: 60.0);
            ProductionNetworkHudSnapshot afterRecharge =
                ProductionNetworkHudModel.Build(network);
            bool energyRecharge =
                replacementRefineryJob.Result ==
                    ProductionQueueCommandResult.Enqueued &&
                restoredEnergy > 0.0 &&
                afterRecharge.EnergyRemaining > energyBeforeRecharge &&
                afterRecharge.EnergyRemaining <=
                    afterRecharge.EnergyCapacity + 0.000001;

            smelter.Advance(1.0);
            refinery.Advance(1.0);
            ProductionNetworkHudSnapshot beforeSave =
                ProductionNetworkHudModel.Build(network);
            ProductionQueueNetworkSaveData beforeSaveData =
                network.CreateSaveData();
            Dictionary<string, (ProductionQueueJobStatus Status, double Elapsed)>
                savedJobs = beforeSaveData.Stations
                    .SelectMany(queue => queue.Jobs)
                    .ToDictionary(
                        job => job.JobId + "@" + job.RecipeId,
                        job => (job.Status, job.ElapsedSeconds),
                        StringComparer.Ordinal);

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
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
                productionQueueNetwork: beforeSaveData);
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
                    recipeMap,
                    StationIds,
                    restoredSession.AvailableInventory,
                    static _ => true,
                    loaded?.ProductionQueueNetwork,
                    loaded?.ProductionQueue);
            ProductionNetworkHudSnapshot restoredProjection =
                ProductionNetworkHudModel.Build(restoredNetwork);
            ProductionQueueNetworkSaveData restoredSaveData =
                restoredNetwork.CreateSaveData();
            Dictionary<string, (ProductionQueueJobStatus Status, double Elapsed)>
                restoredJobs = restoredSaveData.Stations
                    .SelectMany(queue => queue.Jobs)
                    .ToDictionary(
                        job => job.JobId + "@" + job.RecipeId,
                        job => (job.Status, job.ElapsedSeconds),
                        StringComparer.Ordinal);
            bool coldRestore =
                beforeSave.Stations == restoredProjection.Stations &&
                beforeSave.Jobs == restoredProjection.Jobs &&
                beforeSave.RunningJobs == restoredProjection.RunningJobs &&
                beforeSave.QueuedJobs == restoredProjection.QueuedJobs &&
                beforeSave.PausedJobs == restoredProjection.PausedJobs &&
                NearlyEqual(
                    beforeSave.EnergyRemaining,
                    restoredProjection.EnergyRemaining) &&
                NearlyEqual(
                    beforeSave.EnergyCapacity,
                    restoredProjection.EnergyCapacity) &&
                JobsEqual(savedJobs, restoredJobs);

            StarterRepairSession legacySession = CreateRepairedSession(
                repairRecipe,
                runtimeRecipes);
            ProductionNetworkRuntime legacySource =
                ProductionNetworkRuntime.Create(
                    catalog.Stations,
                    recipeMap,
                    StationIds,
                    legacySession.AvailableInventory,
                    static _ => true);
            CraftingRecipeDefinition legacyRecipe = runtimeRecipes.First(
                recipe => string.Equals(
                    recipe.RequiredStation,
                    "station.portable_fabricator",
                    StringComparison.Ordinal));
            foreach (CraftingStackDefinition input in legacyRecipe.Inputs)
            {
                Grant(
                    legacySession,
                    legacySource,
                    input.DefinitionId,
                    input.Quantity);
            }

            foreach (CatalystStackDefinition catalyst in legacyRecipe.Catalysts)
            {
                Grant(
                    legacySession,
                    legacySource,
                    catalyst.DefinitionId,
                    catalyst.Quantity);
            }

            EnqueueAndReserve(legacyRecipe, legacySession, legacySource);
            ProductionQueueSaveData legacyQueue = legacySource
                .GetQueue("station.portable_fabricator")
                .CreateSaveData();
            ProductionNetworkRuntime legacyRestored =
                ProductionNetworkRuntime.Create(
                    catalog.Stations,
                    recipeMap,
                    StationIds,
                    legacySession.AvailableInventory,
                    static _ => true,
                    saveData: null,
                    legacySaveData: legacyQueue);
            ProductionNetworkHudSnapshot legacyProjection =
                ProductionNetworkHudModel.Build(legacyRestored);
            bool legacyFallback =
                legacyProjection.IsAvailable &&
                legacyProjection.Stations == StationIds.Length &&
                legacyProjection.Jobs == 1 &&
                legacyProjection.RunningJobs == 1 &&
                legacyProjection.StationRows.Single(row =>
                    row.StationId == "station.portable_fabricator").Jobs == 1;

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
            bool passed =
                aggregateCounts &&
                aggregateEnergy &&
                simultaneousRunning &&
                pauseResume &&
                cancelTransition &&
                completion &&
                energyRecharge &&
                coldRestore &&
                legacyFallback &&
                falseUnavailable &&
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
            if (!aggregateCounts) failures.Add("aggregateCounts=0");
            if (!aggregateEnergy) failures.Add("aggregateEnergy=0");
            if (!simultaneousRunning) failures.Add("simultaneousRunning=0");
            if (!pauseResume) failures.Add("pauseResume=0");
            if (!cancelTransition) failures.Add("cancel=0");
            if (!completion) failures.Add("completion=0");
            if (!energyRecharge) failures.Add("recharge=0");
            if (!coldRestore) failures.Add("coldRestore=0");
            if (!legacyFallback) failures.Add("legacyFallback=0");
            if (!falseUnavailable) failures.Add("falseUnavailable=1");
            if (!exactRoundTrip) failures.Add($"roundTrip={mismatch}");
            if (!logWritten) failures.Add("log=0");
            stopwatch.Stop();
            return new ProductionNetworkHudAcceptanceReport(
                passed,
                passed
                    ? "aggregate production network HUD tracks all station queues, transitions, energy, persistence and legacy fallback without false unavailable state"
                    : "production network HUD criteria failed: " +
                      string.Join(", ", failures),
                restoredProjection.Stations,
                aggregateCounts,
                aggregateEnergy,
                simultaneousRunning,
                pauseResume,
                cancelTransition,
                completion,
                energyRecharge,
                coldRestore,
                legacyFallback,
                falseUnavailable,
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
            return new ProductionNetworkHudAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
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
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static StarterRepairSession CreateRepairedSession(
        CraftingRecipeDefinition repairRecipe,
        IReadOnlyList<CraftingRecipeDefinition> runtimeRecipes)
    {
        StarterRepairSession session = new(
            repairRecipe,
            static _ => true,
            runtimeRecipes.ToArray());
        session.GrantInventory(
            repairRecipe.Inputs[0].DefinitionId,
            repairRecipe.Inputs[0].Quantity);
        if (session.TryRepair(out string repairResult) !=
            StarterRepairResult.Repaired)
        {
            throw new InvalidOperationException(repairResult);
        }

        return session;
    }

    private static ProductionQueueCommandReport EnqueueAndReserve(
        CraftingRecipeDefinition recipe,
        StarterRepairSession session,
        ProductionNetworkRuntime network)
    {
        ProductionQueueRuntime queue = network.GetQueue(recipe.RequiredStation);
        ProductionQueueCommandReport report = queue.Enqueue(
            recipe.RecipeId,
            ItemPropertyRuntime.CreateNominalEnvironment(recipe),
            requestedBatches: 1);
        if (report.Result != ProductionQueueCommandResult.Enqueued)
        {
            throw new InvalidOperationException(report.ResultText);
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

        return report;
    }

    private static void ApplyRefund(
        StarterRepairSession session,
        ProductionNetworkRuntime network,
        ProductionQueueRuntime sourceQueue,
        ProductionQueueCommandReport report)
    {
        if (report.Result != ProductionQueueCommandResult.Cancelled)
        {
            throw new InvalidOperationException(report.ResultText);
        }

        foreach (CraftingStackDefinition input in report.RefundedInputs)
        {
            session.GrantInventory(input.DefinitionId, input.Quantity);
            network.AddInventoryAllExcept(
                sourceQueue.StationId,
                input.DefinitionId,
                input.Quantity);
        }

        foreach (CraftingStackDefinition catalyst in report.RefundedCatalysts)
        {
            session.GrantInventory(catalyst.DefinitionId, catalyst.Quantity);
            network.AddInventoryAllExcept(
                sourceQueue.StationId,
                catalyst.DefinitionId,
                catalyst.Quantity);
        }
    }

    private static void ApplyCompletion(
        CraftingRecipeDefinition recipe,
        IndustryProcessExecutionReport process,
        StarterRepairSession session,
        ProductionNetworkRuntime network,
        ProductionQueueRuntime sourceQueue)
    {
        IndustryItemProperties outputProperties =
            ItemPropertyRuntime.CreateOutputProperties(
                recipe,
                process.ProcessSequence,
                ItemPropertyRuntime.CreateNominalEnvironment(recipe));
        foreach (CraftingStackDefinition catalyst in process.RetainedCatalysts)
        {
            session.GrantInventory(catalyst.DefinitionId, catalyst.Quantity);
            network.AddInventoryAllExcept(
                sourceQueue.StationId,
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
                sourceQueue.StationId,
                output.DefinitionId,
                output.Quantity);
        }

        foreach (CraftingStackDefinition byproduct in process.Byproducts)
        {
            session.GrantInventory(byproduct.DefinitionId, byproduct.Quantity);
            network.AddInventoryAllExcept(
                sourceQueue.StationId,
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

    private static bool JobsEqual(
        IReadOnlyDictionary<
            string,
            (ProductionQueueJobStatus Status, double Elapsed)> expected,
        IReadOnlyDictionary<
            string,
            (ProductionQueueJobStatus Status, double Elapsed)> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach (KeyValuePair<
            string,
            (ProductionQueueJobStatus Status, double Elapsed)> pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var actualValue) ||
                pair.Value.Status != actualValue.Status ||
                !NearlyEqual(pair.Value.Elapsed, actualValue.Elapsed))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
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
