using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record FourthCraftingPathAcceptanceReport(
    bool Passed,
    string Result,
    int ResourcesCollected,
    bool BlockedBeforeResources,
    bool TimedCompletion,
    bool RecipeIsolation,
    bool AllThreeRecipesCrafted,
    int CoolantOutputQuantity,
    bool QuestAutosaveObserved,
    bool ExactRoundTrip,
    bool LogWritten,
    int Revision,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class FourthCraftingPathAcceptanceRunner
{
    public static async Task<FourthCraftingPathAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        CraftingRecipeDefinition repairRecipe,
        CraftingRecipeDefinition launchRecipe,
        CraftingRecipeDefinition navigationRecipe,
        CraftingRecipeDefinition coolantRecipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repairRecipe);
        ArgumentNullException.ThrowIfNull(launchRecipe);
        ArgumentNullException.ThrowIfNull(navigationRecipe);
        ArgumentNullException.ThrowIfNull(coolantRecipe);
        ArgumentNullException.ThrowIfNull(resourceBindings);
        SaveDatabase.RegisterKnownInventoryDefinitions(
            resourceBindings
                .Select(binding => binding.ItemDefinitionId)
                .Concat(repairRecipe.Outputs.Select(output => output.DefinitionId))
                .Concat(launchRecipe.Outputs.Select(output => output.DefinitionId))
                .Concat(navigationRecipe.Outputs.Select(output => output.DefinitionId))
                .Concat(coolantRecipe.Outputs.Select(output => output.DefinitionId)));
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
                launchRecipe,
                navigationRecipe,
                coolantRecipe);
            CollectRecipeInputs(session, repairRecipe, resourceBindings);
            StarterRepairResult repairResult = session.TryRepair(out _);
            StationCraftResult blocked = session.ValidateCraft(
                coolantRecipe.RecipeId,
                coolantRecipe.RequiredStation,
                out _);

            ResourceNodeBinding[] coolantBindings = GetRecipeBindings(
                coolantRecipe,
                resourceBindings);
            CollectBindings(session, coolantBindings);
            int coolantInputsBefore = coolantRecipe.Inputs.Sum(input =>
                session.GetAvailableQuantity(input.DefinitionId));
            DataDrivenCraftTimer timer = new();
            bool coolantStarted = timer.TryStart(
                coolantRecipe,
                coolantRecipe.RequiredStation,
                out _);
            CraftTimerAdvanceResult partial = timer.Advance(
                coolantRecipe.CraftTimeSeconds * 0.5,
                out _);
            bool inputsHeld = coolantRecipe.Inputs.Sum(input =>
                session.GetAvailableQuantity(input.DefinitionId)) ==
                coolantInputsBefore &&
                coolantRecipe.Outputs.All(output =>
                    session.GetCraftedQuantity(output.DefinitionId) == 0);
            CraftTimerAdvanceResult coolantCompleted = timer.Advance(
                coolantRecipe.CraftTimeSeconds - timer.ElapsedSeconds,
                out _);
            StationCraftResult coolantCrafted =
                coolantCompleted == CraftTimerAdvanceResult.Completed
                    ? session.TryCraft(
                        coolantRecipe.RecipeId,
                        coolantRecipe.RequiredStation,
                        out _)
                    : StationCraftResult.RecipeUnavailable;
            int coolantOutput = coolantRecipe.Outputs.Sum(output =>
                session.GetCraftedQuantity(output.DefinitionId));
            bool recipeIsolation =
                session.IsRecipeCrafted(coolantRecipe.RecipeId) &&
                !session.IsRecipeCrafted(launchRecipe.RecipeId) &&
                !session.IsRecipeCrafted(navigationRecipe.RecipeId) &&
                launchRecipe.Outputs.Concat(navigationRecipe.Outputs).All(output =>
                    session.GetCraftedQuantity(output.DefinitionId) == 0);

            CollectRecipeInputs(session, navigationRecipe, resourceBindings);
            bool navigationStarted = timer.TryStart(
                navigationRecipe,
                navigationRecipe.RequiredStation,
                out _);
            CraftTimerAdvanceResult navigationCompleted = timer.Advance(
                navigationRecipe.CraftTimeSeconds,
                out _);
            StationCraftResult navigationCrafted =
                navigationCompleted == CraftTimerAdvanceResult.Completed
                    ? session.TryCraft(
                        navigationRecipe.RecipeId,
                        navigationRecipe.RequiredStation,
                        out _)
                    : StationCraftResult.RecipeUnavailable;

            CollectRecipeInputs(session, launchRecipe, resourceBindings);
            bool launchStarted = timer.TryStart(
                launchRecipe,
                launchRecipe.RequiredStation,
                out _);
            CraftTimerAdvanceResult launchCompleted = timer.Advance(
                launchRecipe.CraftTimeSeconds,
                out _);
            StationCraftResult launchCrafted =
                launchCompleted == CraftTimerAdvanceResult.Completed
                    ? session.TryCraft(
                        launchRecipe.RecipeId,
                        launchRecipe.RequiredStation,
                        out _)
                    : StationCraftResult.RecipeUnavailable;
            bool allThreeRecipesCrafted =
                session.IsRecipeCrafted(coolantRecipe.RecipeId) &&
                session.IsRecipeCrafted(navigationRecipe.RecipeId) &&
                session.IsRecipeCrafted(launchRecipe.RecipeId);

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 4.0,
                playerPositionY: 1.0,
                playerPositionZ: -7.0);
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
                launchRecipe,
                navigationRecipe,
                coolantRecipe);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch) &&
                restored.IsRecipeCrafted(launchRecipe.RecipeId) &&
                restored.IsRecipeCrafted(navigationRecipe.RecipeId) &&
                restored.IsRecipeCrafted(coolantRecipe.RecipeId);
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
            bool timedCompletion =
                coolantStarted &&
                partial == CraftTimerAdvanceResult.Running &&
                inputsHeld &&
                coolantCompleted == CraftTimerAdvanceResult.Completed &&
                coolantCrafted == StationCraftResult.Crafted &&
                coolantOutput == coolantRecipe.Outputs.Sum(
                    output => output.Quantity);
            bool previousRecipeRegression =
                navigationStarted &&
                navigationCompleted == CraftTimerAdvanceResult.Completed &&
                navigationCrafted == StationCraftResult.Crafted &&
                launchStarted &&
                launchCompleted == CraftTimerAdvanceResult.Completed &&
                launchCrafted == StationCraftResult.Crafted;
            bool passed =
                repairResult == StarterRepairResult.Repaired &&
                blocked == StationCraftResult.InsufficientInputs &&
                coolantBindings.Length >= 2 &&
                timedCompletion &&
                recipeIsolation &&
                previousRecipeRegression &&
                allThreeRecipesCrafted &&
                autosaveObserved &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (repairResult != StarterRepairResult.Repaired)
                failures.Add("repairSetup=0");
            if (blocked != StationCraftResult.InsufficientInputs)
                failures.Add("blockedBeforeResources=0");
            if (coolantBindings.Length < 2)
                failures.Add($"resources={coolantBindings.Length}");
            if (!timedCompletion)
                failures.Add("timedCompletion=0");
            if (!recipeIsolation)
                failures.Add("recipeIsolation=0");
            if (!previousRecipeRegression)
                failures.Add("previousRecipeRegression=0");
            if (!allThreeRecipesCrafted)
                failures.Add("allThreeCrafted=0");
            if (!autosaveObserved)
                failures.Add("autosave=0");
            if (!exactRoundTrip)
                failures.Add($"roundTrip={mismatch}");
            if (!logWritten)
                failures.Add("logWritten=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add($"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new FourthCraftingPathAcceptanceReport(
                passed,
                passed
                    ? "fourth data-driven resource and timed recipe remained isolated, coexisted with both previous station recipes and persisted exactly"
                    : $"fourth crafting path criteria failed: {string.Join(", ", failures)}",
                coolantBindings.Length,
                blocked == StationCraftResult.InsufficientInputs,
                timedCompletion,
                recipeIsolation,
                allThreeRecipesCrafted,
                coolantOutput,
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
            return new FourthCraftingPathAcceptanceReport(
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

    private static ResourceNodeBinding[] GetRecipeBindings(
        CraftingRecipeDefinition recipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings)
    {
        List<ResourceNodeBinding> selected = new();
        HashSet<string> selectedNodeIds = new(StringComparer.Ordinal);
        foreach (CraftingStackDefinition input in recipe.Inputs)
        {
            int remaining = input.Quantity;
            foreach (ResourceNodeBinding binding in resourceBindings
                .Where(binding => string.Equals(
                    binding.ItemDefinitionId,
                    input.DefinitionId,
                    StringComparison.Ordinal))
                .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal))
            {
                if (!selectedNodeIds.Add(binding.ResourceNodeId))
                {
                    continue;
                }

                selected.Add(binding);
                remaining -= binding.Quantity;
                if (remaining <= 0)
                {
                    break;
                }
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException(
                    $"Recipe {recipe.RecipeId} is missing {remaining} x " +
                    $"{input.DefinitionId} in acceptance bindings.");
            }
        }

        return selected.ToArray();
    }

    private static void CollectRecipeInputs(
        StarterRepairSession session,
        CraftingRecipeDefinition recipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings)
    {
        CollectBindings(session, GetRecipeBindings(recipe, resourceBindings));
    }

    private static void CollectBindings(
        StarterRepairSession session,
        IReadOnlyList<ResourceNodeBinding> bindings)
    {
        foreach (ResourceNodeBinding binding in bindings)
        {
            if (!session.TryCollect(
                binding.ResourceNodeId,
                binding.ItemDefinitionId,
                binding.Quantity,
                out string result))
            {
                throw new InvalidOperationException(result);
            }
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
