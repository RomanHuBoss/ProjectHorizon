using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record ThirdCraftingPathAcceptanceReport(
    bool Passed,
    string Result,
    int ResourcesCollected,
    bool BlockedBeforeResources,
    bool TimedCompletion,
    bool RecipeIsolation,
    bool BothRecipesCrafted,
    int NavigationOutputQuantity,
    bool QuestAutosaveObserved,
    bool ExactRoundTrip,
    bool LogWritten,
    int Revision,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class ThirdCraftingPathAcceptanceRunner
{
    public static async Task<ThirdCraftingPathAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        CraftingRecipeDefinition repairRecipe,
        CraftingRecipeDefinition launchRecipe,
        CraftingRecipeDefinition navigationRecipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repairRecipe);
        ArgumentNullException.ThrowIfNull(launchRecipe);
        ArgumentNullException.ThrowIfNull(navigationRecipe);
        ArgumentNullException.ThrowIfNull(resourceBindings);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);

            StarterRepairSession session = new(
                repairRecipe,
                launchRecipe,
                navigationRecipe);
            CollectRecipeInputs(session, repairRecipe, resourceBindings);
            StarterRepairResult repairResult = session.TryRepair(out _);
            StationCraftResult blocked = session.ValidateCraft(
                navigationRecipe.RecipeId,
                navigationRecipe.RequiredStation,
                out _);

            ResourceNodeBinding[] navigationBindings = GetRecipeBindings(
                navigationRecipe,
                resourceBindings);
            CollectBindings(session, navigationBindings);
            int navigationInputsBefore = navigationRecipe.Inputs.Sum(input =>
                session.GetAvailableQuantity(input.DefinitionId));
            DataDrivenCraftTimer timer = new();
            bool navigationStarted = timer.TryStart(
                navigationRecipe,
                navigationRecipe.RequiredStation,
                out _);
            CraftTimerAdvanceResult partial = timer.Advance(
                navigationRecipe.CraftTimeSeconds * 0.5,
                out _);
            bool inputsHeld = navigationRecipe.Inputs.Sum(input =>
                session.GetAvailableQuantity(input.DefinitionId)) ==
                navigationInputsBefore &&
                navigationRecipe.Outputs.All(output =>
                    session.GetCraftedQuantity(output.DefinitionId) == 0);
            CraftTimerAdvanceResult navigationCompleted = timer.Advance(
                navigationRecipe.CraftTimeSeconds - timer.ElapsedSeconds,
                out _);
            StationCraftResult navigationCrafted =
                navigationCompleted == CraftTimerAdvanceResult.Completed
                    ? session.TryCraft(
                        navigationRecipe.RecipeId,
                        navigationRecipe.RequiredStation,
                        out _)
                    : StationCraftResult.RecipeUnavailable;
            int navigationOutput = navigationRecipe.Outputs.Sum(output =>
                session.GetCraftedQuantity(output.DefinitionId));
            bool recipeIsolation =
                session.IsRecipeCrafted(navigationRecipe.RecipeId) &&
                !session.IsRecipeCrafted(launchRecipe.RecipeId) &&
                launchRecipe.Outputs.All(output =>
                    session.GetCraftedQuantity(output.DefinitionId) == 0);

            ResourceNodeBinding[] launchBindings = GetRecipeBindings(
                launchRecipe,
                resourceBindings);
            CollectBindings(session, launchBindings);
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
            bool bothRecipesCrafted =
                session.IsRecipeCrafted(navigationRecipe.RecipeId) &&
                session.IsRecipeCrafted(launchRecipe.RecipeId);

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: -3.0,
                playerPositionY: 1.0,
                playerPositionZ: -6.0);
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
                navigationRecipe);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch) &&
                restored.IsRecipeCrafted(launchRecipe.RecipeId) &&
                restored.IsRecipeCrafted(navigationRecipe.RecipeId);
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
                navigationStarted &&
                partial == CraftTimerAdvanceResult.Running &&
                inputsHeld &&
                navigationCompleted == CraftTimerAdvanceResult.Completed &&
                navigationCrafted == StationCraftResult.Crafted &&
                navigationOutput == navigationRecipe.Outputs.Sum(
                    output => output.Quantity);
            bool passed =
                repairResult == StarterRepairResult.Repaired &&
                blocked == StationCraftResult.InsufficientInputs &&
                navigationBindings.Length >= 2 &&
                timedCompletion &&
                recipeIsolation &&
                launchStarted &&
                launchCompleted == CraftTimerAdvanceResult.Completed &&
                launchCrafted == StationCraftResult.Crafted &&
                bothRecipesCrafted &&
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
            if (navigationBindings.Length < 2)
                failures.Add($"resources={navigationBindings.Length}");
            if (!timedCompletion)
                failures.Add("timedCompletion=0");
            if (!recipeIsolation)
                failures.Add("recipeIsolation=0");
            if (!launchStarted ||
                launchCompleted != CraftTimerAdvanceResult.Completed ||
                launchCrafted != StationCraftResult.Crafted)
                failures.Add("launchRegression=0");
            if (!bothRecipesCrafted)
                failures.Add("bothCrafted=0");
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
            return new ThirdCraftingPathAcceptanceReport(
                passed,
                passed
                    ? "third data-driven resource and timed recipe coexisted with the launch recipe and persisted exactly"
                    : $"third crafting path criteria failed: {string.Join(", ", failures)}",
                navigationBindings.Length,
                blocked == StationCraftResult.InsufficientInputs,
                timedCompletion,
                recipeIsolation,
                bothRecipesCrafted,
                navigationOutput,
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
            return new ThirdCraftingPathAcceptanceReport(
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
