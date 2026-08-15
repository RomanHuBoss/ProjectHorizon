using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public enum BasePlacementResult
{
    Placed = 0,
    UnknownModule = 1,
    OutOfStock = 2,
    AnchorRequired = 3,
    Overlap = 4,
    NotSnapped = 5,
    LimitExceeded = 6
}

public sealed record BaseModulePlacement(
    string InstanceId,
    string ModuleId,
    int GridX,
    int GridZ,
    int RotationQuarterTurns,
    bool Enabled);

public sealed record BasePowerNetworkSnapshot(
    int Modules,
    int InteractiveDevices,
    int ActivePhysicsObjects,
    int DynamicLights,
    int ConnectedComponents,
    double Generation,
    double Consumption,
    double BatteryStored,
    double BatteryCapacity,
    int EnabledConsumers,
    int PoweredConsumers,
    bool HasPowerDeficit);

public sealed class BaseConstructionRuntime
{
    public const string DefaultBaseId = "base.vertical_slice.alpha";

    private readonly BaseConstructionCatalog _catalog;
    private readonly Dictionary<string, BaseModulePlacement> _placements =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stock =
        new(StringComparer.Ordinal);
    private long _nextSequence = 1;
    private double _storedEnergy;

    public BaseConstructionRuntime(
        BaseConstructionCatalog catalog,
        BaseConstructionSaveData? saveData = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        BaseId = saveData?.BaseId ?? DefaultBaseId;
        if (!GameContentCatalog.IsStableId(BaseId))
        {
            throw new InvalidOperationException(
                $"Invalid base construction ID {BaseId}.");
        }

        foreach (BaseModuleDefinition definition in catalog.Modules.Values)
        {
            _stock.Add(definition.ModuleId, definition.StarterStock);
        }

        if (saveData is not null)
        {
            Restore(saveData);
        }

        RecomputePower(0.0);
    }

    public string BaseId { get; }

    public BaseConstructionCatalog Catalog => _catalog;

    public IReadOnlyList<BaseModulePlacement> Placements => _placements.Values
        .OrderBy(placement => placement.InstanceId, StringComparer.Ordinal)
        .ToArray();

    public int ModuleCount => _placements.Count;

    public double StoredEnergy => _storedEnergy;

    public BasePowerNetworkSnapshot Power { get; private set; } = new(
        0, 0, 0, 0, 0, 0.0, 0.0, 0.0, 0.0, 0, 0, false);

    public int GetStock(string moduleId)
    {
        return _stock.TryGetValue(moduleId, out int quantity) ? quantity : 0;
    }

    public BasePlacementResult TryPlace(
        string moduleId,
        int gridX,
        int gridZ,
        int rotationQuarterTurns,
        out BaseModulePlacement? placement,
        out string result)
    {
        placement = null;
        if (!_catalog.Modules.TryGetValue(
                moduleId,
                out BaseModuleDefinition? definition))
        {
            result = GameLocalizationService.Format("ui.base.unknown_module", ("module", moduleId));
            return BasePlacementResult.UnknownModule;
        }

        if (GetStock(moduleId) <= 0)
        {
            result = GameLocalizationService.Format("ui.base.out_of_stock", ("module", moduleId));
            return BasePlacementResult.OutOfStock;
        }

        if (_placements.Count == 0 && !definition.IsAnchor)
        {
            result = GameLocalizationService.Text("ui.base.anchor_required");
            return BasePlacementResult.AnchorRequired;
        }

        if (FindAt(gridX, gridZ) is not null)
        {
            result = GameLocalizationService.Format("ui.base.cell_occupied", ("x", gridX), ("z", gridZ));
            return BasePlacementResult.Overlap;
        }

        if (_placements.Count > 0 && !HasAdjacentModule(gridX, gridZ))
        {
            result = GameLocalizationService.Text("ui.base.snap_required");
            return BasePlacementResult.NotSnapped;
        }

        if (WouldExceedLimits(definition))
        {
            result = GameLocalizationService.Text("ui.base.limit_exceeded");
            return BasePlacementResult.LimitExceeded;
        }

        int normalizedRotation = ((rotationQuarterTurns % 4) + 4) % 4;
        string instanceId = $"base.module.{_nextSequence:000000}";
        _nextSequence++;
        placement = new BaseModulePlacement(
            instanceId,
            moduleId,
            gridX,
            gridZ,
            normalizedRotation,
            Enabled: true);
        _placements.Add(instanceId, placement);
        _stock[moduleId]--;
        RecomputePower(0.0);
        result = GameLocalizationService.Format("ui.base.placed", ("module", moduleId), ("x", gridX), ("z", gridZ));
        return BasePlacementResult.Placed;
    }

