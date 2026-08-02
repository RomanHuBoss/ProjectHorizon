using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record TechnologyRecipeSelectorAcceptanceReport(
    bool Passed,
    string Result,
    int RecipesListed,
    int PhysicalStationsRequired,
    int InitiallyUnlockedRecipes,
    int InitiallyLockedRecipes,
    bool MissingPrerequisiteRejected,
    int TechnologiesUnlocked,
    bool AllRecipesUnlocked,
    bool TechnologyBlockedBeforeResearch,
    bool CraftReadyAfterResearch,
    bool SelectedRecipeCrafted,
    int ResearchPointsRemaining,
    bool ExactRoundTrip,
    bool ProgressRestored,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class TechnologyRecipeSelectorAcceptanceRunner
{
    public const int DefaultResearchPoints = 2000;

    public static async Task<TechnologyRecipeSelectorAcceptanceReport> RunAsync(
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
        string stationId = stationRecipes
            .Select(recipe => recipe.RequiredStation)
            .Distinct(StringComparer.Ordinal)
            .Single();

        SaveDatabase.RegisterKnownInventoryDefinitions(catalog.Items.Keys);
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

            TechnologyProgression progression = new(
                catalog.Technologies,
                DefaultResearchPoints);
            StarterRepairSession session = new(
                repairRecipe,
                progression.IsUnlocked,
                stationRecipes);
            StationRecipeSelectorModel selector = new(
                catalog,
                session,
                progression);
            IReadOnlyList<StationRecipeSelectorEntry> initialEntries =
                selector.GetRecipeEntries(stationId);
            int initiallyUnlocked = initialEntries.Count(entry =>
                entry.TechnologyUnlocked);
            int initiallyLocked = initialEntries.Count - initiallyUnlocked;

            CollectRecipeInputs(session, repairRecipe, resourceBindings);
            if (session.TryRepair(out _) != StarterRepairResult.Repaired)
            {
                throw new InvalidOperationException(
                    "Repair setup failed before selector acceptance.");
            }

            CraftingRecipeDefinition selectedRecipe = stationRecipes.Single(
                recipe => string.Equals(
                    recipe.RecipeId,
                    "recipe.ship.sensor_lens",
                    StringComparison.Ordinal));
            StationCraftResult blockedBeforeResearch = session.ValidateCraft(
                selectedRecipe.RecipeId,
                stationId,
                out _);
            bool technologyBlockedBeforeResearch =
                blockedBeforeResearch == StationCraftResult.TechnologyLocked;

            TechnologyUnlockResult missingPrerequisite = progression.TryUnlock(
                selectedRecipe.RequiredTechnology,
                out _);
            bool missingPrerequisiteRejected =
                missingPrerequisite == TechnologyUnlockResult.MissingPrerequisites;

            IReadOnlyList<TechnologyDefinition> relevantTechnologies =
                selector.GetResearchEntries(stationId);
            int unlockedBefore = progression.UnlockedCount;
            UnlockRelevantTechnologies(progression, relevantTechnologies);
            int technologiesUnlocked = progression.UnlockedCount - unlockedBefore;
            bool allRecipesUnlocked = selector.GetRecipeEntries(stationId)
                .All(entry => entry.TechnologyUnlocked);

            StationCraftResult afterResearch = session.ValidateCraft(
                selectedRecipe.RecipeId,
                stationId,
                out _);
            bool craftReadyAfterResearch =
                afterResearch == StationCraftResult.InsufficientInputs;
            CollectRecipeInputs(session, selectedRecipe, resourceBindings);
            StationCraftResult craftResult = session.TryCraft(
                selectedRecipe.RecipeId,
                stationId,
                out _);
            bool selectedRecipeCrafted =
                craftResult == StationCraftResult.Crafted &&
                session.IsRecipeCrafted(selectedRecipe.RecipeId);

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 6.0,
                technologyProgress: progression.ToSaveData());
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
            TechnologyProgression restoredProgression =
                TechnologyProgression.FromSaveData(
                    catalog.Technologies,
                    loaded?.TechnologyProgress,
                    DefaultResearchPoints);
            bool progressRestored =
                loaded?.TechnologyProgress is not null &&
                restoredProgression.ResearchPoints == progression.ResearchPoints &&
                restoredProgression.UnlockedTechnologyIds.SequenceEqual(
                    progression.UnlockedTechnologyIds,
                    StringComparer.Ordinal);

            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool autosaveOk =
                autosave.CompletedBatches == 1 &&
                autosave.FailedBatches == 0 &&
                autosave.HasObservedTrigger(AutosaveTrigger.QuestCompleted);
            bool passed =
                initialEntries.Count == stationRecipes.Length &&
                stationRecipes.Length > 1 &&
                initiallyUnlocked > 0 &&
                initiallyLocked > 0 &&
                missingPrerequisiteRejected &&
                technologiesUnlocked > 0 &&
                allRecipesUnlocked &&
                technologyBlockedBeforeResearch &&
                craftReadyAfterResearch &&
                selectedRecipeCrafted &&
                exactRoundTrip &&
                progressRestored &&
                autosaveOk &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (initialEntries.Count != stationRecipes.Length)
                failures.Add($"listed={initialEntries.Count}/{stationRecipes.Length}");
            if (initiallyUnlocked == 0 || initiallyLocked == 0)
                failures.Add($"initial={initiallyUnlocked}/{initiallyLocked}");
            if (!missingPrerequisiteRejected)
                failures.Add("prerequisite=0");
            if (!allRecipesUnlocked)
                failures.Add("allUnlocked=0");
            if (!technologyBlockedBeforeResearch)
                failures.Add("technologyBlock=0");
            if (!craftReadyAfterResearch)
                failures.Add("readyAfterResearch=0");
            if (!selectedRecipeCrafted)
                failures.Add("crafted=0");
            if (!exactRoundTrip)
                failures.Add($"roundTrip={mismatch}");
            if (!progressRestored)
                failures.Add("progressRestored=0");
            if (!autosaveOk)
                failures.Add("autosave=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add($"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new TechnologyRecipeSelectorAcceptanceReport(
                passed,
                passed
                    ? "one physical station exposed every runtime recipe, enforced technology prerequisites, unlocked the relevant research graph and persisted progress exactly"
                    : $"selector/research criteria failed: {string.Join(", ", failures)}",
                initialEntries.Count,
                1,
                initiallyUnlocked,
                initiallyLocked,
                missingPrerequisiteRejected,
                technologiesUnlocked,
                allRecipesUnlocked,
                technologyBlockedBeforeResearch,
                craftReadyAfterResearch,
                selectedRecipeCrafted,
                progression.ResearchPoints,
                exactRoundTrip,
                progressRestored,
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
            return new TechnologyRecipeSelectorAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                stationRecipes.Length,
                1,
                0,
                0,
                false,
                0,
                false,
                false,
                false,
                false,
                0,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void UnlockRelevantTechnologies(
        TechnologyProgression progression,
        IReadOnlyList<TechnologyDefinition> technologies)
    {
        HashSet<string> pending = technologies
            .Where(technology => !progression.IsUnlocked(
                technology.TechnologyId))
            .Select(technology => technology.TechnologyId)
            .ToHashSet(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            bool progressed = false;
            foreach (string technologyId in pending
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray())
            {
                TechnologyUnlockResult result = progression.TryUnlock(
                    technologyId,
                    out _);
                if (result == TechnologyUnlockResult.Unlocked ||
                    result == TechnologyUnlockResult.AlreadyUnlocked)
                {
                    pending.Remove(technologyId);
                    progressed = true;
                }
                else if (result ==
                    TechnologyUnlockResult.InsufficientResearchPoints)
                {
                    throw new InvalidOperationException(
                        $"Acceptance research budget was insufficient for {technologyId}.");
                }
            }

            if (!progressed)
            {
                throw new InvalidOperationException(
                    "Relevant technology graph could not be unlocked in prerequisite order.");
            }
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
                throw new InvalidOperationException(
                    $"Recipe {recipe.RecipeId} is missing {remaining} x " +
                    $"{input.DefinitionId} in acceptance bindings.");
            }
        }
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        foreach (string suffix in new[]
        {
            string.Empty,
            "-wal",
            "-shm",
            ".bak",
            ".bak-wal",
            ".bak-shm",
            ".autosave.log"
        })
        {
            string path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
