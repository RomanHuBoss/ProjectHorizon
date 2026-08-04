using System;
using System.Collections.Generic;
using System.Linq;

public enum ShipModuleInstallResult
{
    Installed = 0,
    AlreadyInstalled = 1,
    SlotUnavailable = 2,
    UnknownModule = 3,
    NotCommissioned = 4
}

public enum ShipModuleUninstallResult
{
    Uninstalled = 0,
    NotInstalled = 1,
    NotCommissioned = 2
}

public enum ShipSystemMutationResult
{
    Applied = 0,
    AlreadyFull = 1,
    AlreadyOffline = 2,
    UnknownSystem = 3,
    NotCommissioned = 4
}

public sealed record ShipEffectiveStats(
    double Hull,
    double Shield,
    int CargoCapacity,
    double FuelCapacity,
    double Acceleration,
    double MaxSpeed,
    double Maneuverability,
    int WeaponSlots,
    int TechnologySlots,
    double HyperdriveRange,
    double AtmosphericEfficiency);

public sealed record InstalledShipModuleState(
    ShipModuleDefinition Definition,
    int SlotIndex,
    bool Active);

public sealed class ShipSystemsRuntime
{
    private readonly ShipSystemsCatalog _catalog;
    private readonly ShipClassDefinition _shipClass;
    private readonly Dictionary<string, ShipModuleInstallationSaveData>
        _installedModules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _systemHealth = new(
        StringComparer.Ordinal);

    public ShipSystemsRuntime(
        ShipSystemsCatalog catalog,
        ShipSystemsSaveData? saveData = null,
        bool? commissioned = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        string classId = saveData?.ShipClassId ?? catalog.StarterClassId;
        _shipClass = catalog.GetClass(classId);
        foreach (string systemId in catalog.Systems.Keys)
        {
            _systemHealth[systemId] = 100.0;
        }

        if (saveData is not null)
        {
            RestoreModules(saveData.InstalledModules);
            RestoreSystems(saveData.Systems);
        }

        Commissioned = commissioned ?? saveData?.Commissioned ?? false;
        if (!Commissioned)
        {
            _installedModules.Clear();
            foreach (string systemId in _systemHealth.Keys.ToArray())
            {
                _systemHealth[systemId] = 0.0;
            }
        }

        Fuel = Math.Clamp(
            saveData?.Fuel ?? 35.0,
            0.0,
            GetEffectiveStats().FuelCapacity);
    }

    public string ShipClassId => _shipClass.ShipClassId;

    public bool Commissioned { get; private set; }

    public double Fuel { get; private set; }

    public int InstalledModuleCount => _installedModules.Count;

    public int InstalledWeaponModules => _installedModules.Values.Count(value =>
        string.Equals(value.SlotType, "Weapon", StringComparison.Ordinal));

    public int InstalledTechnologyModules => _installedModules.Values.Count(value =>
        string.Equals(value.SlotType, "Technology", StringComparison.Ordinal));

    public int DisabledSystemCount => _systemHealth.Count(pair => pair.Value <= 0.0);

    public bool FlightReady =>
        Commissioned &&
        GetSystemHealth("ship.system.hull") > 0.0 &&
        GetSystemHealth("ship.system.engine") > 0.0 &&
        GetSystemHealth("ship.system.impulse") > 0.0 &&
        GetSystemHealth("ship.system.landing") > 0.0;

    public bool HyperspaceReady =>
        Commissioned &&
        GetSystemHealth("ship.system.hyperdrive") > 0.0 &&
        InstalledModules.Any(module =>
            module.Definition.EnablesHyperspace && module.Active);

    public IReadOnlyList<InstalledShipModuleState> InstalledModules =>
        _installedModules.Values
            .OrderBy(value => value.SlotType, StringComparer.Ordinal)
            .ThenBy(value => value.SlotIndex)
            .Select(value =>
            {
                ShipModuleDefinition definition = _catalog.GetModule(
                    value.ModuleId);
                return new InstalledShipModuleState(
                    definition,
                    value.SlotIndex,
                    IsModuleActive(definition));
            })
            .ToArray();

    public IReadOnlyDictionary<string, double> SystemHealth => _systemHealth;

    public bool IsInstalled(string moduleId)
    {
        return _installedModules.ContainsKey(moduleId);
    }

    public int GetAvailableSlots(string slotType)
    {
        if (!Commissioned)
        {
            return 0;
        }

        int capacity = GetSlotCapacity(slotType);
        int used = _installedModules.Values.Count(value => string.Equals(
            value.SlotType,
            slotType,
            StringComparison.Ordinal));
        return Math.Max(0, capacity - used);
    }

