using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public enum StarterRepairResult
{
    Repaired = 0,
    AlreadyRepaired = 1,
    InsufficientSalvage = 2
}

public sealed record ResourceNodeBinding(
    string ResourceNodeId,
    string ItemDefinitionId,
    int Quantity);

public sealed record CollectedResourceState(
    string ResourceNodeId,
    string DefinitionId,
    int CollectedQuantity,
    int RemainingQuantity);

public sealed class StarterRepairSession
{
    private sealed class MutableCollectedResourceState
    {
        public MutableCollectedResourceState(
            string resourceNodeId,
            string definitionId,
            int collectedQuantity,
            int remainingQuantity)
        {
            ResourceNodeId = resourceNodeId;
            DefinitionId = definitionId;
            CollectedQuantity = collectedQuantity;
            RemainingQuantity = remainingQuantity;
        }

        public string ResourceNodeId { get; }

        public string DefinitionId { get; }

        public int CollectedQuantity { get; }

        public int RemainingQuantity { get; set; }
    }

    private readonly Dictionary<string, MutableCollectedResourceState>
        _collectedResources = new(StringComparer.Ordinal);
    private readonly CraftingRecipeDefinition _repairRecipe;
    private IReadOnlyList<CraftingStackDefinition> _lastCraftedOutputs =
        Array.Empty<CraftingStackDefinition>();

    public StarterRepairSession(CraftingRecipeDefinition repairRecipe)
    {
        ArgumentNullException.ThrowIfNull(repairRecipe);
        if (repairRecipe.Inputs.Count == 0)
        {
            throw new ArgumentException(
                "Starter repair recipe must contain at least one input.",
                nameof(repairRecipe));
        }

        if (!string.Equals(
            repairRecipe.Application.Type,
            "RepairShip",
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Starter repair recipe must use RepairShip application.",
                nameof(repairRecipe));
        }

        _repairRecipe = repairRecipe;
    }

    public CraftingRecipeDefinition RepairRecipe => _repairRecipe;

    public string SalvageDefinitionId => _repairRecipe.Inputs[0].DefinitionId;

    public int RequiredSalvage => _repairRecipe.Inputs[0].Quantity;

    public int SalvageQuantity => GetAvailableQuantity(SalvageDefinitionId);

    public bool ShipRepaired { get; private set; }

    public int CollectedNodeCount => _collectedResources.Count;

    public IReadOnlyCollection<string> CollectedNodeIds =>
        _collectedResources.Keys;

    public IReadOnlyList<CollectedResourceState> CollectedResources =>
        _collectedResources.Values
            .OrderBy(state => state.ResourceNodeId, StringComparer.Ordinal)
            .Select(state => new CollectedResourceState(
                state.ResourceNodeId,
                state.DefinitionId,
                state.CollectedQuantity,
                state.RemainingQuantity))
            .ToArray();

    public IReadOnlyList<CraftingStackDefinition> LastCraftedOutputs =>
        _lastCraftedOutputs;

    public double RepairedHealth => _repairRecipe.Application.ResultHealth;

