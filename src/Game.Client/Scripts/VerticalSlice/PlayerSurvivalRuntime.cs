using System;
using System.Collections.Generic;
using System.Linq;

public enum PlayerMultitoolFunction
{
    Scanner = 0,
    Mining = 1,
    Weapon = 2,
    Analyzer = 3,
    Repair = 4
}

public enum PlayerEquipmentMutationResult
{
    Applied = 0,
    AlreadyInstalled = 1,
    NotInstalled = 2,
    SlotLimit = 3,
    UnknownDefinition = 4,
    NoEffect = 5,
    InsufficientEnergy = 6
}

public interface IPlayerMovementResourceProvider
{
    bool TryConsumeStamina(double amount);
    bool TryConsumeJetpackEnergy(double amount);
    void RecoverMovementResources(double deltaSeconds, bool sprinting, bool jetpacking);
    void SetSwimming(bool swimming);
}

public sealed record PlayerSurvivalEffectiveStats(
    double MaximumHealth,
    double MaximumShield,
    double MaximumStamina,
    double MaximumLifeSupport,
    double MaximumHazardProtection,
    double TemperatureProtection,
    double RadiationProtection,
    double ToxicProtection,
    double MaximumOxygen,
    double MaximumJetpackEnergy,
    double MaximumMultitoolEnergy);

public sealed record PlayerEnvironmentTickReport(
    string Archetype,
    double TemperatureExposure,
    double RadiationExposure,
    double ToxicExposure,
    double HazardDrain,
    double LifeSupportDrain,
    double OxygenDrain,
    double HealthDamage,
    bool Swimming);

public sealed class PlayerSurvivalRuntime : IPlayerMovementResourceProvider
{
    private const double ShieldRegenerationDelaySeconds = 5.0;
    private const double ShieldRegenerationPerSecond = 8.0;
    private const double StaminaRegenerationPerSecond = 18.0;
    private const double JetpackRegenerationPerSecond = 12.0;
    private const double MultitoolRegenerationPerSecond = 8.0;
    private const double SprintStaminaCostPerSecond = 17.0;
    private const double JetpackEnergyCostPerSecond = 24.0;

    private readonly PlayerSurvivalCatalog _catalog;
    private readonly HashSet<string> _installedSuitModules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _installedMultitoolModules = new(StringComparer.Ordinal);
    private double _secondsSinceDamage = 1000.0;

    public PlayerSurvivalRuntime(
        PlayerSurvivalCatalog catalog,
        PlayerSurvivalSaveData? saveData = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;

        PlayerSurvivalEffectiveStats baseStats = GetEffectiveStats();
        Health = baseStats.MaximumHealth;
        Shield = baseStats.MaximumShield;
        Stamina = baseStats.MaximumStamina;
        LifeSupport = baseStats.MaximumLifeSupport;
        HazardProtection = baseStats.MaximumHazardProtection;
        Oxygen = baseStats.MaximumOxygen;
        JetpackEnergy = baseStats.MaximumJetpackEnergy;
        MultitoolEnergy = baseStats.MaximumMultitoolEnergy;
        ActiveMultitoolFunction = PlayerMultitoolFunction.Scanner;

        if (saveData is null)
        {
            return;
        }

        Restore(saveData);
    }

    public double Health { get; private set; }
    public double Shield { get; private set; }
    public double Stamina { get; private set; }
    public double LifeSupport { get; private set; }
    public double HazardProtection { get; private set; }
    public double Oxygen { get; private set; }
    public double JetpackEnergy { get; private set; }
    public double MultitoolEnergy { get; private set; }
    public bool Swimming { get; private set; }
    public PlayerMultitoolFunction ActiveMultitoolFunction { get; private set; }
    public bool IsAlive => Health > 0.0001;

