using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record ShipBaseStatsDefinition(
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

public sealed record ShipModuleEffectsDefinition(
    double Hull,
    double Shield,
    int CargoCapacity,
    double FuelCapacity,
    double Acceleration,
    double MaxSpeed,
    double Maneuverability,
    double HyperdriveRange,
    double AtmosphericEfficiency);

public sealed record ShipClassDefinition(
    string ShipClassId,
    string LocalizationKey,
    ShipBaseStatsDefinition BaseStats);

public sealed record ShipSystemDefinition(
    string SystemId,
    string LocalizationKey,
    string RepairDefinitionId,
    double RepairPerUnit);

public sealed record ShipModuleDefinition(
    string ModuleId,
    string LocalizationKey,
    string SlotType,
    IReadOnlyList<string> AffectedSystems,
    double DurabilityBonus,
    ShipModuleEffectsDefinition Effects,
    bool EnablesHyperspace);

public sealed class ShipSystemsCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedClassCount = 6;
    public const int ExpectedModuleCount = 18;
    public const int ExpectedSystemCount = 7;

    private static readonly string[] RequiredClassIds =
    {
        "ship.class.explorer",
        "ship.class.cargo",
        "ship.class.fighter",
        "ship.class.versatile",
        "ship.class.exotic",
        "ship.class.heavy_expeditionary"
    };

    private static readonly string[] RequiredSystemIds =
    {
        "ship.system.hull",
        "ship.system.shield",
        "ship.system.engine",
        "ship.system.impulse",
        "ship.system.hyperdrive",
        "ship.system.weapon",
        "ship.system.landing"
    };

    private static readonly HashSet<string> SupportedSlotTypes = new(
        new[] { "Technology", "Weapon" },
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

    private readonly Dictionary<string, ShipClassDefinition> _classes;
    private readonly Dictionary<string, ShipSystemDefinition> _systems;
    private readonly Dictionary<string, ShipModuleDefinition> _modules;

    private ShipSystemsCatalog(
        int schemaVersion,
        string starterClassId,
        Dictionary<string, ShipClassDefinition> classes,
        Dictionary<string, ShipSystemDefinition> systems,
        Dictionary<string, ShipModuleDefinition> modules)
    {
        SchemaVersion = schemaVersion;
        StarterClassId = starterClassId;
        _classes = classes;
        _systems = systems;
        _modules = modules;
    }

    public int SchemaVersion { get; }

    public string StarterClassId { get; }

    public IReadOnlyDictionary<string, ShipClassDefinition> Classes => _classes;

    public IReadOnlyDictionary<string, ShipSystemDefinition> Systems => _systems;

    public IReadOnlyDictionary<string, ShipModuleDefinition> Modules => _modules;

    public ShipClassDefinition GetClass(string shipClassId)
    {
        return _classes.TryGetValue(shipClassId, out ShipClassDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown ship class {shipClassId}.");
    }

    public ShipSystemDefinition GetSystem(string systemId)
    {
        return _systems.TryGetValue(systemId, out ShipSystemDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown ship system {systemId}.");
    }

    public ShipModuleDefinition GetModule(string moduleId)
    {
        return _modules.TryGetValue(moduleId, out ShipModuleDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown ship module {moduleId}.");
    }

    public static ShipSystemsCatalog LoadFromJson(
        string json,
        GameContentCatalog contentCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ShipSystemsDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ShipSystemsDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "ships.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"ships.json is invalid: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ContentValidationException(
                $"ships.json schema {document.SchemaVersion} is not supported; " +
                $"expected {CurrentSchemaVersion}.");
        }

        Dictionary<string, ShipClassDefinition> classes = new(
            StringComparer.Ordinal);
        foreach (ShipClassDefinition definition in
            document.Classes ?? Array.Empty<ShipClassDefinition>())
        {
            ValidateClass(definition);
            if (!classes.TryAdd(definition.ShipClassId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate ship class {definition.ShipClassId}.");
            }
        }

        Dictionary<string, ShipSystemDefinition> systems = new(
            StringComparer.Ordinal);
        foreach (ShipSystemDefinition definition in
            document.Systems ?? Array.Empty<ShipSystemDefinition>())
        {
            ValidateSystem(definition, contentCatalog);
            if (!systems.TryAdd(definition.SystemId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate ship system {definition.SystemId}.");
            }
        }

        Dictionary<string, ShipModuleDefinition> modules = new(
            StringComparer.Ordinal);
        foreach (ShipModuleDefinition definition in
            document.Modules ?? Array.Empty<ShipModuleDefinition>())
        {
            ValidateModule(definition, systems, contentCatalog);
            if (!modules.TryAdd(definition.ModuleId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate ship module {definition.ModuleId}.");
            }
        }

        if (classes.Count != ExpectedClassCount ||
            systems.Count != ExpectedSystemCount ||
            modules.Count != ExpectedModuleCount)
        {
            throw new ContentValidationException(
                "ships.json must define exactly " +
                $"{ExpectedClassCount} classes, {ExpectedSystemCount} systems " +
                $"and {ExpectedModuleCount} modules; found " +
                $"{classes.Count}/{systems.Count}/{modules.Count}.");
        }

        string[] missingClasses = RequiredClassIds
            .Where(id => !classes.ContainsKey(id))
            .ToArray();
        string[] missingSystems = RequiredSystemIds
            .Where(id => !systems.ContainsKey(id))
            .ToArray();
        if (missingClasses.Length > 0 || missingSystems.Length > 0)
        {
            throw new ContentValidationException(
                "ships.json is missing required PDF ship definitions: " +
                string.Join(", ", missingClasses.Concat(missingSystems)));
        }

        if (!GameContentCatalog.IsStableId(document.StarterClassId) ||
            !classes.ContainsKey(document.StarterClassId))
        {
            throw new ContentValidationException(
                $"Unknown starter ship class {document.StarterClassId}.");
        }

        string[] recipeModuleOutputs = contentCatalog.Recipes.Values
            .Where(recipe => string.Equals(
                recipe.Category,
                "ShipModule",
                StringComparison.Ordinal))
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] catalogModules = modules.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!recipeModuleOutputs.SequenceEqual(
            catalogModules,
            StringComparer.Ordinal))
        {
            throw new ContentValidationException(
                "ships.json module set must exactly match all ShipModule recipe outputs.");
        }

        return new ShipSystemsCatalog(
            document.SchemaVersion,
            document.StarterClassId,
            classes,
            systems,
            modules);
    }

    private static void ValidateClass(ShipClassDefinition definition)
    {
        ShipBaseStatsDefinition? stats = definition.BaseStats;
        if (!GameContentCatalog.IsStableId(definition.ShipClassId) ||
            !definition.ShipClassId.StartsWith(
                "ship.class.",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(definition.LocalizationKey) ||
            stats is null ||
            stats.Hull <= 0.0 ||
            stats.Shield < 0.0 ||
            stats.CargoCapacity <= 0 ||
            stats.FuelCapacity <= 0.0 ||
            stats.Acceleration <= 0.0 ||
            stats.MaxSpeed <= 0.0 ||
            stats.Maneuverability <= 0.0 ||
            stats.WeaponSlots is < 0 or > 12 ||
            stats.TechnologySlots is < 1 or > 24 ||
            stats.HyperdriveRange < 0.0 ||
            stats.AtmosphericEfficiency is < 0.0 or > 100.0)
        {
            throw new ContentValidationException(
                $"Ship class {definition.ShipClassId} contains invalid values.");
        }
    }

    private static void ValidateSystem(
        ShipSystemDefinition definition,
        GameContentCatalog contentCatalog)
    {
        if (!GameContentCatalog.IsStableId(definition.SystemId) ||
            !definition.SystemId.StartsWith(
                "ship.system.",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(definition.LocalizationKey) ||
            !contentCatalog.Items.ContainsKey(definition.RepairDefinitionId) ||
            definition.RepairPerUnit is <= 0.0 or > 100.0)
        {
            throw new ContentValidationException(
                $"Ship system {definition.SystemId} contains invalid values.");
        }
    }

    private static void ValidateModule(
        ShipModuleDefinition definition,
        IReadOnlyDictionary<string, ShipSystemDefinition> systems,
        GameContentCatalog contentCatalog)
    {
        ShipModuleEffectsDefinition? effects = definition.Effects;
        bool itemExists = contentCatalog.Items.TryGetValue(
            definition.ModuleId,
            out GameItemDefinition? item);
        bool tagged = itemExists && item is not null &&
            item.Tags.Contains("ship", StringComparer.Ordinal) &&
            item.Tags.Contains("module", StringComparer.Ordinal);
        if (!GameContentCatalog.IsStableId(definition.ModuleId) ||
            !definition.ModuleId.StartsWith(
                "module.ship.",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(definition.LocalizationKey) ||
            !SupportedSlotTypes.Contains(definition.SlotType) ||
            definition.AffectedSystems is null ||
            definition.AffectedSystems.Count == 0 ||
            definition.AffectedSystems.Distinct(StringComparer.Ordinal).Count() !=
                definition.AffectedSystems.Count ||
            definition.AffectedSystems.Any(id => !systems.ContainsKey(id)) ||
            definition.DurabilityBonus is < 0.0 or > 100.0 ||
            effects is null ||
            effects.Hull < 0.0 ||
            effects.Shield < 0.0 ||
            effects.CargoCapacity < 0 ||
            effects.FuelCapacity < 0.0 ||
            effects.Acceleration < 0.0 ||
            effects.MaxSpeed < 0.0 ||
            effects.Maneuverability < 0.0 ||
            effects.HyperdriveRange < 0.0 ||
            effects.AtmosphericEfficiency < 0.0 ||
            !tagged)
        {
            throw new ContentValidationException(
                $"Ship module {definition.ModuleId} contains invalid values.");
        }
    }

    private sealed record ShipSystemsDocument(
        int SchemaVersion,
        string StarterClassId,
        IReadOnlyList<ShipClassDefinition>? Classes,
        IReadOnlyList<ShipSystemDefinition>? Systems,
        IReadOnlyList<ShipModuleDefinition>? Modules);
}
