using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record CraftingExpansionAcceptanceReport(
    bool Passed,
    string Result,
    int ResourcesCollected,
    bool RepairPrerequisiteEnforced,
    bool WrongStationRejected,
    bool CraftBlockedBeforeResources,
    bool Crafted,
    int ProducedOutputQuantity,
    bool QuestAutosaveObserved,
    bool ExactRoundTrip,
    bool LogWritten,
    int Revision,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class CraftingExpansionAcceptanceRunner
{
    public static async Task<CraftingExpansionAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        CraftingRecipeDefinition repairRecipe,
        CraftingRecipeDefinition craftingRecipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repairRecipe);
        ArgumentNullException.ThrowIfNull(craftingRecipe);
        ArgumentNullException.ThrowIfNull(resourceBindings);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);

            StarterRepairSession session = new(
                repairRecipe,
                craftingRecipe);
            StationCraftResult beforeRepair = session.TryCraftSecondary(
                craftingRecipe.RequiredStation,
                out _);

            HashSet<string> repairDefinitions = repairRecipe.Inputs
                .Select(input => input.DefinitionId)
                .ToHashSet(StringComparer.Ordinal);
            ResourceNodeBinding[] repairBindings = resourceBindings
                .Where(binding => repairDefinitions.Contains(
                    binding.ItemDefinitionId))
                .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
                .ToArray();
            foreach (ResourceNodeBinding binding in repairBindings)
            {
                if (!session.TryCollect(
                    binding.ResourceNodeId,
                    binding.ItemDefinitionId,
                    binding.Quantity,
                    out string repairCollectResult))
                {
                    throw new InvalidOperationException(repairCollectResult);
                }
            }

            StarterRepairResult repairResult = session.TryRepair(out _);
            StationCraftResult wrongStation = session.TryCraftSecondary(
                "station.acceptance.wrong",
                out _);
            StationCraftResult blocked = session.TryCraftSecondary(
                craftingRecipe.RequiredStation,
                out _);

            HashSet<string> requiredDefinitions = craftingRecipe.Inputs
                .Select(input => input.DefinitionId)
                .ToHashSet(StringComparer.Ordinal);
            ResourceNodeBinding[] relevantBindings = resourceBindings
                .Where(binding => requiredDefinitions.Contains(
                    binding.ItemDefinitionId))
                .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
                .ToArray();
            foreach (ResourceNodeBinding binding in relevantBindings)
            {
                if (!session.TryCollect(
                    binding.ResourceNodeId,
                    binding.ItemDefinitionId,
                    binding.Quantity,
                    out string collectResult))
                {
                    throw new InvalidOperationException(collectResult);
                }
            }

            StationCraftResult craftedResult = session.TryCraftSecondary(
                craftingRecipe.RequiredStation,
                out _);
            int outputQuantity = craftingRecipe.Outputs.Sum(output =>
                session.GetCraftedQuantity(output.DefinitionId));
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 2.0,
                playerPositionY: 1.0,
                playerPositionZ: -5.0);
            await autosave.FlushAsync(
                AutosaveTrigger.QuestCompleted,
                expected,
                cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            StarterRepairSession restored = StarterRepairSession.FromSnapshot(
                loaded,
                resourceBindings.ToDictionary(
                    binding => binding.ResourceNodeId,
                    StringComparer.Ordinal),
                repairRecipe,
                craftingRecipe);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch) &&
                restored.SecondaryRecipeCrafted;
            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            bool autosaveObserved = autosave.HasObservedTrigger(
                AutosaveTrigger.QuestCompleted) &&
                autosave.CompletedBatches == 1 &&
                autosave.FailedBatches == 0;
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten = logText.Contains(
                "AUTOSAVE_COMPLETED",
                StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool inputsConsumed = craftingRecipe.Inputs.All(input =>
                session.GetAvailableQuantity(input.DefinitionId) == 0);
            bool passed =
                beforeRepair == StationCraftResult.ShipNotRepaired &&
                repairResult == StarterRepairResult.Repaired &&
                wrongStation == StationCraftResult.WrongStation &&
                blocked == StationCraftResult.InsufficientInputs &&
                relevantBindings.Length >= 2 &&
                craftedResult == StationCraftResult.Crafted &&
                session.SecondaryRecipeCrafted &&
                inputsConsumed &&
                outputQuantity == craftingRecipe.Outputs.Sum(
                    output => output.Quantity) &&
                autosaveObserved &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (beforeRepair != StationCraftResult.ShipNotRepaired)
                failures.Add("repairPrerequisite=0");
            if (repairResult != StarterRepairResult.Repaired)
                failures.Add("repairSetup=0");
            if (wrongStation != StationCraftResult.WrongStation)
                failures.Add("wrongStationRejected=0");
            if (blocked != StationCraftResult.InsufficientInputs)
                failures.Add("blockedBeforeResources=0");
            if (craftedResult != StationCraftResult.Crafted)
                failures.Add("crafted=0");
            if (!inputsConsumed)
                failures.Add("inputsConsumed=0");
            if (!autosaveObserved)
                failures.Add("autosave=0");
            if (!exactRoundTrip)
                failures.Add($"roundTrip={mismatch}");
            if (!logWritten)
                failures.Add("logWritten=0");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new CraftingExpansionAcceptanceReport(
                passed,
                passed
                    ? "second resource was collected, crafted at the dedicated station and persisted exactly"
                    : $"crafting expansion criteria failed: {string.Join(", ", failures)}",
                relevantBindings.Length,
                beforeRepair == StationCraftResult.ShipNotRepaired,
                wrongStation == StationCraftResult.WrongStation,
                blocked == StationCraftResult.InsufficientInputs,
                craftedResult == StationCraftResult.Crafted,
                outputQuantity,
                autosaveObserved,
                exactRoundTrip,
                logWritten,
                expected.Revision,
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
            return new CraftingExpansionAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                0,
                false,
                false,
                false,
                false,
                0,
                false,
                false,
                false,
                0,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
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

        string logPath = Path.Combine(directory, "logs", $"{baseName}.autosave.log");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
    }
}
