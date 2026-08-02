using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record ProductionQueueAcceptanceReport(
    bool Passed,
    string Result,
    string StationId,
    int ParallelSlots,
    int MaximumParallelRunning,
    bool ThirdJobQueued,
    bool PauseResumePreservedProgress,
    bool GracefulExitRestored,
    bool ActiveCancellation,
    bool RefundExact,
    int CompletedProcesses,
    bool QueueDrained,
    double EnergyRemaining,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ProductionQueueAcceptanceRunner
{
    private const string StationId = "station.smelter";
    private const string FerriteRecipeId =
        "recipe.refining.refined_ferrite";
    private const string CopperRecipeId =
        "recipe.refining.copper_ingot";
    private const string TitaniumRecipeId =
        "recipe.refining.titanium_ingot";

    public static async Task<ProductionQueueAcceptanceReport> RunAsync(
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
            CraftingStationDefinition station = catalog.GetStation(StationId);
            CraftingRecipeDefinition ferrite = catalog.GetRecipe(
                FerriteRecipeId);
            CraftingRecipeDefinition copper = catalog.GetRecipe(
                CopperRecipeId);
            CraftingRecipeDefinition titanium = catalog.GetRecipe(
                TitaniumRecipeId);
            IndustryProcessEnvironment environment = new(
                300.0,
                110.0,
                false);

            ProductionQueueRuntime runtime = new(
                station,
                catalog.Recipes,
                initialEnergy: station.EnergyCapacity,
                static _ => true);
            SeedInputs(runtime, ferrite);
            SeedInputs(runtime, copper);
            SeedInputs(runtime, titanium);

            ProductionQueueCommandReport ferriteEnqueue = runtime.Enqueue(
                ferrite.RecipeId,
                environment,
                requestedBatches: 1);
            ProductionQueueCommandReport copperEnqueue = runtime.Enqueue(
                copper.RecipeId,
                environment,
                requestedBatches: 1);
            ProductionQueueCommandReport titaniumEnqueue = runtime.Enqueue(
                titanium.RecipeId,
                environment,
                requestedBatches: 1);
            bool enqueueAccepted =
                ferriteEnqueue.Result == ProductionQueueCommandResult.Enqueued &&
                copperEnqueue.Result == ProductionQueueCommandResult.Enqueued &&
                titaniumEnqueue.Result == ProductionQueueCommandResult.Enqueued;
            bool thirdJobQueued =
                runtime.RunningCount == station.ParallelSlots &&
                runtime.QueuedCount == 1 &&
                runtime.Jobs.Single(job =>
                    job.JobId == titaniumEnqueue.JobId).Status ==
                    ProductionQueueJobStatus.Queued;

            runtime.Advance(2.0);
            double ferriteElapsedBeforePause = runtime.Jobs.Single(job =>
                job.JobId == ferriteEnqueue.JobId).ElapsedSeconds;
            ProductionQueueCommandReport pause = runtime.Pause(
                ferriteEnqueue.JobId);
            bool queuedStartedAfterPause =
                runtime.Jobs.Single(job =>
                    job.JobId == titaniumEnqueue.JobId).Status ==
                    ProductionQueueJobStatus.Running;
            runtime.Advance(1.0);
            double ferriteElapsedWhilePaused = runtime.Jobs.Single(job =>
                job.JobId == ferriteEnqueue.JobId).ElapsedSeconds;
            ProductionQueueCommandReport resume = runtime.Resume(
                ferriteEnqueue.JobId);
            ProductionQueueJobView resumedFerrite = runtime.Jobs.Single(job =>
                job.JobId == ferriteEnqueue.JobId);
            bool pauseResumePreservedProgress =
                pause.Result == ProductionQueueCommandResult.Paused &&
                resume.Result == ProductionQueueCommandResult.Resumed &&
                queuedStartedAfterPause &&
                Math.Abs(
                    ferriteElapsedBeforePause -
                    ferriteElapsedWhilePaused) < 0.000001 &&
                resumedFerrite.Status == ProductionQueueJobStatus.Queued &&
                Math.Abs(resumedFerrite.ElapsedSeconds - 2.0) < 0.000001;

            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);

            SaveGameSnapshot gracefulSnapshot = CreateSnapshot(
                slotId,
                revision: 1,
                runtime);
            await autosave.FlushAsync(
                AutosaveTrigger.GracefulExit,
                gracefulSnapshot,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? gracefulLoaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool gracefulRoundTrip = SaveDatabase.SnapshotsEqual(
                gracefulSnapshot,
                gracefulLoaded,
                out string gracefulMismatch);
            SaveGameSnapshot restoredSnapshot = gracefulLoaded ??
                throw new InvalidDataException(
                    "Graceful-exit snapshot was not restored.");
            ProductionQueueSaveData restoredQueueData =
                restoredSnapshot.ProductionQueue ??
                throw new InvalidDataException(
                    "Graceful-exit snapshot did not restore production queue.");
            IReadOnlyList<InventoryItemSaveData> restoredInventory =
                restoredSnapshot.Inventory;

            ProductionQueueRuntime restored = ProductionQueueRuntime.Restore(
                station,
                catalog.Recipes,
                restoredQueueData,
                ToInventory(restoredInventory),
                static _ => true);
            bool gracefulExitRestored =
                gracefulRoundTrip &&
                restored.RunningCount == 2 &&
                restored.QueuedCount == 1 &&
                restored.PausedCount == 0 &&
                restored.Jobs.Any(job =>
                    job.JobId == ferriteEnqueue.JobId &&
                    job.Status == ProductionQueueJobStatus.Queued &&
                    Math.Abs(job.ElapsedSeconds - 2.0) < 0.000001) &&
                restored.Jobs.Any(job =>
                    job.JobId == copperEnqueue.JobId &&
                    job.Status == ProductionQueueJobStatus.Running &&
                    Math.Abs(job.ElapsedSeconds - 3.0) < 0.000001) &&
                restored.Jobs.Any(job =>
                    job.JobId == titaniumEnqueue.JobId &&
                    job.Status == ProductionQueueJobStatus.Running &&
                    Math.Abs(job.ElapsedSeconds - 1.0) < 0.000001);

            ProductionQueueCommandReport cancellation = restored.Cancel(
                titaniumEnqueue.JobId);
            bool activeCancellation =
                cancellation.Result == ProductionQueueCommandResult.Cancelled &&
                restored.Jobs.All(job =>
                    job.JobId != titaniumEnqueue.JobId) &&
                restored.Jobs.Single(job =>
                    job.JobId == ferriteEnqueue.JobId).Status ==
                    ProductionQueueJobStatus.Running;
            CraftingStackDefinition titaniumInput = titanium.Inputs.Single();
            double expectedRefundEnergy = titanium.EnergyCost;
            bool refundExact =
                cancellation.RefundedInputs.Count == 1 &&
                cancellation.RefundedInputs[0].DefinitionId ==
                    titaniumInput.DefinitionId &&
                cancellation.RefundedInputs[0].Quantity ==
                    titaniumInput.Quantity &&
                cancellation.RefundedCatalysts.Count == 0 &&
                Math.Abs(
                    cancellation.RefundedEnergy - expectedRefundEnergy) <
                    0.000001 &&
                restored.GetQuantity(titaniumInput.DefinitionId) ==
                    titaniumInput.Quantity &&
                Math.Abs(restored.EnergyRemaining - 96.0) < 0.000001;

            ProductionQueueAdvanceReport completion = restored.Advance(20.0);
            bool completionExact =
                completion.CompletedProcesses.Count == 2 &&
                restored.GetQuantity(
                    ferrite.Outputs.Single().DefinitionId) == 1 &&
                restored.GetQuantity(
                    copper.Outputs.Single().DefinitionId) == 1 &&
                restored.GetQuantity(
                    titanium.Outputs.Single().DefinitionId) == 0 &&
                restored.GetQuantity("resource.scrap_metal") == 2 &&
                restored.GetQuantity(titaniumInput.DefinitionId) == 2;
            bool queueDrained =
                restored.Jobs.Count == 0 &&
                restored.RunningCount == 0 &&
                restored.QueuedCount == 0 &&
                restored.PausedCount == 0;

            SaveGameSnapshot finalSnapshot = CreateSnapshot(
                slotId,
                revision: 2,
                restored);
            await autosave.FlushAsync(
                AutosaveTrigger.QuestCompleted,
                finalSnapshot,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? finalLoaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool finalRoundTrip = SaveDatabase.SnapshotsEqual(
                finalSnapshot,
                finalLoaded,
                out string finalMismatch);
            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten =
                logText.Contains(
                    nameof(AutosaveTrigger.GracefulExit),
                    StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal) &&
                CountOccurrences(logText, "AUTOSAVE_COMPLETED") >= 2;
            bool autosaveOk =
                autosave.CompletedBatches == 2 &&
                autosave.FailedBatches == 0 &&
                autosave.HasObservedTrigger(AutosaveTrigger.GracefulExit) &&
                autosave.HasObservedTrigger(AutosaveTrigger.QuestCompleted);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed =
                enqueueAccepted &&
                thirdJobQueued &&
                pauseResumePreservedProgress &&
                gracefulExitRestored &&
                activeCancellation &&
                refundExact &&
                completionExact &&
                queueDrained &&
                restored.MaximumObservedRunning == station.ParallelSlots &&
                finalRoundTrip &&
                logWritten &&
                autosaveOk &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (!enqueueAccepted)
                failures.Add("enqueue=0");
            if (!thirdJobQueued)
                failures.Add("queued=0");
            if (!pauseResumePreservedProgress)
                failures.Add("pauseResume=0");
            if (!gracefulExitRestored)
                failures.Add($"gracefulRestore={gracefulMismatch}");
            if (!activeCancellation)
                failures.Add("activeCancel=0");
            if (!refundExact)
                failures.Add("refund=0");
            if (!completionExact)
                failures.Add("completion=0");
            if (!queueDrained)
                failures.Add("queueDrained=0");
            if (restored.MaximumObservedRunning != station.ParallelSlots)
                failures.Add(
                    $"maxParallel={restored.MaximumObservedRunning}");
            if (!finalRoundTrip)
                failures.Add($"roundTrip={finalMismatch}");
            if (!logWritten)
                failures.Add("logWritten=0");
            if (!autosaveOk)
                failures.Add("autosave=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add(
                    $"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new ProductionQueueAcceptanceReport(
                passed,
                passed
                    ? "parallel production slots queued work, freeze-and-resume persistence restored exact progress, cancellation refunded every reservation and remaining jobs completed exactly"
                    : $"production queue criteria failed: {string.Join(", ", failures)}",
                station.StationId,
                station.ParallelSlots,
                restored.MaximumObservedRunning,
                thirdJobQueued,
                pauseResumePreservedProgress,
                gracefulExitRestored,
                activeCancellation,
                refundExact,
                completion.CompletedProcesses.Count,
                queueDrained,
                restored.EnergyRemaining,
                finalRoundTrip,
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
            return new ProductionQueueAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                StationId,
                0,
                0,
                false,
                false,
                false,
                false,
                false,
                0,
                false,
                0.0,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void SeedInputs(
        ProductionQueueRuntime runtime,
        CraftingRecipeDefinition recipe)
    {
        foreach (CraftingStackDefinition input in recipe.Inputs)
        {
            runtime.AddInventory(input.DefinitionId, input.Quantity);
        }

        foreach (CatalystStackDefinition catalyst in recipe.Catalysts)
        {
            runtime.AddInventory(catalyst.DefinitionId, catalyst.Quantity);
        }
    }

    private static SaveGameSnapshot CreateSnapshot(
        string slotId,
        int revision,
        ProductionQueueRuntime runtime)
    {
        string updatedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        InventoryItemSaveData[] inventory = runtime.Inventory
            .Select((stack, index) => new InventoryItemSaveData(
                $"queue.inventory.{index:000}",
                stack.DefinitionId,
                stack.Quantity,
                1.0))
            .ToArray();
        return new SaveGameSnapshot(
            slotId,
            revision,
            GeneratorVersion: 1,
            ContentVersion: SaveDatabase.CurrentContentVersion,
            updatedUtc,
            new PlayerSaveData(
                "player.queue_acceptance",
                0.0,
                1.0,
                0.0,
                StarterRepairSnapshotFactory.PlanetId),
            new ShipSaveData(
                "ship.queue_acceptance",
                "ship.starter.repairable",
                "Queue Acceptance Vessel",
                100.0,
                35.0,
                0.0,
                1.0,
                0.0),
            inventory,
            new VisitedPlanetSaveData(
                StarterRepairSnapshotFactory.PlanetId,
                StarterRepairSnapshotFactory.SystemId,
                updatedUtc,
                1),
            TechnologyProgress: null,
            ProductionQueue: runtime.CreateSaveData());
    }

    private static IReadOnlyList<CraftingStackDefinition> ToInventory(
        IReadOnlyList<InventoryItemSaveData> inventory)
    {
        return inventory
            .GroupBy(item => item.DefinitionId, StringComparer.Ordinal)
            .Select(group => new CraftingStackDefinition(
                group.Key,
                group.Sum(item => item.Quantity)))
            .Where(stack => stack.Quantity > 0)
            .OrderBy(stack => stack.DefinitionId, StringComparer.Ordinal)
            .ToArray();
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

    private static int CountOccurrences(
        string text,
        string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(
            value,
            offset,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