    public ShipModuleInstallResult CanInstall(
        string moduleId,
        out string result)
    {
        if (!Commissioned)
        {
            result = "starter ship is not commissioned";
            return ShipModuleInstallResult.NotCommissioned;
        }

        if (!_catalog.Modules.TryGetValue(
            moduleId,
            out ShipModuleDefinition? definition))
        {
            result = $"unknown ship module {moduleId}";
            return ShipModuleInstallResult.UnknownModule;
        }

        if (_installedModules.ContainsKey(moduleId))
        {
            result = $"{moduleId} is already installed";
            return ShipModuleInstallResult.AlreadyInstalled;
        }

        if (GetAvailableSlots(definition.SlotType) <= 0)
        {
            result = $"no free {definition.SlotType} slot";
            return ShipModuleInstallResult.SlotUnavailable;
        }

        result = $"{moduleId} can be installed";
        return ShipModuleInstallResult.Installed;
    }

    public ShipModuleInstallResult TryInstall(
        string moduleId,
        out string result)
    {
        ShipModuleInstallResult check = CanInstall(moduleId, out result);
        if (check != ShipModuleInstallResult.Installed)
        {
            return check;
        }

        ShipModuleDefinition definition = _catalog.GetModule(moduleId);
        int slotIndex = Enumerable.Range(0, GetSlotCapacity(definition.SlotType))
            .First(index => !_installedModules.Values.Any(value =>
                string.Equals(
                    value.SlotType,
                    definition.SlotType,
                    StringComparison.Ordinal) &&
                value.SlotIndex == index));
        _installedModules.Add(
            moduleId,
            new ShipModuleInstallationSaveData(
                moduleId,
                definition.SlotType,
                slotIndex));
        Fuel = Math.Min(Fuel, GetEffectiveStats().FuelCapacity);
        result = $"installed {moduleId} in {definition.SlotType} slot {slotIndex + 1}";
        return ShipModuleInstallResult.Installed;
    }

    public ShipModuleUninstallResult TryUninstall(
        string moduleId,
        out string result)
    {
        if (!Commissioned)
        {
            result = "starter ship is not commissioned";
            return ShipModuleUninstallResult.NotCommissioned;
        }

        if (!_installedModules.Remove(moduleId))
        {
            result = $"{moduleId} is not installed";
            return ShipModuleUninstallResult.NotInstalled;
        }

        foreach (string systemId in _systemHealth.Keys.ToArray())
        {
            _systemHealth[systemId] = Math.Min(
                _systemHealth[systemId],
                GetSystemMaximumHealth(systemId));
        }

        Fuel = Math.Min(Fuel, GetEffectiveStats().FuelCapacity);
        result = $"uninstalled {moduleId}";
        return ShipModuleUninstallResult.Uninstalled;
    }