    public bool TryRemove(string instanceId, out string result)
    {
        if (!_placements.TryGetValue(
                instanceId,
                out BaseModulePlacement? placement))
        {
            result = GameLocalizationService.Format("ui.base.unknown_instance", ("instance", instanceId));
            return false;
        }

        _placements.Remove(instanceId);
        if (_placements.Count > 0 &&
            (!HasAnchor() || CountConnectedComponents() != 1))
        {
            _placements.Add(instanceId, placement);
            result = GameLocalizationService.Text("ui.base.disconnect");
            return false;
        }

        _stock[placement.ModuleId] = GetStock(placement.ModuleId) + 1;
        RecomputePower(0.0);
        result = GameLocalizationService.Format("ui.base.removed", ("module", placement.ModuleId));
        return true;
    }

    public bool TryToggle(string instanceId, out string result)
    {
        if (!_placements.TryGetValue(
                instanceId,
                out BaseModulePlacement? placement))
        {
            result = GameLocalizationService.Format("ui.base.unknown_instance", ("instance", instanceId));
            return false;
        }

        BaseModuleDefinition definition = _catalog.GetModule(
            placement.ModuleId);
        bool isDevice = definition.InteractiveDevices > 0 ||
            definition.PowerGeneration > 0.0 ||
            definition.PowerConsumption > 0.0 ||
            definition.BatteryCapacity > 0.0;
        if (!isDevice)
        {
            result = GameLocalizationService.Format("ui.base.no_switch", ("module", placement.ModuleId));
            return false;
        }

        BaseModulePlacement updated = placement with
        {
            Enabled = !placement.Enabled
        };
        _placements[instanceId] = updated;
        RecomputePower(0.0);
        result = GameLocalizationService.Format(
            updated.Enabled ? "ui.base.enabled" : "ui.base.disabled",
            ("module", updated.ModuleId));
        return true;
    }

    public BaseModulePlacement? FindAt(int gridX, int gridZ)
    {
        return _placements.Values.FirstOrDefault(placement =>
            placement.GridX == gridX && placement.GridZ == gridZ);
    }

    public void Tick(double deltaSeconds)
    {
        if (deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        RecomputePower(deltaSeconds);
    }

    public BaseConstructionSaveData CreateSaveData()
    {
        return new BaseConstructionSaveData(
            BaseId,
            _nextSequence,
            _storedEnergy,
            _stock
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new BaseConstructionStockSaveData(
                    pair.Key,
                    pair.Value))
                .ToArray(),
            Placements
                .Select(placement => new BaseConstructionModuleSaveData(
                    placement.InstanceId,
                    placement.ModuleId,
                    placement.GridX,
                    placement.GridZ,
                    placement.RotationQuarterTurns,
                    placement.Enabled))
                .ToArray());
    }

    public string BuildSummary()
    {
        BasePowerNetworkSnapshot power = Power;
        return $"modules={power.Modules}/{_catalog.Limits.MaximumModules} • " +
            $"devices={power.InteractiveDevices}/" +
            $"{_catalog.Limits.MaximumInteractiveDevices} • " +
            $"power={power.Generation.ToString("0.#", CultureInfo.InvariantCulture)}/" +
            $"{power.Consumption.ToString("0.#", CultureInfo.InvariantCulture)} • " +
            $"battery={power.BatteryStored.ToString("0.#", CultureInfo.InvariantCulture)}/" +
            $"{power.BatteryCapacity.ToString("0.#", CultureInfo.InvariantCulture)} • " +
            $"components={power.ConnectedComponents}";
    }