    public bool TryCollect(
        string resourceNodeId,
        string definitionId,
        int quantity,
        out string result)
    {
        if (string.IsNullOrWhiteSpace(resourceNodeId))
        {
            throw new ArgumentException(
                "Resource node ID must not be empty.",
                nameof(resourceNodeId));
        }

        if (!GameContentCatalog.IsStableId(definitionId))
        {
            throw new ArgumentException(
                "Resource definition ID must be a stable dotted string ID.",
                nameof(definitionId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Collected quantity must be positive.");
        }

        if (ShipRepaired)
        {
            result = "repair objective already completed";
            return false;
        }

        if (_collectedResources.ContainsKey(resourceNodeId))
        {
            result = $"resource node {resourceNodeId} was already collected";
            return false;
        }

        _collectedResources.Add(
            resourceNodeId,
            new MutableCollectedResourceState(
                resourceNodeId,
                definitionId,
                quantity,
                quantity));
        result = string.Equals(
            definitionId,
            SalvageDefinitionId,
            StringComparison.Ordinal)
            ? $"salvage {SalvageQuantity}/{RequiredSalvage}"
            : $"collected {quantity} x {definitionId}";
        return true;
    }

    public StarterRepairResult TryRepair(out string result)
    {
        if (ShipRepaired)
        {
            result = "ship already repaired";
            return StarterRepairResult.AlreadyRepaired;
        }

        IReadOnlyList<CraftingStackDefinition> missing =
            GetMissingRecipeInputs();
        if (missing.Count > 0)
        {
            result = "missing " + string.Join(
                ", ",
                missing.Select(input =>
                    $"{input.Quantity} x {input.DefinitionId}"));
            return StarterRepairResult.InsufficientSalvage;
        }

        foreach (CraftingStackDefinition input in _repairRecipe.Inputs)
        {
            Consume(input.DefinitionId, input.Quantity);
        }

        _lastCraftedOutputs = _repairRecipe.Outputs
            .Select(output => new CraftingStackDefinition(
                output.DefinitionId,
                output.Quantity))
            .ToArray();
        ShipRepaired = true;
        result =
            $"recipe {_repairRecipe.RecipeId} crafted and applied; " +
            $"ship health={RepairedHealth.ToString("0.0", CultureInfo.InvariantCulture)}";
        return StarterRepairResult.Repaired;
    }

    public int GetAvailableQuantity(string definitionId)
    {
        return _collectedResources.Values
            .Where(state => string.Equals(
                state.DefinitionId,
                definitionId,
                StringComparison.Ordinal))
            .Sum(state => state.RemainingQuantity);
    }

    public static StarterRepairSession FromSnapshot(
        SaveGameSnapshot? snapshot,
        IReadOnlyDictionary<string, ResourceNodeBinding> resourceBindings,
        CraftingRecipeDefinition repairRecipe)
    {
        ArgumentNullException.ThrowIfNull(resourceBindings);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        StarterRepairSession session = new(repairRecipe);
        if (snapshot is null)
        {
            return session;
        }

        foreach (InventoryItemSaveData item in snapshot.Inventory)
        {
            const string itemPrefix = "item.";
            if (!item.ItemId.StartsWith(itemPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string nodeId = item.ItemId[itemPrefix.Length..];
            if (!resourceBindings.TryGetValue(
                nodeId,
                out ResourceNodeBinding binding))
            {
                continue;
            }

            if (!string.Equals(
                item.DefinitionId,
                binding.ItemDefinitionId,
                StringComparison.Ordinal))
            {
                continue;
            }

            int remainingQuantity = Math.Clamp(
                item.Quantity,
                0,
                binding.Quantity);
            session._collectedResources.Add(
                nodeId,
                new MutableCollectedResourceState(
                    nodeId,
                    binding.ItemDefinitionId,
                    binding.Quantity,
                    remainingQuantity));
        }

        session.ShipRepaired = snapshot.Ship.Health >=
            repairRecipe.Application.ResultHealth - 0.001;
        if (session.ShipRepaired && session._collectedResources.Count == 0)
        {
            foreach (ResourceNodeBinding binding in resourceBindings.Values)
            {
                session._collectedResources.Add(
                    binding.ResourceNodeId,
                    new MutableCollectedResourceState(
                        binding.ResourceNodeId,
                        binding.ItemDefinitionId,
                        binding.Quantity,
                        0));
            }
        }

        return session;
    }

    private IReadOnlyList<CraftingStackDefinition> GetMissingRecipeInputs()
    {
        List<CraftingStackDefinition> missing = new();
        foreach (CraftingStackDefinition input in _repairRecipe.Inputs)
        {
            int available = GetAvailableQuantity(input.DefinitionId);
            if (available < input.Quantity)
            {
                missing.Add(input with
                {
                    Quantity = input.Quantity - available
                });
            }
        }

        return missing;
    }

    private void Consume(string definitionId, int quantity)
    {
        int remaining = quantity;
        foreach (MutableCollectedResourceState state in
            _collectedResources.Values
                .Where(state => string.Equals(
                    state.DefinitionId,
                    definitionId,
                    StringComparison.Ordinal))
                .OrderBy(state => state.ResourceNodeId, StringComparer.Ordinal))
        {
            if (remaining == 0)
            {
                break;
            }

            int consumed = Math.Min(state.RemainingQuantity, remaining);
            state.RemainingQuantity -= consumed;
            remaining -= consumed;
        }

        if (remaining != 0)
        {
            throw new InvalidOperationException(
                $"Recipe consumption underflow for {definitionId}: " +
                $"remaining={remaining}.");
        }
    }
}

public static class StarterRepairSnapshotFactory
{
    public const string SlotId = "save_1";
    public const string PlanetId = "planet.vertical_slice";
    public const string SystemId = "system.vertical_slice";

    public static SaveGameSnapshot Create(
        string slotId,
        int revision,
        StarterRepairSession session,
        double playerPositionX,
        double playerPositionY,
        double playerPositionZ)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        ArgumentNullException.ThrowIfNull(session);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                "Revision must be positive.");
        }

        string updatedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        return new SaveGameSnapshot(
            slotId,
            revision,
            GeneratorVersion: 1,
            ContentVersion: SaveDatabase.CurrentContentVersion,
            updatedUtc,
            new PlayerSaveData(
                "player.vertical_slice",
                playerPositionX,
                playerPositionY,
                playerPositionZ,
                PlanetId),
            new ShipSaveData(
                "ship.starter",
                session.RepairRecipe.Application.TargetId,
                "Horizon Starter",
                session.ShipRepaired ? session.RepairedHealth : 28.0,
                35.0,
                0.0,
                1.0,
                -10.0),
            session.CollectedResources
                .Select(resource => new InventoryItemSaveData(
                    $"item.{resource.ResourceNodeId}",
                    resource.DefinitionId,
                    resource.RemainingQuantity,
                    1.0))
                .ToArray(),
            new VisitedPlanetSaveData(
                PlanetId,
                SystemId,
                updatedUtc,
                1));
    }
}

public sealed record VerticalSliceAcceptanceReport(
    bool Passed,
    string Result,
    int ResourcesCollected,
    bool RepairBlockedBeforeResources,
    bool ShipRepaired,
    bool QuestAutosaveObserved,
    bool ExactRoundTrip,
    bool LogWritten,
    int Revision,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class VerticalSliceAcceptanceRunner
{
    public static async Task<VerticalSliceAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        CraftingRecipeDefinition repairRecipe,
        IReadOnlyList<ResourceNodeBinding> resourceBindings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Database path must not be empty.",
                nameof(databasePath));
        }

