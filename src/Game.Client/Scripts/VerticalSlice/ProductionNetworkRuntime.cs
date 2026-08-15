using System;
using System.Collections.Generic;
using System.Linq;

public sealed record StationProductionAdvance(
    string StationId,
    ProductionQueueAdvanceReport Report);

public sealed class ProductionNetworkRuntime
{
    private readonly Dictionary<string, ProductionQueueRuntime> _queues =
        new(StringComparer.Ordinal);

    private ProductionNetworkRuntime()
    {
    }

    public IReadOnlyList<string> StationIds => _queues.Keys
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<ProductionQueueRuntime> Queues => _queues.Values
        .OrderBy(queue => queue.StationId, StringComparer.Ordinal)
        .ToArray();

    public int TotalJobs => _queues.Values.Sum(queue => queue.Jobs.Count);

    public ProductionQueueRuntime GetQueue(string stationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        return _queues.TryGetValue(stationId, out ProductionQueueRuntime? queue) &&
            queue is not null
            ? queue
            : throw new InvalidOperationException(
                $"Gameplay production station {stationId} is unavailable.");
    }

    public static ProductionNetworkRuntime Create(
        IReadOnlyDictionary<string, CraftingStationDefinition> stations,
        IReadOnlyDictionary<string, CraftingRecipeDefinition> recipes,
        IEnumerable<string> activeStationIds,
        IReadOnlyList<CraftingStackDefinition> freeInventory,
        Func<string, bool>? isTechnologyUnlocked = null,
        ProductionQueueNetworkSaveData? saveData = null,
        ProductionQueueSaveData? legacySaveData = null)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(activeStationIds);
        ArgumentNullException.ThrowIfNull(freeInventory);

        string[] stationIds = activeStationIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (stationIds.Length == 0)
        {
            throw new InvalidOperationException(
                "Production network requires at least one active station.");
        }

        Dictionary<string, ProductionQueueSaveData> savedByStation =
            new(StringComparer.Ordinal);
        if (saveData is not null)
        {
            foreach (ProductionQueueSaveData saved in saveData.Stations)
            {
                if (!savedByStation.TryAdd(saved.StationId, saved))
                {
                    throw new InvalidOperationException(
                        $"Production network contains duplicate station " +
                        $"{saved.StationId}.");
                }
            }
        }

        if (legacySaveData is not null &&
            !savedByStation.ContainsKey(legacySaveData.StationId))
        {
            savedByStation.Add(legacySaveData.StationId, legacySaveData);
        }

        ProductionNetworkRuntime network = new();
        foreach (string stationId in stationIds)
        {
            if (!stations.TryGetValue(
                    stationId,
                    out CraftingStationDefinition? station) ||
                station is null)
            {
                throw new InvalidOperationException(
                    $"Production network references unknown station {stationId}.");
            }

            ProductionQueueRuntime queue = savedByStation.TryGetValue(
                    stationId,
                    out ProductionQueueSaveData? savedQueue) &&
                savedQueue is not null
                ? ProductionQueueRuntime.Restore(
                    station,
                    recipes,
                    savedQueue,
                    freeInventory,
                    isTechnologyUnlocked)
                : CreateFreshQueue(
                    station,
                    recipes,
                    freeInventory,
                    isTechnologyUnlocked);
            network._queues.Add(stationId, queue);
        }

        string[] unavailableSavedStations = savedByStation.Keys
            .Except(stationIds, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (unavailableSavedStations.Length > 0)
        {
            throw new InvalidOperationException(
                "Saved production network references unavailable stations: " +
                string.Join(", ", unavailableSavedStations));
        }

        return network;
    }

    public ProductionQueueNetworkSaveData CreateSaveData()
    {
        return new ProductionQueueNetworkSaveData(
            Queues.Select(queue => queue.CreateSaveData()).ToArray());
    }

    public void AddInventoryAll(string definitionId, int quantity)
    {
        foreach (ProductionQueueRuntime queue in _queues.Values)
        {
            queue.AddInventory(definitionId, quantity);
        }
    }

    public void AddInventoryAllExcept(
        string excludedStationId,
        string definitionId,
        int quantity)
    {
        foreach (ProductionQueueRuntime queue in _queues.Values)
        {
            if (!string.Equals(
                    queue.StationId,
                    excludedStationId,
                    StringComparison.Ordinal))
            {
                queue.AddInventory(definitionId, quantity);
            }
        }
    }

    public bool TryConsumeInventoryAll(
        string definitionId,
        int quantity,
        out string result)
    {
        return TryConsumeInventoryAllExcept(
            null,
            definitionId,
            quantity,
            out result);
    }

    public bool TryConsumeInventoryAllExcept(
        string? excludedStationId,
        string definitionId,
        int quantity,
        out string result)
    {
        ProductionQueueRuntime[] targets = _queues.Values
            .Where(queue => excludedStationId is null ||
                !string.Equals(
                    queue.StationId,
                    excludedStationId,
                    StringComparison.Ordinal))
            .ToArray();
        ProductionQueueRuntime? missing = targets.FirstOrDefault(
            queue => queue.GetQuantity(definitionId) < quantity);
        if (missing is not null)
        {
            result = GameLocalizationService.Format(
                "ui.industry.network_missing",
                ("station", missing.StationId),
                ("quantity", quantity - missing.GetQuantity(definitionId)),
                ("item", definitionId));
            return false;
        }

        foreach (ProductionQueueRuntime queue in targets)
        {
            if (!queue.TryConsumeInventory(
                    definitionId,
                    quantity,
                    out string consumeResult))
            {
                throw new InvalidOperationException(
                    $"Production network preflight diverged: {consumeResult}.");
            }
        }

        result = GameLocalizationService.Format(
            "ui.industry.network_consumed",
            ("quantity", quantity), ("item", definitionId), ("stations", targets.Length));
        return true;
    }

    public IReadOnlyList<StationProductionAdvance> Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                "Production network elapsed time must be finite and non-negative.");
        }

        return Queues
            .Where(queue => queue.Jobs.Count > 0)
            .Select(queue => new StationProductionAdvance(
                queue.StationId,
                queue.Advance(elapsedSeconds)))
            .ToArray();
    }

    public double RechargeAll(double elapsedSeconds, double fullRechargeSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        if (!double.IsFinite(fullRechargeSeconds) || fullRechargeSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(fullRechargeSeconds));
        }

        double restored = 0.0;
        foreach (ProductionQueueRuntime queue in _queues.Values)
        {
            restored += queue.RechargeEnergy(
                queue.EnergyCapacity / fullRechargeSeconds * elapsedSeconds);
        }

        return restored;
    }

    private static ProductionQueueRuntime CreateFreshQueue(
        CraftingStationDefinition station,
        IReadOnlyDictionary<string, CraftingRecipeDefinition> recipes,
        IReadOnlyList<CraftingStackDefinition> freeInventory,
        Func<string, bool>? isTechnologyUnlocked)
    {
        ProductionQueueRuntime queue = new(
            station,
            recipes,
            station.EnergyCapacity,
            isTechnologyUnlocked);
        foreach (CraftingStackDefinition stack in freeInventory)
        {
            queue.AddInventory(stack.DefinitionId, stack.Quantity);
        }

        return queue;
    }
}