    private void Restore(BaseConstructionSaveData saveData)
    {
        if (!string.Equals(saveData.BaseId, BaseId, StringComparison.Ordinal) ||
            saveData.NextSequence <= 0 ||
            saveData.StoredEnergy < 0.0 ||
            saveData.Stock is null ||
            saveData.Modules is null)
        {
            throw new InvalidOperationException(
                "Invalid base construction save data.");
        }

        HashSet<string> restoredStockIds = new(StringComparer.Ordinal);
        foreach (BaseConstructionStockSaveData stock in saveData.Stock)
        {
            if (!_catalog.Modules.ContainsKey(stock.ModuleId) ||
                stock.Quantity < 0 ||
                !restoredStockIds.Add(stock.ModuleId))
            {
                throw new InvalidOperationException(
                    $"Invalid or duplicate base stock entry {stock.ModuleId}.");
            }

            _stock[stock.ModuleId] = stock.Quantity;
        }

        if (restoredStockIds.Count != _catalog.Modules.Count)
        {
            throw new InvalidOperationException(
                "Base construction save must contain one stock entry for every catalog module.");
        }

        foreach (BaseConstructionModuleSaveData module in saveData.Modules)
        {
            if (!GameContentCatalog.IsStableId(module.InstanceId) ||
                !_catalog.Modules.ContainsKey(module.ModuleId) ||
                module.RotationQuarterTurns is < 0 or > 3 ||
                _placements.ContainsKey(module.InstanceId) ||
                FindAt(module.GridX, module.GridZ) is not null)
            {
                throw new InvalidOperationException(
                    $"Invalid or duplicate base module {module.InstanceId}.");
            }

            _placements.Add(
                module.InstanceId,
                new BaseModulePlacement(
                    module.InstanceId,
                    module.ModuleId,
                    module.GridX,
                    module.GridZ,
                    module.RotationQuarterTurns,
                    module.Enabled));
        }

        long highestSequence = _placements.Keys
            .Select(ParseSequence)
            .DefaultIfEmpty(0)
            .Max();
        if (saveData.NextSequence <= highestSequence)
        {
            throw new InvalidOperationException(
                "Base construction next sequence must exceed every restored instance sequence.");
        }

        _nextSequence = saveData.NextSequence;
        _storedEnergy = saveData.StoredEnergy;
        if (_placements.Count > 0 &&
            (!HasAnchor() || CountConnectedComponents() != 1))
        {
            throw new InvalidOperationException(
                "Restored base construction graph is disconnected or lacks an anchor.");
        }

        BasePowerNetworkSnapshot counts = CalculatePowerSnapshot();
        if (counts.Modules > _catalog.Limits.MaximumModules ||
            counts.InteractiveDevices >
                _catalog.Limits.MaximumInteractiveDevices ||
            counts.ActivePhysicsObjects >
                _catalog.Limits.MaximumActivePhysicsObjects ||
            counts.DynamicLights > _catalog.Limits.MaximumDynamicLights)
        {
            throw new InvalidOperationException(
                "Restored base construction state exceeds configured limits.");
        }
    }


