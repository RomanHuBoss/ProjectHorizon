using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record BaseVector3Definition(double X, double Y, double Z);

public sealed record BaseColorDefinition(double R, double G, double B);

public sealed record BaseConstructionLimits(
    int MaximumModules,
    int MaximumInteractiveDevices,
    int MaximumActivePhysicsObjects,
    int MaximumDynamicLights);

public sealed record BaseModuleDefinition(
    string ModuleId,
    string? ItemDefinitionId,
    string LocalizationKey,
    string Category,
    string Shape,
    BaseVector3Definition Size,
    BaseColorDefinition Color,
    int StarterStock,
    bool IsAnchor,
    int InteractiveDevices,
    int ActivePhysicsObjects,
    int DynamicLights,
    double PowerGeneration,
    double PowerConsumption,
    double BatteryCapacity);

public sealed class BaseConstructionCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedModuleCount = 50;

    private static readonly string[] RequiredCategories =
    {
        "Foundation", "Floor", "Wall", "Roof", "Corridor", "Door",
        "Window", "Stair", "Room", "Structure", "Generator", "Battery",
        "Processor", "Storage", "LandingPad", "Terminal", "Decoration"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly Dictionary<string, BaseModuleDefinition> _modules;

    private BaseConstructionCatalog(
        int schemaVersion,
        double gridSizeMeters,
        BaseConstructionLimits limits,
        Dictionary<string, BaseModuleDefinition> modules)
    {
        SchemaVersion = schemaVersion;
        GridSizeMeters = gridSizeMeters;
        Limits = limits;
        _modules = modules;
    }

    public int SchemaVersion { get; }

    public double GridSizeMeters { get; }

    public BaseConstructionLimits Limits { get; }

    public IReadOnlyDictionary<string, BaseModuleDefinition> Modules => _modules;

    public BaseModuleDefinition GetModule(string moduleId)
    {
        return _modules.TryGetValue(moduleId, out BaseModuleDefinition? value)
            ? value
            : throw new KeyNotFoundException($"Unknown base module {moduleId}.");
    }

    public static BaseConstructionCatalog LoadFromJson(
        string json,
        GameContentCatalog contentCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        BaseConstructionDocument document;
        try
        {
            document = JsonSerializer.Deserialize<BaseConstructionDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "base_construction.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"base_construction.json is invalid: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ContentValidationException(
                $"base_construction.json schema {document.SchemaVersion} is not " +
                $"supported; expected {CurrentSchemaVersion}.");
        }

        if (document.GridSizeMeters is < 1.0 or > 10.0)
        {
            throw new ContentValidationException(
                "Base construction grid size must be between 1 and 10 metres.");
        }

        BaseConstructionLimits limits = document.Limits ??
            throw new ContentValidationException(
                "base_construction.json is missing limits.");
        if (limits.MaximumModules != 500 ||
            limits.MaximumInteractiveDevices != 100 ||
            limits.MaximumActivePhysicsObjects != 200 ||
            limits.MaximumDynamicLights != 20)
        {
            throw new ContentValidationException(
                "Base construction limits must match PDF section 20.2: " +
                "500 modules / 100 interactive devices / 200 active physics " +
                "objects / 20 dynamic lights.");
        }

        Dictionary<string, BaseModuleDefinition> modules =
            new(StringComparer.Ordinal);
        foreach (BaseModuleDefinition definition in
            document.Definitions ?? Array.Empty<BaseModuleDefinition>())
        {
            ValidateDefinition(definition, contentCatalog);
            if (!modules.TryAdd(definition.ModuleId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate base module ID {definition.ModuleId}.");
            }
        }

        if (modules.Count != ExpectedModuleCount)
        {
            throw new ContentValidationException(
                $"Base construction catalog must define exactly " +
                $"{ExpectedModuleCount} modules; found {modules.Count}.");
        }

        if (modules.Values.Count(module => module.IsAnchor) != 1)
        {
            throw new ContentValidationException(
                "Base construction catalog must define exactly one anchor module.");
        }

        string[] missingCategories = RequiredCategories
            .Where(category => !modules.Values.Any(module => string.Equals(
                module.Category,
                category,
                StringComparison.Ordinal)))
            .ToArray();
        if (missingCategories.Length > 0)
        {
            throw new ContentValidationException(
                "Base construction catalog is missing PDF section 20.1 " +
                $"categories: {string.Join(", ", missingCategories)}.");
        }

        string[] baseRecipeOutputs = contentCatalog.Recipes.Values
            .Where(recipe => string.Equals(
                recipe.Category,
                "Base",
                StringComparison.Ordinal))
            .SelectMany(recipe => recipe.Outputs)
            .Select(output => output.DefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] moduleItems = modules.Values
            .Where(module => !string.IsNullOrWhiteSpace(
                module.ItemDefinitionId))
            .Select(module => module.ItemDefinitionId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!baseRecipeOutputs.SequenceEqual(moduleItems, StringComparer.Ordinal))
        {
            throw new ContentValidationException(
                "Catalog-backed base construction definitions must cover " +
                "exactly the ten Base recipe outputs from Industry Content v2.");
        }

        return new BaseConstructionCatalog(
            document.SchemaVersion,
            document.GridSizeMeters,
            limits,
            modules);
    }

    private static void ValidateDefinition(
        BaseModuleDefinition definition,
        GameContentCatalog contentCatalog)
    {
        if (!GameContentCatalog.IsStableId(definition.ModuleId) ||
            string.IsNullOrWhiteSpace(definition.LocalizationKey) ||
            string.IsNullOrWhiteSpace(definition.Category) ||
            definition.Shape is not "Box" and not "Cylinder" ||
            definition.Size is null ||
            definition.Color is null ||
            definition.Size.X <= 0.0 ||
            definition.Size.Y <= 0.0 ||
            definition.Size.Z <= 0.0 ||
            definition.Color.R is < 0.0 or > 1.0 ||
            definition.Color.G is < 0.0 or > 1.0 ||
            definition.Color.B is < 0.0 or > 1.0 ||
            definition.StarterStock <= 0 ||
            definition.InteractiveDevices is < 0 or > 100 ||
            definition.ActivePhysicsObjects is < 0 or > 200 ||
            definition.DynamicLights is < 0 or > 20 ||
            definition.PowerGeneration < 0.0 ||
            definition.PowerConsumption < 0.0 ||
            definition.BatteryCapacity < 0.0)
        {
            throw new ContentValidationException(
                $"Base module {definition.ModuleId} contains invalid values.");
        }

        if (!string.IsNullOrWhiteSpace(definition.ItemDefinitionId))
        {
            if (!GameContentCatalog.IsStableId(definition.ItemDefinitionId) ||
                !contentCatalog.Items.TryGetValue(
                    definition.ItemDefinitionId,
                    out GameItemDefinition? item) ||
                !string.Equals(
                    item.Category,
                    "Building",
                    StringComparison.Ordinal))
            {
                throw new ContentValidationException(
                    $"Base module {definition.ModuleId} references missing or " +
                    $"non-Building item {definition.ItemDefinitionId}.");
            }
        }
    }

    private sealed record BaseConstructionDocument(
        int SchemaVersion,
        double GridSizeMeters,
        BaseConstructionLimits? Limits,
        IReadOnlyList<BaseModuleDefinition>? Definitions);
}
