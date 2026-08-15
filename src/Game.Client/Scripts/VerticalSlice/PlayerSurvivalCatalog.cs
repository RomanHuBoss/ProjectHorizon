using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record PlayerSurvivalBaseStatsDefinition(
    double Health,
    double Shield,
    double Stamina,
    double LifeSupport,
    double HazardProtection,
    double TemperatureProtection,
    double RadiationProtection,
    double ToxicProtection,
    double Oxygen,
    double JetpackEnergy,
    double MultitoolEnergy);

public sealed record PlayerSuitModuleDefinition(
    string ModuleId,
    double TemperatureProtectionBonus,
    double RadiationProtectionBonus,
    double ToxicProtectionBonus,
    double HazardCapacityBonus,
    double LifeSupportCapacityBonus,
    double OxygenCapacityBonus);

public sealed record PlayerMultitoolModuleDefinition(
    string ModuleId,
    string Function,
    double EnergyCostMultiplier,
    double EffectivenessBonus);

public sealed record PlayerConsumableDefinition(
    string DefinitionId,
    double HealthRestore,
    double ShieldRestore,
    double LifeSupportRestore,
    double HazardRestore,
    double OxygenRestore,
    double JetpackRestore,
    double MultitoolEnergyRestore);

public sealed record PlayerEnvironmentDefinition(
    string Archetype,
    double TemperatureHazard,
    double RadiationHazard,
    double ToxicHazard,
    double LifeSupportDrainPerSecond,
    double OxygenDrainPerSecond,
    bool Breathable);

