using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record CatalogCraftingMatrixAcceptanceReport(
    bool Passed,
    string Result,
    int ItemDefinitions,
    int ResourceDefinitions,
    int RecipeDefinitions,
    int StationRecipes,
    int ResourceNodes,
    int BlockedRecipes,
    int TimedRecipes,
    int IsolatedRecipes,
    int CraftedRecipes,
    int ProducedOutputQuantity,
    bool WrongStationRejected,
    bool DuplicateStartRejected,
    bool QuestAutosaveObserved,
    bool ExactRoundTrip,
    bool LogWritten,
    int Revision,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class CatalogCraftingMatrixAcceptanceRunner
{
    private const int MinimumResourceDefinitions = 10;
    private const int MinimumRecipeDefinitions = 10;
    private const int MinimumItemDefinitions = 20;

    public static async Task<CatalogCraftingMatrixAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog catalog,
        IReadOnlyList<ResourceNodeBinding> resourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resourceBindings);

        Stopwatch stopwatch = Stopwatch.StartNew();
        CraftingRecipeDefinition repairRecipe = catalog.GetRecipe(
            StarterRepairContentIds.RecipeId);
        CraftingRecipeDefinition[] stationRecipes = catalog.Recipes.Values
            .Where(recipe =>
                recipe.RuntimeEnabled &&
                string.Equals(
                    recipe.Application.Type,
                    "StoreOutputs",
                    StringComparison.Ordinal))
            .OrderBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
            .ToArray();

        SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);
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

            StarterRepairSession session = new(repairRecipe, stationRecipes);
            CollectRecipeInputs(session, repairRecipe, resourceBindings);
            StarterRepairResult repairResult = session.TryRepair(out _);
            if (repairResult != StarterRepairResult.Repaired)
            {
                throw new InvalidOperationException(
                    $"Repair setup failed: {repairResult}.");
            }

            int blockedRecipes = 0;
            int timedRecipes = 0;
            int isolatedRecipes = 0;
            int craftedRecipes = 0;
            bool wrongStationRejected = false;
            bool duplicateStartRejected = true;
            DataDrivenCraftTimer timer = new();

            for (int recipeIndex = 0;
                recipeIndex < stationRecipes.Length;
                recipeIndex++)
            {
                CraftingRecipeDefinition recipe = stationRecipes[recipeIndex];
                StationCraftResult blocked = session.ValidateCraft(
                    recipe.RecipeId,
                    recipe.RequiredStation,
                    out _);
                if (blocked == StationCraftResult.InsufficientInputs)
                {
                    blockedRecipes++;
                }

                if (recipeIndex == 0)
                {
                    StationCraftResult wrongStation = session.ValidateCraft(
                        recipe.RecipeId,
                        "station.acceptance_wrong",
                        out _);
                    wrongStationRejected =
                        wrongStation == StationCraftResult.WrongStation;
                }

                CollectRecipeInputs(session, recipe, resourceBindings);
                Dictionary<string, int> beforeOutputs = stationRecipes
                    .SelectMany(definition => definition.Outputs)
                    .Select(output => output.DefinitionId)
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(
                        definitionId => definitionId,
                        session.GetCraftedQuantity,
                        StringComparer.Ordinal);
                Dictionary<string, int> beforeInputs = recipe.Inputs
                    .ToDictionary(
                        input => input.DefinitionId,
                        input => session.GetAvailableQuantity(
                            input.DefinitionId),
                        StringComparer.Ordinal);

                bool started = timer.TryStart(
                    recipe,
                    recipe.RequiredStation,
                    out _);
                bool duplicateRejectedForRecipe = !timer.TryStart(
                    recipe,
                    recipe.RequiredStation,
                    out _);
                duplicateStartRejected &= duplicateRejectedForRecipe;
                CraftTimerAdvanceResult partial = timer.Advance(
                    recipe.CraftTimeSeconds * 0.5,
                    out _);
                bool inputsHeld = recipe.Inputs.All(input =>
                    session.GetAvailableQuantity(input.DefinitionId) ==
                    beforeInputs[input.DefinitionId]);
                bool outputHeld = recipe.Outputs.All(output =>
                    session.GetCraftedQuantity(output.DefinitionId) ==
                    beforeOutputs[output.DefinitionId]);
                CraftTimerAdvanceResult completed = timer.Advance(
                    recipe.CraftTimeSeconds - timer.ElapsedSeconds,
                    out _);
                StationCraftResult craftResult =
                    completed == CraftTimerAdvanceResult.Completed
                        ? session.TryCraft(
                            recipe.RecipeId,
                            recipe.RequiredStation,
                            out _)
                        : StationCraftResult.RecipeUnavailable;

                bool currentOutputsCorrect = recipe.Outputs.All(output =>
                    session.GetCraftedQuantity(output.DefinitionId) ==
                    beforeOutputs[output.DefinitionId] + output.Quantity);
                HashSet<string> currentOutputIds = recipe.Outputs
                    .Select(output => output.DefinitionId)
                    .ToHashSet(StringComparer.Ordinal);
                HashSet<string> currentInputIds = recipe.Inputs
                    .Select(input => input.DefinitionId)
                    .ToHashSet(StringComparer.Ordinal);
                bool otherOutputsUnchanged = beforeOutputs.All(pair =>
                    currentOutputIds.Contains(pair.Key) ||
                    currentInputIds.Contains(pair.Key) ||
                    session.GetCraftedQuantity(pair.Key) == pair.Value);

                if (started &&
                    partial == CraftTimerAdvanceResult.Running &&
                    inputsHeld &&
                    outputHeld &&
                    completed == CraftTimerAdvanceResult.Completed &&
                    craftResult == StationCraftResult.Crafted &&
                    currentOutputsCorrect)
                {
                    timedRecipes++;
                }

                if (currentOutputsCorrect && otherOutputsUnchanged)
                {
                    isolatedRecipes++;
                }

                if (session.IsRecipeCrafted(recipe.RecipeId))
                {
                    craftedRecipes++;
                }

                foreach (CraftingStackDefinition input in recipe.Inputs)
                {
                    bool inputIsCatalogOutput = stationRecipes.Any(
                        candidate => candidate.Outputs.Any(output =>
                            string.Equals(
                                output.DefinitionId,
                                input.DefinitionId,
                                StringComparison.Ordinal)));
                    if (inputIsCatalogOutput)
                    {
                        session.GrantInventory(
                            input.DefinitionId,
                            input.Quantity);
                    }
                }

                timer.Reset();
            }

            int producedOutputQuantity = stationRecipes
                .SelectMany(recipe => recipe.Outputs)
                .Sum(output => session.GetCraftedQuantity(
                    output.DefinitionId));
            bool allStationOutputsCrafted = stationRecipes.All(recipe =>
                session.IsRecipeCrafted(recipe.RecipeId));

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 6.0);
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
                stationRecipes);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch) &&
                stationRecipes.All(recipe =>
                    restored.IsRecipeCrafted(recipe.RecipeId));

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
            bool catalogCoverage =
                catalog.Resources.Count >= MinimumResourceDefinitions &&
                catalog.Recipes.Count >= MinimumRecipeDefinitions &&
                stationRecipes.Length >= MinimumRecipeDefinitions - 1 &&
                catalog.Items.Count >= MinimumItemDefinitions;
            bool passed =
                catalogCoverage &&
                blockedRecipes == stationRecipes.Length &&
                timedRecipes == stationRecipes.Length &&
                isolatedRecipes == stationRecipes.Length &&
                craftedRecipes == stationRecipes.Length &&
                allStationOutputsCrafted &&
                wrongStationRejected &&
                duplicateStartRejected &&
                autosaveObserved &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (!catalogCoverage)
                failures.Add(
                    $"catalog={catalog.Items.Count}/" +
                    $"{catalog.Resources.Count}/{catalog.Recipes.Count}");
            if (blockedRecipes != stationRecipes.Length)
                failures.Add($"blocked={blockedRecipes}/{stationRecipes.Length}");
            if (timedRecipes != stationRecipes.Length)
                failures.Add($"timed={timedRecipes}/{stationRecipes.Length}");
            if (isolatedRecipes != stationRecipes.Length)
                failures.Add($"isolated={isolatedRecipes}/{stationRecipes.Length}");
            if (craftedRecipes != stationRecipes.Length)
                failures.Add($"crafted={craftedRecipes}/{stationRecipes.Length}");
            if (!wrongStationRejected)
                failures.Add("wrongStation=0");
            if (!duplicateStartRejected)
                failures.Add("duplicateStart=0");
            if (!autosaveObserved)
                failures.Add("autosave=0");
            if (!exactRoundTrip)
                failures.Add($"roundTrip={mismatch}");
            if (!logWritten)
                failures.Add("logWritten=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add(
                    $"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new CatalogCraftingMatrixAcceptanceReport(
                passed,
                passed
                    ? "the full crafting catalog met Stage 1 minimum coverage, was validated in one data-driven matrix, crafted independently and persisted exactly"
                    : $"catalog crafting matrix criteria failed: {string.Join(", ", failures)}",
                catalog.Items.Count,
                catalog.Resources.Count,
                catalog.Recipes.Count,
                stationRecipes.Length,
                resourceBindings.Count,
                blockedRecipes,
                timedRecipes,
                isolatedRecipes,
                craftedRecipes,
                producedOutputQuantity,
                wrongStationRejected,
                duplicateStartRejected,
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
            return new CatalogCraftingMatrixAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                catalog.Items.Count,
                catalog.Resources.Count,
                catalog.Recipes.Count,
                stationRecipes.Length,
                resourceBindings.Count,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                false,
                false,
                false,
                0,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void CollectRecipeInputs(
        StarterRepairSession session,
        CraftingRecipeDefinition recipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings)
    {
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
                if (session.CollectedNodeIds.Contains(binding.ResourceNodeId))
                {
                    continue;
                }

                if (!session.TryCollect(
                        binding.ResourceNodeId,
                        binding.ItemDefinitionId,
                        binding.Quantity,
                        out string result))
                {
                    throw new InvalidOperationException(result);
                }

                remaining -= binding.Quantity;
                if (remaining <= 0)
                {
                    break;
                }
            }

            if (remaining > 0)
            {
                session.GrantInventory(input.DefinitionId, remaining);
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