    public IReadOnlyCollection<string> InstalledSuitModules =>
        _installedSuitModules.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public IReadOnlyCollection<string> InstalledMultitoolModules =>
        _installedMultitoolModules.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public PlayerSurvivalEffectiveStats GetEffectiveStats()
    {
        PlayerSurvivalBaseStatsDefinition baseStats = _catalog.BaseStats;
        double temperature = baseStats.TemperatureProtection;
        double radiation = baseStats.RadiationProtection;
        double toxic = baseStats.ToxicProtection;
        double hazardCapacity = baseStats.HazardProtection;
        double lifeSupportCapacity = baseStats.LifeSupport;
        double oxygenCapacity = baseStats.Oxygen;
        foreach (string moduleId in _installedSuitModules)
        {
            PlayerSuitModuleDefinition module = _catalog.GetSuitModule(moduleId);
            temperature += module.TemperatureProtectionBonus;
            radiation += module.RadiationProtectionBonus;
            toxic += module.ToxicProtectionBonus;
            hazardCapacity += module.HazardCapacityBonus;
            lifeSupportCapacity += module.LifeSupportCapacityBonus;
            oxygenCapacity += module.OxygenCapacityBonus;
        }

        return new PlayerSurvivalEffectiveStats(
            baseStats.Health,
            baseStats.Shield,
            baseStats.Stamina,
            lifeSupportCapacity,
            hazardCapacity,
            Math.Clamp(temperature, 0.0, 100.0),
            Math.Clamp(radiation, 0.0, 100.0),
            Math.Clamp(toxic, 0.0, 100.0),
            oxygenCapacity,
            baseStats.JetpackEnergy,
            baseStats.MultitoolEnergy);
    }

    public PlayerEquipmentMutationResult InstallSuitModule(
        string moduleId,
        out string result)
    {
        if (!_catalog.SuitModules.ContainsKey(moduleId))
        {
            result = GameLocalizationService.Format("ui.survival.unknown_suit_module", ("module", moduleId));
            return PlayerEquipmentMutationResult.UnknownDefinition;
        }
        if (_installedSuitModules.Contains(moduleId))
        {
            result = GameLocalizationService.Format("ui.survival.already_installed", ("module", moduleId));
            return PlayerEquipmentMutationResult.AlreadyInstalled;
        }
        if (_installedSuitModules.Count >= _catalog.SuitSlotLimit)
        {
            result = GameLocalizationService.Format("ui.survival.suit_slot_limit", ("limit", _catalog.SuitSlotLimit));
            return PlayerEquipmentMutationResult.SlotLimit;
        }

        PlayerSurvivalEffectiveStats before = GetEffectiveStats();
        _installedSuitModules.Add(moduleId);
        PlayerSurvivalEffectiveStats after = GetEffectiveStats();
        HazardProtection += Math.Max(0.0, after.MaximumHazardProtection - before.MaximumHazardProtection);
        LifeSupport += Math.Max(0.0, after.MaximumLifeSupport - before.MaximumLifeSupport);
        Oxygen += Math.Max(0.0, after.MaximumOxygen - before.MaximumOxygen);
        ClampVitals();
        result = GameLocalizationService.Format("ui.survival.installed", ("module", moduleId));
        return PlayerEquipmentMutationResult.Applied;
    }

    public PlayerEquipmentMutationResult UninstallSuitModule(
        string moduleId,
        out string result)
    {
        if (!_installedSuitModules.Remove(moduleId))
        {
            result = GameLocalizationService.Format("ui.survival.not_installed", ("module", moduleId));
            return PlayerEquipmentMutationResult.NotInstalled;
        }
        ClampVitals();
        result = GameLocalizationService.Format("ui.survival.uninstalled", ("module", moduleId));
        return PlayerEquipmentMutationResult.Applied;
    }

    public PlayerEquipmentMutationResult InstallMultitoolModule(
        string moduleId,
        out string result)
    {
        if (!_catalog.MultitoolModules.ContainsKey(moduleId))
        {
            result = GameLocalizationService.Format("ui.survival.unknown_multitool_module", ("module", moduleId));
            return PlayerEquipmentMutationResult.UnknownDefinition;
        }
        if (_installedMultitoolModules.Contains(moduleId))
        {
            result = GameLocalizationService.Format("ui.survival.already_installed", ("module", moduleId));
            return PlayerEquipmentMutationResult.AlreadyInstalled;
        }
        if (_installedMultitoolModules.Count >= _catalog.MultitoolSlotLimit)
        {
            result = GameLocalizationService.Format("ui.survival.multitool_slot_limit", ("limit", _catalog.MultitoolSlotLimit));
            return PlayerEquipmentMutationResult.SlotLimit;
        }
        _installedMultitoolModules.Add(moduleId);
        result = GameLocalizationService.Format("ui.survival.installed", ("module", moduleId));
        return PlayerEquipmentMutationResult.Applied;
    }

    public PlayerEquipmentMutationResult UninstallMultitoolModule(
        string moduleId,
        out string result)
    {
        if (!_installedMultitoolModules.Remove(moduleId))
        {
            result = GameLocalizationService.Format("ui.survival.not_installed", ("module", moduleId));
            return PlayerEquipmentMutationResult.NotInstalled;
        }
        result = GameLocalizationService.Format("ui.survival.uninstalled", ("module", moduleId));
        return PlayerEquipmentMutationResult.Applied;
    }