    private static long ParseSequence(string instanceId)
    {
        int separator = instanceId.LastIndexOf(".", StringComparison.Ordinal);
        if (separator < 0 ||
            !long.TryParse(
                instanceId[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long sequence))
        {
            throw new InvalidOperationException(
                $"Base module instance {instanceId} lacks a numeric sequence.");
        }

        return sequence;
    }

    private bool WouldExceedLimits(BaseModuleDefinition added)
    {
        BasePowerNetworkSnapshot current = CalculatePowerSnapshot();
        BaseConstructionLimits limits = _catalog.Limits;
        return current.Modules + 1 > limits.MaximumModules ||
            current.InteractiveDevices + added.InteractiveDevices >
                limits.MaximumInteractiveDevices ||
            current.ActivePhysicsObjects + added.ActivePhysicsObjects >
                limits.MaximumActivePhysicsObjects ||
            current.DynamicLights + added.DynamicLights >
                limits.MaximumDynamicLights;
    }

    private bool HasAdjacentModule(int gridX, int gridZ)
    {
        return FindAt(gridX - 1, gridZ) is not null ||
            FindAt(gridX + 1, gridZ) is not null ||
            FindAt(gridX, gridZ - 1) is not null ||
            FindAt(gridX, gridZ + 1) is not null;
    }

    private bool HasAnchor()
    {
        return _placements.Values.Any(placement =>
            _catalog.GetModule(placement.ModuleId).IsAnchor);
    }

    private int CountConnectedComponents()
    {
        if (_placements.Count == 0)
        {
            return 0;
        }

        Dictionary<(int X, int Z), BaseModulePlacement> byCell =
            _placements.Values.ToDictionary(
                placement => (placement.GridX, placement.GridZ));
        HashSet<string> visited = new(StringComparer.Ordinal);
        int components = 0;
        foreach (BaseModulePlacement start in _placements.Values)
        {
            if (!visited.Add(start.InstanceId))
            {
                continue;
            }

            components++;
            Queue<BaseModulePlacement> queue = new();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                BaseModulePlacement current = queue.Dequeue();
                foreach ((int X, int Z) neighborCell in new[]
                {
                    (current.GridX - 1, current.GridZ),
                    (current.GridX + 1, current.GridZ),
                    (current.GridX, current.GridZ - 1),
                    (current.GridX, current.GridZ + 1)
                })
                {
                    if (byCell.TryGetValue(
                            neighborCell,
                            out BaseModulePlacement? neighbor) &&
                        visited.Add(neighbor.InstanceId))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return components;
    }

    private BasePowerNetworkSnapshot CalculatePowerSnapshot()
    {
        BaseModuleDefinition[] definitions = _placements.Values
            .Select(placement => _catalog.GetModule(placement.ModuleId))
            .ToArray();
        BaseModulePlacement[] enabled = _placements.Values
            .Where(placement => placement.Enabled)
            .ToArray();
        double generation = enabled.Sum(placement =>
            _catalog.GetModule(placement.ModuleId).PowerGeneration);
        double consumption = enabled.Sum(placement =>
            _catalog.GetModule(placement.ModuleId).PowerConsumption);
        double capacity = definitions.Sum(definition =>
            definition.BatteryCapacity);
        int enabledConsumers = enabled.Count(placement =>
            _catalog.GetModule(placement.ModuleId).PowerConsumption > 0.0);
        bool powered = generation >= consumption || _storedEnergy > 0.0001;
        return new BasePowerNetworkSnapshot(
            _placements.Count,
            definitions.Sum(definition => definition.InteractiveDevices),
            definitions.Sum(definition => definition.ActivePhysicsObjects),
            definitions.Sum(definition => definition.DynamicLights),
            CountConnectedComponents(),
            generation,
            consumption,
            Math.Min(_storedEnergy, capacity),
            capacity,
            enabledConsumers,
            powered ? enabledConsumers : 0,
            generation < consumption && _storedEnergy <= 0.0001);
    }

    private void RecomputePower(double deltaSeconds)
    {
        BasePowerNetworkSnapshot before = CalculatePowerSnapshot();
        double net = before.Generation - before.Consumption;
        if (deltaSeconds > 0.0)
        {
            _storedEnergy = Math.Clamp(
                _storedEnergy + net * deltaSeconds,
                0.0,
                before.BatteryCapacity);
        }
        else
        {
            _storedEnergy = Math.Clamp(
                _storedEnergy,
                0.0,
                before.BatteryCapacity);
        }

        Power = CalculatePowerSnapshot();
    }
}