    public bool TryConsumeFuel(double amount, out string result)
    {
        if (!Commissioned)
        {
            result = "starter ship is not commissioned";
            return false;
        }

        if (amount <= 0.0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (Fuel + 0.0001 < amount)
        {
            result = $"insufficient fuel {Fuel:0.#}/{amount:0.#}";
            return false;
        }

        Fuel -= amount;
        result = $"fuel consumed {amount:0.#}; remaining={Fuel:0.#}";
        return true;
    }

    public double Refuel(double amount)
    {
        if (!Commissioned)
        {
            return 0.0;
        }

        if (amount <= 0.0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        double previous = Fuel;
        Fuel = Math.Min(GetEffectiveStats().FuelCapacity, Fuel + amount);
        return Fuel - previous;
    }

    public ShipSystemMutationResult ApplyDamage(
        string systemId,
        double amount,
        out string result)
    {
        if (!Commissioned)
        {
            result = "starter ship is not commissioned";
            return ShipSystemMutationResult.NotCommissioned;
        }

        if (!_systemHealth.TryGetValue(systemId, out double current))
        {
            result = $"unknown ship system {systemId}";
            return ShipSystemMutationResult.UnknownSystem;
        }

        if (amount <= 0.0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (current <= 0.0)
        {
            result = $"{systemId} is already offline";
            return ShipSystemMutationResult.AlreadyOffline;
        }

        double next = Math.Max(0.0, current - amount);
        _systemHealth[systemId] = next;
        result = $"{systemId} damaged {current:0.#}->{next:0.#}";
        return ShipSystemMutationResult.Applied;
    }

    public ShipSystemMutationResult Repair(
        string systemId,
        double amount,
        out string result)
    {
        if (!Commissioned)
        {
            result = "starter ship is not commissioned";
            return ShipSystemMutationResult.NotCommissioned;
        }

        if (!_systemHealth.TryGetValue(systemId, out double current))
        {
            result = $"unknown ship system {systemId}";
            return ShipSystemMutationResult.UnknownSystem;
        }

        if (amount <= 0.0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        double maximum = GetSystemMaximumHealth(systemId);
        if (current >= maximum)
        {
            result = $"{systemId} is already at maximum health";
            return ShipSystemMutationResult.AlreadyFull;
        }

        double next = Math.Min(maximum, current + amount);
        _systemHealth[systemId] = next;
        result = $"{systemId} repaired {current:0.#}->{next:0.#}/{maximum:0.#}";
        return ShipSystemMutationResult.Applied;
    }

    public bool Commission(out string result)
    {
        if (Commissioned)
        {
            result = "starter ship is already commissioned";
            return false;
        }

        Commissioned = true;
        foreach (string systemId in _systemHealth.Keys.ToArray())
        {
            _systemHealth[systemId] = GetSystemMaximumHealth(systemId);
        }

        Fuel = Math.Min(Fuel, GetEffectiveStats().FuelCapacity);
        result = "starter ship commissioned; all core systems online";
        return true;
    }

    public double GetSystemHealth(string systemId)
    {
        return _systemHealth.TryGetValue(systemId, out double health)
            ? health
            : throw new KeyNotFoundException($"Unknown ship system {systemId}.");
    }

    public double GetSystemMaximumHealth(string systemId)
    {
        _ = _catalog.GetSystem(systemId);
        return 100.0 + _installedModules.Keys
            .Select(_catalog.GetModule)
            .Where(module => module.AffectedSystems.Contains(
                systemId,
                StringComparer.Ordinal))
            .Sum(module => module.DurabilityBonus);
    }

    public ShipEffectiveStats GetEffectiveStats()
    {
        ShipBaseStatsDefinition baseStats = _shipClass.BaseStats;
        ShipModuleDefinition[] active = InstalledModules
            .Where(module => module.Active)
            .Select(module => module.Definition)
            .ToArray();
        return new ShipEffectiveStats(
            baseStats.Hull + active.Sum(module => module.Effects.Hull),
            baseStats.Shield + active.Sum(module => module.Effects.Shield),
            checked(baseStats.CargoCapacity + active.Sum(module =>
                module.Effects.CargoCapacity)),
            baseStats.FuelCapacity + active.Sum(module =>
                module.Effects.FuelCapacity),
            baseStats.Acceleration + active.Sum(module =>
                module.Effects.Acceleration),
            baseStats.MaxSpeed + active.Sum(module =>
                module.Effects.MaxSpeed),
            baseStats.Maneuverability + active.Sum(module =>
                module.Effects.Maneuverability),
            baseStats.WeaponSlots,
            baseStats.TechnologySlots,
            baseStats.HyperdriveRange + active.Sum(module =>
                module.Effects.HyperdriveRange),
            Math.Clamp(
                baseStats.AtmosphericEfficiency + active.Sum(module =>
                    module.Effects.AtmosphericEfficiency),
                0.0,
                100.0));
    }

    public ShipSystemsSaveData CreateSaveData()
    {
        return new ShipSystemsSaveData(
            ShipClassId,
            Fuel,
            _installedModules.Values
                .OrderBy(value => value.SlotType, StringComparer.Ordinal)
                .ThenBy(value => value.SlotIndex)
                .ToArray(),
            _systemHealth
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ShipSystemHealthSaveData(
                    pair.Key,
                    pair.Value))
                .ToArray(),
            Commissioned);
    }

    private int GetSlotCapacity(string slotType)
    {
        return slotType switch
        {
            "Weapon" => _shipClass.BaseStats.WeaponSlots,
            "Technology" => _shipClass.BaseStats.TechnologySlots,
            _ => throw new InvalidOperationException(
                $"Unsupported ship module slot type {slotType}.")
        };
    }

    private bool IsModuleActive(ShipModuleDefinition definition)
    {
        return Commissioned && definition.AffectedSystems.All(systemId =>
            GetSystemHealth(systemId) > 0.0);
    }

    private void RestoreModules(
        IReadOnlyList<ShipModuleInstallationSaveData> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (ShipModuleInstallationSaveData saved in modules
            .OrderBy(value => value.SlotType, StringComparer.Ordinal)
            .ThenBy(value => value.SlotIndex))
        {
            ShipModuleDefinition definition = _catalog.GetModule(saved.ModuleId);
            if (!string.Equals(
                    definition.SlotType,
                    saved.SlotType,
                    StringComparison.Ordinal) ||
                saved.SlotIndex < 0 ||
                saved.SlotIndex >= GetSlotCapacity(saved.SlotType) ||
                _installedModules.ContainsKey(saved.ModuleId) ||
                _installedModules.Values.Any(value =>
                    string.Equals(
                        value.SlotType,
                        saved.SlotType,
                        StringComparison.Ordinal) &&
                    value.SlotIndex == saved.SlotIndex))
            {
                throw new InvalidOperationException(
                    $"Invalid saved ship module installation {saved.ModuleId}.");
            }

            _installedModules.Add(saved.ModuleId, saved);
        }
    }

    private void RestoreSystems(
        IReadOnlyList<ShipSystemHealthSaveData> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (ShipSystemHealthSaveData saved in systems)
        {
            if (!_systemHealth.ContainsKey(saved.SystemId) ||
                !seen.Add(saved.SystemId) ||
                saved.Health < 0.0 ||
                double.IsNaN(saved.Health) ||
                double.IsInfinity(saved.Health))
            {
                throw new InvalidOperationException(
                    $"Invalid saved ship system state {saved.SystemId}.");
            }

            _systemHealth[saved.SystemId] = Math.Min(
                saved.Health,
                GetSystemMaximumHealth(saved.SystemId));
        }
    }
}