    public PlayerEquipmentMutationResult UseConsumable(
        string definitionId,
        out string result)
    {
        if (!_catalog.Consumables.TryGetValue(
            definitionId,
            out PlayerConsumableDefinition? consumable))
        {
            result = GameLocalizationService.Format("ui.survival.unknown_consumable", ("item", definitionId));
            return PlayerEquipmentMutationResult.UnknownDefinition;
        }

        PlayerSurvivalEffectiveStats stats = GetEffectiveStats();
        double before = Health + Shield + LifeSupport + HazardProtection + Oxygen +
            JetpackEnergy + MultitoolEnergy;
        Health = Math.Min(stats.MaximumHealth, Health + consumable.HealthRestore);
        Shield = Math.Min(stats.MaximumShield, Shield + consumable.ShieldRestore);
        LifeSupport = Math.Min(
            stats.MaximumLifeSupport,
            LifeSupport + consumable.LifeSupportRestore);
        HazardProtection = Math.Min(
            stats.MaximumHazardProtection,
            HazardProtection + consumable.HazardRestore);
        Oxygen = Math.Min(stats.MaximumOxygen, Oxygen + consumable.OxygenRestore);
        JetpackEnergy = Math.Min(
            stats.MaximumJetpackEnergy,
            JetpackEnergy + consumable.JetpackRestore);
        MultitoolEnergy = Math.Min(
            stats.MaximumMultitoolEnergy,
            MultitoolEnergy + consumable.MultitoolEnergyRestore);
        double after = Health + Shield + LifeSupport + HazardProtection + Oxygen +
            JetpackEnergy + MultitoolEnergy;
        if (after <= before + 0.0001)
        {
            result = GameLocalizationService.Format("ui.survival.no_effect", ("item", definitionId));
            return PlayerEquipmentMutationResult.NoEffect;
        }

        result = GameLocalizationService.Format("ui.survival.used", ("item", definitionId));
        return PlayerEquipmentMutationResult.Applied;
    }

    public bool TryUseMultitool(
        PlayerMultitoolFunction function,
        out double energySpent)
    {
        ActiveMultitoolFunction = function;
        double cost = BaseMultitoolCost(function) * GetMultitoolEnergyMultiplier(function);
        if (MultitoolEnergy + 0.0001 < cost)
        {
            energySpent = 0.0;
            return false;
        }
        MultitoolEnergy = Math.Max(0.0, MultitoolEnergy - cost);
        energySpent = cost;
        return true;
    }

    public double GetMultitoolEffectiveness(PlayerMultitoolFunction function)
    {
        double bonus = _installedMultitoolModules
            .Select(moduleId => _catalog.GetMultitoolModule(moduleId))
            .Where(module => string.Equals(
                module.Function,
                function.ToString(),
                StringComparison.Ordinal))
            .Sum(module => module.EffectivenessBonus);
        return 1.0 + bonus;
    }

    public void SetActiveMultitoolFunction(PlayerMultitoolFunction function)
    {
        ActiveMultitoolFunction = function;
    }

    public void CycleMultitoolFunction()
    {
        ActiveMultitoolFunction = (PlayerMultitoolFunction)(
            ((int)ActiveMultitoolFunction + 1) %
            Enum.GetValues<PlayerMultitoolFunction>().Length);
    }

    public void ApplyDamage(double damage)
    {
        if (damage <= 0.0 || !double.IsFinite(damage) || !IsAlive)
        {
            return;
        }
        double shieldDamage = Math.Min(Shield, damage);
        Shield -= shieldDamage;
        Health = Math.Max(0.0, Health - (damage - shieldDamage));
        _secondsSinceDamage = 0.0;
    }

    public PlayerEnvironmentTickReport Tick(
        PlayerEnvironmentDefinition environment,
        double deltaSeconds,
        bool activeOnFoot,
        bool safeInterior = false)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (deltaSeconds <= 0.0 || !double.IsFinite(deltaSeconds))
        {
            return new PlayerEnvironmentTickReport(
                environment.Archetype, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, Swimming);
        }

        _secondsSinceDamage += deltaSeconds;
        PlayerSurvivalEffectiveStats stats = GetEffectiveStats();
        double temperatureExposure = 0.0;
        double radiationExposure = 0.0;
        double toxicExposure = 0.0;
        double hazardDrain = 0.0;
        double lifeSupportDrain = 0.0;
        double oxygenDrain = 0.0;
        double healthBefore = Health;