        ArgumentNullException.ThrowIfNull(repairRecipe);
        ArgumentNullException.ThrowIfNull(resourceBindings);
        System.Diagnostics.Stopwatch stopwatch =
            System.Diagnostics.Stopwatch.StartNew();
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

            StarterRepairSession session = new(repairRecipe);
            StarterRepairResult blockedResult = session.TryRepair(out _);
            bool repairBlocked =
                blockedResult == StarterRepairResult.InsufficientSalvage;

            foreach (ResourceNodeBinding binding in resourceBindings)
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

            StarterRepairResult repairResult = session.TryRepair(out _);
            bool shipRepaired = repairResult == StarterRepairResult.Repaired;
            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 0.0,
                playerPositionY: 1.0,
                playerPositionZ: 4.0);
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
            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);

            bool questAutosaveObserved =
                autosave.HasObservedTrigger(AutosaveTrigger.QuestCompleted) &&
                autosave.CompletedBatches == 1 &&
                autosave.RequestedSaves == 1 &&
                autosave.FailedBatches == 0 &&
                autosave.LastCompletedTriggerSummary.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten =
                logText.Contains("AUTOSAVE_COMPLETED", StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool allRecipeInputsConsumed = repairRecipe.Inputs.All(input =>
                session.GetAvailableQuantity(input.DefinitionId) == 0);
            bool outputsProduced =
                session.LastCraftedOutputs.SequenceEqual(repairRecipe.Outputs);
            bool passed =
                repairBlocked &&
                session.CollectedNodeCount == resourceBindings.Count &&
                allRecipeInputsConsumed &&
                outputsProduced &&
                shipRepaired &&
                questAutosaveObserved &&
                exactRoundTrip &&
                logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityOk &&
                loaded?.Ship.Health == repairRecipe.Application.ResultHealth;

            List<string> failedCriteria = new();
            if (!repairBlocked)
            {
                failedCriteria.Add("repairBlocked=0");
            }

            if (session.CollectedNodeCount != resourceBindings.Count)
            {
                failedCriteria.Add(
                    $"resources={session.CollectedNodeCount}");
            }

            if (!allRecipeInputsConsumed)
            {
                failedCriteria.Add("inputsConsumed=0");
            }

            if (!outputsProduced)
            {
                failedCriteria.Add("outputsProduced=0");
            }

            if (!shipRepaired)
            {
                failedCriteria.Add("shipRepaired=0");
            }

            if (!questAutosaveObserved)
            {
                failedCriteria.Add("questAutosave=0");
            }

            if (!exactRoundTrip)
            {
                failedCriteria.Add($"roundTrip={mismatch}");
            }

            if (!logWritten)
            {
                failedCriteria.Add("logWritten=0");
            }

            if (diagnostics.MaximumConcurrentWriters != 1)
            {
                failedCriteria.Add(
                    $"maxWriters={diagnostics.MaximumConcurrentWriters}");
            }

            if (!integrityOk)
            {
                failedCriteria.Add($"integrity={diagnostics.IntegrityResult}");
            }

            stopwatch.Stop();
            return new VerticalSliceAcceptanceReport(
                passed,
                passed
                    ? "data-driven resource collection crafted the starter repair recipe and persisted the repaired ship"
                    : $"vertical-slice criteria failed: {string.Join(", ", failedCriteria)}",
                session.CollectedNodeCount,
                repairBlocked,
                shipRepaired,
                questAutosaveObserved,
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
            return new VerticalSliceAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                0,
                false,
                false,
                false,
                false,
                false,
                0,
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
