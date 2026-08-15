using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record CatalogResourceLifecycleAcceptanceReport(
    bool Passed,
    string Result,
    int CatalogResources,
    int PhysicalResourceTypes,
    int ResourceNodes,
    int GeneratedNodes,
    int CollectedResourceTypes,
    int CollectedResourceNodes,
    bool CatalogMetadataValid,
    bool DeterministicPlacement,
    bool UniqueNodes,
    bool DuplicateRejected,
    bool InventoryMirrorsSynchronized,
    bool DepletionPersisted,
    bool ColdRestoreExact,
    bool ResetReady,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

/// <summary>
/// Catalog-wide resource lifecycle acceptance. It verifies physical coverage of
/// every world resource and every physical node, generic collection, duplicate
/// protection, production inventory mirroring, depletion persistence, cold
/// restore and reset semantics in an isolated SQLite database.
/// </summary>
public static class CatalogResourceLifecycleAcceptanceRunner
{
    public const int ExpectedCatalogResources = 42;
    public const int ExpectedAuthoredNodes = 32;
    public const int ExpectedGeneratedNodes = 26;
    public const int ExpectedTotalNodes = 58;

    public static async Task<CatalogResourceLifecycleAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog catalog,
        CraftingRecipeDefinition repairRecipe,
        IReadOnlyList<CraftingRecipeDefinition> stationRecipes,
        IReadOnlyList<ResourceNodeBinding> resourceBindings,
        IReadOnlyList<CatalogResourcePlacement> generatedPlacements,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Database path must not be empty.",
                nameof(databasePath));
        }

        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        ArgumentNullException.ThrowIfNull(stationRecipes);
        ArgumentNullException.ThrowIfNull(resourceBindings);
        ArgumentNullException.ThrowIfNull(generatedPlacements);

        System.Diagnostics.Stopwatch stopwatch =
            System.Diagnostics.Stopwatch.StartNew();
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

            Dictionary<string, GameResourceDefinition> resourcesByItem =
                catalog.Resources.Values.ToDictionary(
                    resource => resource.ItemDefinitionId,
                    StringComparer.Ordinal);
            string[] physicalItemDefinitionIds = resourceBindings
                .Select(binding => binding.ItemDefinitionId)
                .Where(resourcesByItem.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            int physicalResourceTypes = physicalItemDefinitionIds.Length;
            bool completePhysicalCoverage = catalog.Resources.Values.All(
                resource => physicalItemDefinitionIds.Contains(
                    resource.ItemDefinitionId,
                    StringComparer.Ordinal));
            bool uniqueNodes = resourceBindings
                .Select(binding => binding.ResourceNodeId)
                .Distinct(StringComparer.Ordinal)
                .Count() == resourceBindings.Count;

            IReadOnlyDictionary<string, int> physicalYieldByDefinition =
                resourceBindings
                    .GroupBy(
                        binding => binding.ItemDefinitionId,
                        StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(binding => binding.Quantity),
                        StringComparer.Ordinal);
            bool catalogMetadataValid = catalog.Resources.Values.All(resource =>
            {
                GameItemDefinition item = catalog.GetItem(
                    resource.ItemDefinitionId);
                int yield = resource.GetDeterministicYield();
                physicalYieldByDefinition.TryGetValue(
                    resource.ItemDefinitionId,
                    out int physicalYield);
                return yield > 0 &&
                    yield <= item.MaxStack &&
                    physicalYield > 0 &&
                    physicalYield <= item.MaxStack &&
                    item.MaxStack > 0 &&
                    item.Mass >= 0.0 &&
                    item.BasePrice >= 0.0 &&
                    !string.IsNullOrWhiteSpace(resource.ExtractionMethod) &&
                    resource.ScanTier >= 0;
            });

            HashSet<string> generatedDefinitions = generatedPlacements
                .Select(placement => placement.ResourceDefinitionId)
                .ToHashSet(StringComparer.Ordinal);
            string[] authoredDefinitions = catalog.Resources.Keys
                .Except(generatedDefinitions, StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            IReadOnlyList<CatalogResourcePlacement> recomputedPlacements =
                CatalogResourceFieldPlanner.BuildMissingPlacements(
                    catalog.Resources,
                    authoredDefinitions);
            bool deterministicPlacement =
                generatedPlacements.SequenceEqual(recomputedPlacements) &&
                generatedPlacements.Select(placement => (
                        placement.PositionX,
                        placement.PositionY,
                        placement.PositionZ))
                    .Distinct().Count() == generatedPlacements.Count;

            ResourceNodeBinding[] orderedBindings = resourceBindings
                .OrderBy(binding => binding.ResourceNodeId, StringComparer.Ordinal)
                .ToArray();
            CraftingRecipeDefinition[] recipes = stationRecipes
                .OrderBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
                .ToArray();
            StarterRepairSession session = new(
                repairRecipe,
                static _ => true,
                recipes);
            string[] activeStationIds = recipes
                .Select(recipe => recipe.RequiredStation)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            ProductionNetworkRuntime network = ProductionNetworkRuntime.Create(
                catalog.Stations,
                catalog.Recipes,
                activeStationIds,
                Array.Empty<CraftingStackDefinition>());

            foreach (ResourceNodeBinding binding in orderedBindings)
            {
                if (!session.TryCollect(
                        binding.ResourceNodeId,
                        binding.ItemDefinitionId,
                        binding.Quantity,
                        out string collectResult))
                {
                    throw new InvalidOperationException(collectResult);
                }

                network.AddInventoryAll(
                    binding.ItemDefinitionId,
                    binding.Quantity);
            }

            int collectedResourceTypes = session.CollectedResources
                .Select(resource => resource.DefinitionId)
                .Distinct(StringComparer.Ordinal)
                .Count();
            ResourceNodeBinding duplicateBinding = orderedBindings[0];
            bool duplicateRejected = !session.TryCollect(
                duplicateBinding.ResourceNodeId,
                duplicateBinding.ItemDefinitionId,
                duplicateBinding.Quantity,
                out _);
            bool mirrorsAfterCollection = network.Queues.All(queue =>
                physicalYieldByDefinition.All(pair =>
                    queue.GetQuantity(pair.Key) == pair.Value));

            CraftingStackDefinition[] consumedStacks = catalog.Resources.Values
                .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
                .Where((_, index) => index % 4 == 0)
                .Select(resource => new CraftingStackDefinition(
                    resource.ItemDefinitionId,
                    physicalYieldByDefinition[resource.ItemDefinitionId]))
                .ToArray();
            foreach (CraftingStackDefinition stack in consumedStacks)
            {
                if (!session.TryConsumeInventory(
                        stack.DefinitionId,
                        stack.Quantity,
                        out string sessionConsume))
                {
                    throw new InvalidOperationException(sessionConsume);
                }

                if (!network.TryConsumeInventoryAll(
                        stack.DefinitionId,
                        stack.Quantity,
                        out string mirrorConsume))
                {
                    throw new InvalidOperationException(mirrorConsume);
                }
            }

            bool inventoryMirrorsSynchronized = mirrorsAfterCollection &&
                network.Queues.All(queue =>
                    catalog.Resources.Values.All(resource =>
                        queue.GetQuantity(resource.ItemDefinitionId) ==
                        session.GetAvailableQuantity(resource.ItemDefinitionId)));
            bool depletionBeforeSave = consumedStacks.All(stack =>
                session.GetAvailableQuantity(stack.DefinitionId) == 0);

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 5.5,
                productionQueueNetwork: network.CreateSaveData());
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
            if (loaded is null)
            {
                throw new InvalidOperationException(
                    "Resource lifecycle snapshot was not restored.");
            }

            IReadOnlyDictionary<string, ResourceNodeBinding> bindingsByNode =
                resourceBindings.ToDictionary(
                    binding => binding.ResourceNodeId,
                    StringComparer.Ordinal);
            StarterRepairSession restoredSession =
                StarterRepairSession.FromSnapshot(
                    loaded,
                    bindingsByNode,
                    repairRecipe,
                    static _ => true,
                    recipes);
            ProductionNetworkRuntime restoredNetwork =
                ProductionNetworkRuntime.Create(
                    catalog.Stations,
                    catalog.Recipes,
                    activeStationIds,
                    restoredSession.AvailableInventory,
                    saveData: loaded.ProductionQueueNetwork,
                    legacySaveData: loaded.ProductionQueue);

            HashSet<string> depletedDefinitions = consumedStacks
                .Select(stack => stack.DefinitionId)
                .ToHashSet(StringComparer.Ordinal);
            bool depletionPersisted = depletionBeforeSave &&
                consumedStacks.All(stack =>
                    restoredSession.GetAvailableQuantity(
                        stack.DefinitionId) == 0) &&
                orderedBindings
                    .Where(binding => depletedDefinitions.Contains(
                        binding.ItemDefinitionId))
                    .All(binding => restoredSession.CollectedNodeIds.Contains(
                        binding.ResourceNodeId));
            bool coldRestoreExact =
                restoredSession.CollectedResources.SequenceEqual(
                    session.CollectedResources) &&
                restoredSession.AvailableInventory.SequenceEqual(
                    session.AvailableInventory) &&
                restoredNetwork.Queues.All(queue =>
                    catalog.Resources.Values.All(resource =>
                        queue.GetQuantity(resource.ItemDefinitionId) ==
                        restoredSession.GetAvailableQuantity(
                            resource.ItemDefinitionId)));

            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten =
                logText.Contains("AUTOSAVE_COMPLETED", StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.BaseChanged),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);

            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            SaveGameSnapshot? afterReset = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            StarterRepairSession resetSession = StarterRepairSession.FromSnapshot(
                afterReset,
                bindingsByNode,
                repairRecipe,
                static _ => true,
                recipes);
            bool resetReady = afterReset is null &&
                resetSession.CollectedNodeCount == 0 &&
                resetSession.AvailableInventory.Count == 0;

            bool passed =
                catalog.Resources.Count == ExpectedCatalogResources &&
                completePhysicalCoverage &&
                physicalResourceTypes == catalog.Resources.Count &&
                resourceBindings.Count == ExpectedTotalNodes &&
                generatedPlacements.Count == ExpectedGeneratedNodes &&
                resourceBindings.Count - generatedPlacements.Count ==
                    ExpectedAuthoredNodes &&
                catalogMetadataValid &&
                deterministicPlacement &&
                uniqueNodes &&
                collectedResourceTypes == catalog.Resources.Count &&
                session.CollectedNodeCount == resourceBindings.Count &&
                duplicateRejected &&
                inventoryMirrorsSynchronized &&
                depletionPersisted &&
                coldRestoreExact &&
                resetReady &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.SchemaVersion == SaveDatabase.CurrentSchemaVersion &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk;

            List<string> failures = new();
            if (catalog.Resources.Count != ExpectedCatalogResources)
                failures.Add($"catalog={catalog.Resources.Count}");
            if (resourceBindings.Count != ExpectedTotalNodes)
                failures.Add($"nodes={resourceBindings.Count}");
            if (generatedPlacements.Count != ExpectedGeneratedNodes)
                failures.Add($"generated={generatedPlacements.Count}");
            if (resourceBindings.Count - generatedPlacements.Count !=
                ExpectedAuthoredNodes)
            {
                failures.Add(
                    $"authored={resourceBindings.Count - generatedPlacements.Count}");
            }
            if (!completePhysicalCoverage)
                failures.Add($"physical={physicalResourceTypes}");
            if (!catalogMetadataValid)
                failures.Add("metadata=0");
            if (!deterministicPlacement)
                failures.Add("placement=0");
            if (!uniqueNodes)
                failures.Add("unique=0");
            if (collectedResourceTypes != catalog.Resources.Count)
                failures.Add($"collectedTypes={collectedResourceTypes}");
            if (session.CollectedNodeCount != resourceBindings.Count)
                failures.Add($"collectedNodes={session.CollectedNodeCount}");
            if (!duplicateRejected)
                failures.Add("duplicate=0");
            if (!inventoryMirrorsSynchronized)
                failures.Add("mirrors=0");
            if (!depletionPersisted)
                failures.Add("depletion=0");
            if (!coldRestoreExact)
                failures.Add("restore=0");
            if (!resetReady)
                failures.Add("reset=0");
            if (!exactRoundTrip)
                failures.Add($"roundTrip={mismatch}");
            if (!logWritten)
                failures.Add("log=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add($"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new CatalogResourceLifecycleAcceptanceReport(
                passed,
                passed
                    ? "all catalog world resources and all physical nodes are collectable, mirrored, depleted, restored and reset through one generic lifecycle"
                    : "resource lifecycle criteria failed: " +
                      string.Join(", ", failures),
                catalog.Resources.Count,
                physicalResourceTypes,
                resourceBindings.Count,
                generatedPlacements.Count,
                collectedResourceTypes,
                session.CollectedNodeCount,
                catalogMetadataValid,
                deterministicPlacement,
                uniqueNodes,
                duplicateRejected,
                inventoryMirrorsSynchronized,
                depletionPersisted,
                coldRestoreExact,
                resetReady,
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
            return new CatalogResourceLifecycleAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                catalog.Resources.Count,
                0,
                resourceBindings.Count,
                generatedPlacements.Count,
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
                new SaveDatabaseDiagnostics(
                    0,
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
                    0),
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
    }
}