        if (activeOnFoot && !safeInterior && IsAlive)
        {
            temperatureExposure = Math.Max(
                0.0,
                environment.TemperatureHazard - stats.TemperatureProtection / 100.0);
            radiationExposure = Math.Max(
                0.0,
                environment.RadiationHazard - stats.RadiationProtection / 100.0);
            toxicExposure = Math.Max(
                0.0,
                environment.ToxicHazard - stats.ToxicProtection / 100.0);
            double combinedExposure =
                temperatureExposure + radiationExposure + toxicExposure;
            hazardDrain = combinedExposure * 4.0 * deltaSeconds;
            double hazardBefore = HazardProtection;
            HazardProtection = Math.Max(0.0, HazardProtection - hazardDrain);
            double unabsorbedHazard = Math.Max(0.0, hazardDrain - hazardBefore);
            if (unabsorbedHazard > 0.0)
            {
                Health = Math.Max(0.0, Health - unabsorbedHazard * 1.8);
                _secondsSinceDamage = 0.0;
            }

            lifeSupportDrain = environment.LifeSupportDrainPerSecond * deltaSeconds;
            LifeSupport = Math.Max(0.0, LifeSupport - lifeSupportDrain);
            if (LifeSupport <= 0.0001)
            {
                Health = Math.Max(0.0, Health - 4.0 * deltaSeconds);
                _secondsSinceDamage = 0.0;
            }

            double oxygenRate = environment.OxygenDrainPerSecond;
            if (Swimming)
            {
                oxygenRate = Math.Max(oxygenRate, 1.6);
            }
            else if (!environment.Breathable)
            {
                oxygenRate = Math.Max(oxygenRate, 0.8);
            }
            oxygenDrain = oxygenRate * deltaSeconds;
            Oxygen = Math.Max(0.0, Oxygen - oxygenDrain);
            if (Oxygen <= 0.0001)
            {
                Health = Math.Max(0.0, Health - 7.5 * deltaSeconds);
                _secondsSinceDamage = 0.0;
            }
        }
        else
        {
            HazardProtection = Math.Min(
                stats.MaximumHazardProtection,
                HazardProtection + 5.0 * deltaSeconds);
            LifeSupport = Math.Min(
                stats.MaximumLifeSupport,
                LifeSupport + 4.0 * deltaSeconds);
            Oxygen = Math.Min(
                stats.MaximumOxygen,
                Oxygen + 12.0 * deltaSeconds);
        }

        if (activeOnFoot && !Swimming && environment.Breathable)
        {
            Oxygen = Math.Min(
                stats.MaximumOxygen,
                Oxygen + 5.0 * deltaSeconds);
        }
        if (activeOnFoot &&
            temperatureExposure + radiationExposure + toxicExposure <= 0.001)
        {
            HazardProtection = Math.Min(
                stats.MaximumHazardProtection,
                HazardProtection + 2.0 * deltaSeconds);
        }

        if (_secondsSinceDamage >= ShieldRegenerationDelaySeconds)
        {
            Shield = Math.Min(
                stats.MaximumShield,
                Shield + ShieldRegenerationPerSecond * deltaSeconds);
        }
        MultitoolEnergy = Math.Min(
            stats.MaximumMultitoolEnergy,
            MultitoolEnergy + MultitoolRegenerationPerSecond * deltaSeconds);
        ClampVitals();