public sealed class PlayerSurvivalCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedSuitModuleCount = 3;
    public const int ExpectedMultitoolModuleCount = 3;
    public const int ExpectedConsumableCount = 6;
    public const int ExpectedEnvironmentCount = 8;

    private static readonly string[] RequiredSuitModuleIds =
    {
        "module.suit.thermal_liner",
        "module.suit.radiation_mesh",
        "module.suit.toxic_filter"
    };

    private static readonly string[] RequiredMultitoolModuleIds =
    {
        "tool.mining_drill_bit",
        "tool.scanner_upgrade",
        "tool.survey_beacon"
    };

    private static readonly string[] RequiredConsumableIds =
    {
        "consumable.oxygen_canister",
        "consumable.chemical_oxygen_generator",
        "consumable.med_gel",
        "consumable.repair_foam",
        "consumable.emergency_heater",
        "consumable.multitool_battery"
    };

    private static readonly string[] RequiredEnvironmentArchetypes =
    {
        "temperate",
        "desert",
        "frozen",
        "volcanic",
        "toxic",
        "radioactive",
        "barren",
        "oceanic"
    };

    private static readonly HashSet<string> SupportedMultitoolFunctions = new(
        new[] { "Mining", "Scanner", "Analyzer" },
        StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly Dictionary<string, PlayerSuitModuleDefinition> _suitModules;
    private readonly Dictionary<string, PlayerMultitoolModuleDefinition> _multitoolModules;
    private readonly Dictionary<string, PlayerConsumableDefinition> _consumables;
    private readonly Dictionary<string, PlayerEnvironmentDefinition> _environments;

    private PlayerSurvivalCatalog(
        int schemaVersion,
        int suitSlotLimit,
        int multitoolSlotLimit,
        PlayerSurvivalBaseStatsDefinition baseStats,
        Dictionary<string, PlayerSuitModuleDefinition> suitModules,
        Dictionary<string, PlayerMultitoolModuleDefinition> multitoolModules,
        Dictionary<string, PlayerConsumableDefinition> consumables,
        Dictionary<string, PlayerEnvironmentDefinition> environments)
    {
        SchemaVersion = schemaVersion;
        SuitSlotLimit = suitSlotLimit;
        MultitoolSlotLimit = multitoolSlotLimit;
        BaseStats = baseStats;
        _suitModules = suitModules;
        _multitoolModules = multitoolModules;
        _consumables = consumables;
        _environments = environments;
    }

    public int SchemaVersion { get; }
    public int SuitSlotLimit { get; }
    public int MultitoolSlotLimit { get; }
    public PlayerSurvivalBaseStatsDefinition BaseStats { get; }
    public IReadOnlyDictionary<string, PlayerSuitModuleDefinition> SuitModules => _suitModules;
    public IReadOnlyDictionary<string, PlayerMultitoolModuleDefinition> MultitoolModules => _multitoolModules;
    public IReadOnlyDictionary<string, PlayerConsumableDefinition> Consumables => _consumables;
    public IReadOnlyDictionary<string, PlayerEnvironmentDefinition> Environments => _environments;

    public PlayerSuitModuleDefinition GetSuitModule(string moduleId) =>
        _suitModules.TryGetValue(moduleId, out PlayerSuitModuleDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown suit module {moduleId}.");

    public PlayerMultitoolModuleDefinition GetMultitoolModule(string moduleId) =>
        _multitoolModules.TryGetValue(moduleId, out PlayerMultitoolModuleDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown multitool module {moduleId}.");

    public PlayerConsumableDefinition GetConsumable(string definitionId) =>
        _consumables.TryGetValue(definitionId, out PlayerConsumableDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown survival consumable {definitionId}.");

    public PlayerEnvironmentDefinition GetEnvironment(string archetype) =>
        _environments.TryGetValue(archetype, out PlayerEnvironmentDefinition? value)
            ? value
            : _environments["temperate"];

    public static PlayerSurvivalCatalog LoadFromJson(
        string json,
        GameContentCatalog contentCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(contentCatalog);

        PlayerSurvivalDocument document;
        try
        {
            document = JsonSerializer.Deserialize<PlayerSurvivalDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "player_survival.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"player_survival.json is invalid: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ContentValidationException(
                $"player_survival.json schema {document.SchemaVersion} is not supported; " +
                $"expected {CurrentSchemaVersion}.");
        }

        ValidateBaseStats(document.BaseStats);
        if (document.SuitSlotLimit != ExpectedSuitModuleCount ||
            document.MultitoolSlotLimit != ExpectedMultitoolModuleCount)
        {
            throw new ContentValidationException(
                "Player equipment slot limits must be exactly 3 suit and 3 multitool slots.");
        }

        Dictionary<string, PlayerSuitModuleDefinition> suitModules = new(StringComparer.Ordinal);
        foreach (PlayerSuitModuleDefinition definition in
            document.SuitModules ?? Array.Empty<PlayerSuitModuleDefinition>())
        {
            ValidateSuitModule(definition, contentCatalog);
            if (!suitModules.TryAdd(definition.ModuleId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate suit module {definition.ModuleId}.");
            }
        }

        Dictionary<string, PlayerMultitoolModuleDefinition> multitoolModules = new(StringComparer.Ordinal);
        foreach (PlayerMultitoolModuleDefinition definition in
            document.MultitoolModules ?? Array.Empty<PlayerMultitoolModuleDefinition>())
        {
            ValidateMultitoolModule(definition, contentCatalog);
            if (!multitoolModules.TryAdd(definition.ModuleId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate multitool module {definition.ModuleId}.");
            }
        }

        Dictionary<string, PlayerConsumableDefinition> consumables = new(StringComparer.Ordinal);
        foreach (PlayerConsumableDefinition definition in
            document.Consumables ?? Array.Empty<PlayerConsumableDefinition>())
        {
            ValidateConsumable(definition, contentCatalog);
            if (!consumables.TryAdd(definition.DefinitionId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate survival consumable {definition.DefinitionId}.");
            }
        }

        Dictionary<string, PlayerEnvironmentDefinition> environments = new(StringComparer.Ordinal);
        foreach (PlayerEnvironmentDefinition definition in
            document.Environments ?? Array.Empty<PlayerEnvironmentDefinition>())
        {
            ValidateEnvironment(definition);
            if (!environments.TryAdd(definition.Archetype, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate environment archetype {definition.Archetype}.");
            }
        }

        ValidateExactSet(
            suitModules.Keys,
            RequiredSuitModuleIds,
            "suit modules");
        ValidateExactSet(
            multitoolModules.Keys,
            RequiredMultitoolModuleIds,
            "multitool modules");
        ValidateExactSet(
            consumables.Keys,
            RequiredConsumableIds,
            "survival consumables");
        ValidateExactSet(
            environments.Keys,
            RequiredEnvironmentArchetypes,
            "environment archetypes");

        string[] suitRecipeOutputs = contentCatalog.Recipes.Values
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Where(id => id.StartsWith("module.suit.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] configuredSuitModules = suitModules.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!suitRecipeOutputs.SequenceEqual(configuredSuitModules, StringComparer.Ordinal))
        {
            throw new ContentValidationException(
                "Suit module definitions must exactly match all module.suit recipe outputs.");
        }

        string[] toolRecipeOutputs = contentCatalog.Recipes.Values
            .Where(recipe => string.Equals(recipe.Category, "Tool", StringComparison.Ordinal))
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] configuredToolModules = multitoolModules.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!toolRecipeOutputs.SequenceEqual(configuredToolModules, StringComparer.Ordinal))
        {
            throw new ContentValidationException(
                "Multitool module definitions must exactly match Tool recipe outputs.");
        }

        return new PlayerSurvivalCatalog(
            document.SchemaVersion,
            document.SuitSlotLimit,
            document.MultitoolSlotLimit,
            document.BaseStats,
            suitModules,
            multitoolModules,
            consumables,
            environments);
    }

    private static void ValidateBaseStats(PlayerSurvivalBaseStatsDefinition? stats)
    {
        if (stats is null ||
            stats.Health <= 0.0 ||
            stats.Shield < 0.0 ||
            stats.Stamina <= 0.0 ||
            stats.LifeSupport <= 0.0 ||
            stats.HazardProtection <= 0.0 ||
            stats.TemperatureProtection is < 0.0 or > 100.0 ||
            stats.RadiationProtection is < 0.0 or > 100.0 ||
            stats.ToxicProtection is < 0.0 or > 100.0 ||
            stats.Oxygen <= 0.0 ||
            stats.JetpackEnergy <= 0.0 ||
            stats.MultitoolEnergy <= 0.0)
        {
            throw new ContentValidationException(
                "player_survival base stats contain invalid values.");
        }
    }

    private static void ValidateSuitModule(
        PlayerSuitModuleDefinition definition,
        GameContentCatalog contentCatalog)
    {
        if (!GameContentCatalog.IsStableId(definition.ModuleId) ||
            !definition.ModuleId.StartsWith("module.suit.", StringComparison.Ordinal) ||
            !contentCatalog.Items.ContainsKey(definition.ModuleId) ||
            definition.TemperatureProtectionBonus < 0.0 ||
            definition.RadiationProtectionBonus < 0.0 ||
            definition.ToxicProtectionBonus < 0.0 ||
            definition.HazardCapacityBonus < 0.0 ||
            definition.LifeSupportCapacityBonus < 0.0 ||
            definition.OxygenCapacityBonus < 0.0)
        {
            throw new ContentValidationException(
                $"Invalid suit module definition {definition.ModuleId}.");
        }
    }

    private static void ValidateMultitoolModule(
        PlayerMultitoolModuleDefinition definition,
        GameContentCatalog contentCatalog)
    {
        if (!GameContentCatalog.IsStableId(definition.ModuleId) ||
            !definition.ModuleId.StartsWith("tool.", StringComparison.Ordinal) ||
            !contentCatalog.Items.ContainsKey(definition.ModuleId) ||
            !SupportedMultitoolFunctions.Contains(definition.Function) ||
            definition.EnergyCostMultiplier is <= 0.0 or > 1.0 ||
            definition.EffectivenessBonus is < 0.0 or > 2.0)
        {
            throw new ContentValidationException(
                $"Invalid multitool module definition {definition.ModuleId}.");
        }
    }

    private static void ValidateConsumable(
        PlayerConsumableDefinition definition,
        GameContentCatalog contentCatalog)
    {
        double[] values =
        {
            definition.HealthRestore,
            definition.ShieldRestore,
            definition.LifeSupportRestore,
            definition.HazardRestore,
            definition.OxygenRestore,
            definition.JetpackRestore,
            definition.MultitoolEnergyRestore
        };
        if (!GameContentCatalog.IsStableId(definition.DefinitionId) ||
            !definition.DefinitionId.StartsWith("consumable.", StringComparison.Ordinal) ||
            !contentCatalog.Items.ContainsKey(definition.DefinitionId) ||
            values.Any(value => value < 0.0 || !double.IsFinite(value)) ||
            values.All(value => value <= 0.0))
        {
            throw new ContentValidationException(
                $"Invalid player consumable definition {definition.DefinitionId}.");
        }
    }

    private static void ValidateEnvironment(PlayerEnvironmentDefinition definition)
    {
        double[] hazards =
        {
            definition.TemperatureHazard,
            definition.RadiationHazard,
            definition.ToxicHazard,
            definition.LifeSupportDrainPerSecond,
            definition.OxygenDrainPerSecond
        };
        if (string.IsNullOrWhiteSpace(definition.Archetype) ||
            definition.Archetype.Any(character =>
                !char.IsLower(character) && character != '_') ||
            hazards.Any(value => value < 0.0 || !double.IsFinite(value)) ||
            definition.TemperatureHazard > 2.0 ||
            definition.RadiationHazard > 2.0 ||
            definition.ToxicHazard > 2.0)
        {
            throw new ContentValidationException(
                $"Invalid environment definition {definition.Archetype}.");
        }
    }

    private static void ValidateExactSet(
        IEnumerable<string> actual,
        IEnumerable<string> expected,
        string label)
    {
        string[] orderedActual = actual.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] orderedExpected = expected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!orderedActual.SequenceEqual(orderedExpected, StringComparer.Ordinal))
        {
            throw new ContentValidationException(
                $"player_survival {label} do not match the required set.");
        }
    }

    private sealed record PlayerSurvivalDocument(
        int SchemaVersion,
        int SuitSlotLimit,
        int MultitoolSlotLimit,
        PlayerSurvivalBaseStatsDefinition BaseStats,
        IReadOnlyList<PlayerSuitModuleDefinition>? SuitModules,
        IReadOnlyList<PlayerMultitoolModuleDefinition>? MultitoolModules,
        IReadOnlyList<PlayerConsumableDefinition>? Consumables,
        IReadOnlyList<PlayerEnvironmentDefinition>? Environments);
}