        return new PlayerEnvironmentTickReport(
            environment.Archetype,
            temperatureExposure,
            radiationExposure,
            toxicExposure,
            hazardDrain,
            lifeSupportDrain,
            oxygenDrain,
            Math.Max(0.0, healthBefore - Health),
            Swimming);
    }

    public bool TryConsumeStamina(double amount)
    {
        if (amount <= 0.0)
        {
            return true;
        }
        double cost = amount * SprintStaminaCostPerSecond;
        if (Stamina + 0.0001 < cost)
        {
            return false;
        }
        Stamina = Math.Max(0.0, Stamina - cost);
        return true;
    }

    public bool TryConsumeJetpackEnergy(double amount)
    {
        if (amount <= 0.0)
        {
            return true;
        }
        double cost = amount * JetpackEnergyCostPerSecond;
        if (JetpackEnergy + 0.0001 < cost)
        {
            return false;
        }
        JetpackEnergy = Math.Max(0.0, JetpackEnergy - cost);
        return true;
    }

    public void RecoverMovementResources(
        double deltaSeconds,
        bool sprinting,
        bool jetpacking)
    {
        if (deltaSeconds <= 0.0)
        {
            return;
        }
        PlayerSurvivalEffectiveStats stats = GetEffectiveStats();
        if (!sprinting)
        {
            Stamina = Math.Min(
                stats.MaximumStamina,
                Stamina + StaminaRegenerationPerSecond * deltaSeconds);
        }
        if (!jetpacking)
        {
            JetpackEnergy = Math.Min(
                stats.MaximumJetpackEnergy,
                JetpackEnergy + JetpackRegenerationPerSecond * deltaSeconds);
        }
    }

    public void SetSwimming(bool swimming)
    {
        Swimming = swimming;
    }

    public PlayerSurvivalSaveData CreateSaveData()
    {
        return new PlayerSurvivalSaveData(
            Health,
            Shield,
            Stamina,
            LifeSupport,
            HazardProtection,
            Oxygen,
            JetpackEnergy,
            MultitoolEnergy,
            ActiveMultitoolFunction.ToString(),
            InstalledSuitModules.ToArray(),
            InstalledMultitoolModules.ToArray());
    }

    private void Restore(PlayerSurvivalSaveData saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        foreach (string moduleId in saveData.InstalledSuitModuleIds ?? Array.Empty<string>())
        {
            if (!_catalog.SuitModules.ContainsKey(moduleId) ||
                !_installedSuitModules.Add(moduleId))
            {
                throw new InvalidOperationException(
                    $"Invalid or duplicate saved suit module {moduleId}.");
            }
        }
        foreach (string moduleId in saveData.InstalledMultitoolModuleIds ?? Array.Empty<string>())
        {
            if (!_catalog.MultitoolModules.ContainsKey(moduleId) ||
                !_installedMultitoolModules.Add(moduleId))
            {
                throw new InvalidOperationException(
                    $"Invalid or duplicate saved multitool module {moduleId}.");
            }
        }
        if (_installedSuitModules.Count > _catalog.SuitSlotLimit ||
            _installedMultitoolModules.Count > _catalog.MultitoolSlotLimit ||
            !Enum.TryParse(
                saveData.ActiveMultitoolFunction,
                ignoreCase: false,
                out PlayerMultitoolFunction activeFunction))
        {
            throw new InvalidOperationException(
                "Saved player survival equipment exceeds slot limits or has an invalid multitool mode.");
        }

        Health = saveData.Health;
        Shield = saveData.Shield;
        Stamina = saveData.Stamina;
        LifeSupport = saveData.LifeSupport;
        HazardProtection = saveData.HazardProtection;
        Oxygen = saveData.Oxygen;
        JetpackEnergy = saveData.JetpackEnergy;
        MultitoolEnergy = saveData.MultitoolEnergy;
        ActiveMultitoolFunction = activeFunction;
        ClampVitals();
    }

    private double GetMultitoolEnergyMultiplier(PlayerMultitoolFunction function)
    {
        double multiplier = 1.0;
        foreach (string moduleId in _installedMultitoolModules)
        {
            PlayerMultitoolModuleDefinition module = _catalog.GetMultitoolModule(moduleId);
            if (string.Equals(module.Function, function.ToString(), StringComparison.Ordinal))
            {
                multiplier *= module.EnergyCostMultiplier;
            }
        }
        return Math.Clamp(multiplier, 0.25, 1.0);
    }

    private static double BaseMultitoolCost(PlayerMultitoolFunction function)
    {
        return function switch
        {
            PlayerMultitoolFunction.Scanner => 4.0,
            PlayerMultitoolFunction.Mining => 6.0,
            PlayerMultitoolFunction.Weapon => 5.0,
            PlayerMultitoolFunction.Analyzer => 3.5,
            PlayerMultitoolFunction.Repair => 8.0,
            _ => 5.0
        };
    }

    private void ClampVitals()
    {
        PlayerSurvivalEffectiveStats stats = GetEffectiveStats();
        Health = ClampFinite(Health, 0.0, stats.MaximumHealth);
        Shield = ClampFinite(Shield, 0.0, stats.MaximumShield);
        Stamina = ClampFinite(Stamina, 0.0, stats.MaximumStamina);
        LifeSupport = ClampFinite(LifeSupport, 0.0, stats.MaximumLifeSupport);
        HazardProtection = ClampFinite(
            HazardProtection,
            0.0,
            stats.MaximumHazardProtection);
        Oxygen = ClampFinite(Oxygen, 0.0, stats.MaximumOxygen);
        JetpackEnergy = ClampFinite(
            JetpackEnergy,
            0.0,
            stats.MaximumJetpackEnergy);
        MultitoolEnergy = ClampFinite(
            MultitoolEnergy,
            0.0,
            stats.MaximumMultitoolEnergy);
    }

    private static double ClampFinite(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                "Player survival state contains a non-finite value.");
        }
        return Math.Clamp(value, minimum, maximum);
    }
}
