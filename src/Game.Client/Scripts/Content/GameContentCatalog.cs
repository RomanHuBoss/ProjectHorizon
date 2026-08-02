using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public static class StarterRepairContentIds
{
    public const string RecipeId = "recipe.ship.starter_repair";
}

public static class VerticalSliceContentIds
{
    public const string LaunchCapacitorRecipeId =
        "recipe.ship.launch_capacitor";
    public const string ConductiveCrystalResourceId =
        "resource.conductive_crystal";
    public const string LaunchCapacitorItemId =
        "component.ship.launch_capacitor";
    public const string NavigationArrayRecipeId =
        "recipe.ship.navigation_array";
    public const string PhaseFiberResourceId =
        "resource.phase_fiber";
    public const string NavigationArrayItemId =
        "component.ship.navigation_array";
    public const string CoolantRegulatorRecipeId =
        "recipe.ship.coolant_regulator";
    public const string ThermalGelResourceId =
        "resource.thermal_gel";
    public const string CoolantRegulatorItemId =
        "component.ship.coolant_regulator";
    public const string PowerCouplerRecipeId =
        "recipe.ship.power_coupler";
    public const string PlasmaFilamentResourceId =
        "resource.plasma_filament";
    public const string PowerCouplerItemId =
        "component.ship.power_coupler";
}

public sealed class ContentValidationException : Exception
{
    public ContentValidationException(string message)
        : base(message)
    {
    }
}

public sealed record GameItemDefinition(
    string DefinitionId,
    string LocalizationKey,
    string Category,
    int MaxStack,
    double BasePrice,
    double Mass,
    string Rarity,
    int TechnologyTier,
    string StateOfMatter,
    string Icon,
    string WorldModel,
    IReadOnlyList<string> Tags);

public sealed record ResourceVisualDefinition(
    double AlbedoR,
    double AlbedoG,
    double AlbedoB,
    double EmissionR,
    double EmissionG,
    double EmissionB,
    double EmissionEnergy,
    double Metallic,
    double Roughness);

public sealed record GameResourceDefinition(
    string ResourceId,
    string ItemDefinitionId,
    int MinimumYield,
    int MaximumYield,
    int ScanTier,
    string ExtractionMethod,
    ResourceVisualDefinition Visual,
    IReadOnlyList<string> Tags)
{
    public int GetDeterministicYield()
    {
        if (MinimumYield != MaximumYield)
        {
            throw new ContentValidationException(
                $"Resource {ResourceId} uses a yield range " +
                $"{MinimumYield}..{MaximumYield}; the current vertical slice " +
                "requires deterministic resource nodes.");
        }

        return MinimumYield;
    }
}

public sealed record CraftingStackDefinition(
    string DefinitionId,
    int Quantity);

public sealed record CatalystStackDefinition(
    string DefinitionId,
    int Quantity,
    double ConsumptionChance);

public sealed record RecipeEnvironmentDefinition(
    double MinimumTemperatureKelvin,
    double MaximumTemperatureKelvin,
    double MinimumPressureKPa,
    double MaximumPressureKPa,
    bool RequiresVacuum);

public sealed record RecipeQualityDefinition(
    int Minimum,
    int Maximum);

public sealed record RecipeApplicationDefinition(
    string Type,
    string TargetId,
    double ResultHealth);

public sealed record CraftingRecipeDefinition(
    string RecipeId,
    string Category,
    int TechnologyTier,
    IReadOnlyList<CraftingStackDefinition> Inputs,
    IReadOnlyList<CatalystStackDefinition> Catalysts,
    IReadOnlyList<CraftingStackDefinition> Outputs,
    IReadOnlyList<CraftingStackDefinition> Byproducts,
    IReadOnlyList<CraftingStackDefinition> DismantleReturns,
    string RequiredTechnology,
    string RequiredStation,
    int StationTier,
    double CraftTimeSeconds,
    double EnergyCost,
    int BatchSize,
    bool RuntimeEnabled,
    IReadOnlyList<string> RequiredTools,
    RecipeEnvironmentDefinition Environment,
    RecipeQualityDefinition Quality,
    IReadOnlyList<string> Hazards,
    RecipeApplicationDefinition Application,
    IReadOnlyList<string> Tags);

public sealed record CraftingStationDefinition(
    string StationId,
    string LocalizationKey,
    int Tier,
    double EnergyCapacity,
    int ParallelSlots,
    IReadOnlyList<string> SupportedCategories,
    IReadOnlyList<string> Tags);

public sealed record TechnologyDefinition(
    string TechnologyId,
    string LocalizationKey,
    int Tier,
    int ResearchCost,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> Tags);

public sealed record IndustryCatalogAnalysis(
    int ItemCount,
    int ResourceCount,
    int RecipeCount,
    int StationCount,
    int TechnologyCount,
    int RuntimeEnabledRecipes,
    int ChemistryRecipes,
    int CompotiumRecipes,
    int ParaffiniumRecipes,
    int RecipesWithCatalysts,
    int RecipesWithByproducts,
    int RecipesWithEnvironmentControls,
    int DependencyCycles,
    int UnreachableRecipes,
    IReadOnlyList<string> UnreachableRecipeIds);

public sealed class GameContentCatalog
{
    public const int CurrentSchemaVersion = 2;

    private static readonly Regex StableIdPattern = new(
        "^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TagPattern = new(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly Dictionary<string, GameItemDefinition> _items;
    private readonly Dictionary<string, GameResourceDefinition> _resources;
    private readonly Dictionary<string, CraftingRecipeDefinition> _recipes;
    private readonly Dictionary<string, CraftingStationDefinition> _stations;
    private readonly Dictionary<string, TechnologyDefinition> _technologies;

    private GameContentCatalog(
        int schemaVersion,
        Dictionary<string, GameItemDefinition> items,
        Dictionary<string, GameResourceDefinition> resources,
        Dictionary<string, CraftingRecipeDefinition> recipes,
        Dictionary<string, CraftingStationDefinition> stations,
        Dictionary<string, TechnologyDefinition> technologies)
    {
        SchemaVersion = schemaVersion;
        _items = items;
        _resources = resources;
        _recipes = recipes;
        _stations = stations;
        _technologies = technologies;
    }

    public int SchemaVersion { get; }

    public IReadOnlyDictionary<string, GameItemDefinition> Items => _items;

    public IReadOnlyDictionary<string, GameResourceDefinition> Resources =>
        _resources;

    public IReadOnlyDictionary<string, CraftingRecipeDefinition> Recipes =>
        _recipes;

    public IReadOnlyDictionary<string, CraftingStationDefinition> Stations =>
        _stations;

    public IReadOnlyDictionary<string, TechnologyDefinition> Technologies =>
        _technologies;

    public static GameContentCatalog LoadFromJson(
        string itemsJson,
        string resourcesJson,
        string recipesJson)
    {
        ItemCatalogDocument itemsDocument = Deserialize<ItemCatalogDocument>(
            itemsJson,
            "items.json");
        ResourceCatalogDocument resourcesDocument =
            Deserialize<ResourceCatalogDocument>(
                resourcesJson,
                "resources.json");
        RecipeCatalogDocument recipesDocument =
            Deserialize<RecipeCatalogDocument>(
                recipesJson,
                "recipes.json");
        ValidateMatchingSchemaVersions(
            itemsDocument.SchemaVersion,
            resourcesDocument.SchemaVersion,
            recipesDocument.SchemaVersion);

        return Create(
            itemsDocument.SchemaVersion,
            (itemsDocument.Definitions ?? Array.Empty<ItemDefinitionDocument>())
                .Select(MapItem),
            (resourcesDocument.Definitions ??
                Array.Empty<ResourceDefinitionDocument>())
                .Select(MapResource),
            (recipesDocument.Definitions ??
                Array.Empty<RecipeDefinitionDocument>())
                .Select(MapRecipe));
    }

    public static GameContentCatalog LoadFromJson(
        string itemsJson,
        string resourcesJson,
        string recipesJson,
        string stationsJson,
        string technologiesJson)
    {
        ItemCatalogDocument itemsDocument = Deserialize<ItemCatalogDocument>(
            itemsJson,
            "items.json");
        ResourceCatalogDocument resourcesDocument =
            Deserialize<ResourceCatalogDocument>(
                resourcesJson,
                "resources.json");
        RecipeCatalogDocument recipesDocument =
            Deserialize<RecipeCatalogDocument>(
                recipesJson,
                "recipes.json");
        StationCatalogDocument stationsDocument =
            Deserialize<StationCatalogDocument>(
                stationsJson,
                "stations.json");
        TechnologyCatalogDocument technologiesDocument =
            Deserialize<TechnologyCatalogDocument>(
                technologiesJson,
                "technologies.json");
        ValidateMatchingSchemaVersions(
            itemsDocument.SchemaVersion,
            resourcesDocument.SchemaVersion,
            recipesDocument.SchemaVersion,
            stationsDocument.SchemaVersion,
            technologiesDocument.SchemaVersion);

        return Create(
            itemsDocument.SchemaVersion,
            (itemsDocument.Definitions ?? Array.Empty<ItemDefinitionDocument>())
                .Select(MapItem),
            (resourcesDocument.Definitions ??
                Array.Empty<ResourceDefinitionDocument>())
                .Select(MapResource),
            (recipesDocument.Definitions ??
                Array.Empty<RecipeDefinitionDocument>())
                .Select(MapRecipe),
            (stationsDocument.Definitions ??
                Array.Empty<StationDefinitionDocument>())
                .Select(MapStation),
            (technologiesDocument.Definitions ??
                Array.Empty<TechnologyDefinitionDocument>())
                .Select(MapTechnology));
    }

    public static GameContentCatalog Create(
        int schemaVersion,
        IEnumerable<GameItemDefinition> items,
        IEnumerable<GameResourceDefinition> resources,
        IEnumerable<CraftingRecipeDefinition> recipes)
    {
        return CreateCore(
            schemaVersion,
            items,
            resources,
            recipes,
            Array.Empty<CraftingStationDefinition>(),
            Array.Empty<TechnologyDefinition>(),
            validateIndustryReferences: false);
    }

    public static GameContentCatalog Create(
        int schemaVersion,
        IEnumerable<GameItemDefinition> items,
        IEnumerable<GameResourceDefinition> resources,
        IEnumerable<CraftingRecipeDefinition> recipes,
        IEnumerable<CraftingStationDefinition> stations,
        IEnumerable<TechnologyDefinition> technologies)
    {
        return CreateCore(
            schemaVersion,
            items,
            resources,
            recipes,
            stations,
            technologies,
            validateIndustryReferences: true);
    }

    private static GameContentCatalog CreateCore(
        int schemaVersion,
        IEnumerable<GameItemDefinition> items,
        IEnumerable<GameResourceDefinition> resources,
        IEnumerable<CraftingRecipeDefinition> recipes,
        IEnumerable<CraftingStationDefinition> stations,
        IEnumerable<TechnologyDefinition> technologies,
        bool validateIndustryReferences)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(technologies);
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ContentValidationException(
                $"Unsupported content schema {schemaVersion}; " +
                $"expected {CurrentSchemaVersion}.");
        }

        Dictionary<string, GameItemDefinition> itemMap = BuildUniqueMap(
            items,
            item => item.DefinitionId,
            "item");
        Dictionary<string, GameResourceDefinition> resourceMap = BuildUniqueMap(
            resources,
            resource => resource.ResourceId,
            "resource");
        Dictionary<string, CraftingRecipeDefinition> recipeMap = BuildUniqueMap(
            recipes,
            recipe => recipe.RecipeId,
            "recipe");
        Dictionary<string, CraftingStationDefinition> stationMap = BuildUniqueMap(
            stations,
            station => station.StationId,
            "station");
        Dictionary<string, TechnologyDefinition> technologyMap = BuildUniqueMap(
            technologies,
            technology => technology.TechnologyId,
            "technology");

        if (itemMap.Count == 0)
            throw new ContentValidationException(
                "items.json must contain at least one definition.");
        if (resourceMap.Count == 0)
            throw new ContentValidationException(
                "resources.json must contain at least one definition.");
        if (recipeMap.Count == 0)
            throw new ContentValidationException(
                "recipes.json must contain at least one definition.");
        if (validateIndustryReferences && stationMap.Count == 0)
            throw new ContentValidationException(
                "stations.json must contain at least one definition.");
        if (validateIndustryReferences && technologyMap.Count == 0)
            throw new ContentValidationException(
                "technologies.json must contain at least one definition.");

        foreach (GameItemDefinition item in itemMap.Values)
            ValidateItem(item);
        foreach (GameResourceDefinition resource in resourceMap.Values)
            ValidateResource(resource, itemMap);
        foreach (CraftingStationDefinition station in stationMap.Values)
            ValidateStation(station);
        foreach (TechnologyDefinition technology in technologyMap.Values)
            ValidateTechnology(technology, technologyMap);
        ValidateTechnologyGraph(technologyMap);
        foreach (CraftingRecipeDefinition recipe in recipeMap.Values)
            ValidateRecipe(
                recipe,
                itemMap,
                stationMap,
                technologyMap,
                validateIndustryReferences);

        GameContentCatalog catalog = new(
            schemaVersion,
            itemMap,
            resourceMap,
            recipeMap,
            stationMap,
            technologyMap);
        if (validateIndustryReferences)
        {
            IndustryCatalogAnalysis analysis = catalog.AnalyzeIndustry();
            if (analysis.DependencyCycles != 0)
            {
                throw new ContentValidationException(
                    $"Industry recipe graph contains " +
                    $"{analysis.DependencyCycles} dependency cycle(s).");
            }

            if (analysis.UnreachableRecipes != 0)
            {
                throw new ContentValidationException(
                    "Industry recipe graph contains unreachable recipes: " +
                    string.Join(", ", analysis.UnreachableRecipeIds));
            }
        }

        return catalog;
    }

    public GameItemDefinition GetItem(string definitionId)
    {
        if (!_items.TryGetValue(
                definitionId,
                out GameItemDefinition? item) ||
            item is null)
        {
            throw new ContentValidationException(
                $"Unknown item definition {definitionId}.");
        }

        return item;
    }

    public GameResourceDefinition GetResource(string resourceId)
    {
        if (!_resources.TryGetValue(
                resourceId,
                out GameResourceDefinition? resource) ||
            resource is null)
        {
            throw new ContentValidationException(
                $"Unknown resource definition {resourceId}.");
        }

        return resource;
    }

    public CraftingRecipeDefinition GetRecipe(string recipeId)
    {
        if (!_recipes.TryGetValue(
                recipeId,
                out CraftingRecipeDefinition? recipe) ||
            recipe is null)
        {
            throw new ContentValidationException(
                $"Unknown recipe definition {recipeId}.");
        }

        return recipe;
    }

    public CraftingStationDefinition GetStation(string stationId)
    {
        if (!_stations.TryGetValue(
                stationId,
                out CraftingStationDefinition? station) ||
            station is null)
        {
            throw new ContentValidationException(
                $"Unknown station definition {stationId}.");
        }

        return station;
    }

    public TechnologyDefinition GetTechnology(string technologyId)
    {
        if (!_technologies.TryGetValue(
                technologyId,
                out TechnologyDefinition? technology) ||
            technology is null)
        {
            throw new ContentValidationException(
                $"Unknown technology definition {technologyId}.");
        }

        return technology;
    }

    public IndustryCatalogAnalysis AnalyzeIndustry()
    {
        HashSet<string> available = Resources.Values
            .Select(resource => resource.ItemDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, CraftingRecipeDefinition> remaining = Recipes
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        bool progressed;
        do
        {
            progressed = false;
            foreach ((string recipeId, CraftingRecipeDefinition recipe) in
                remaining.ToArray())
            {
                IEnumerable<string> required = recipe.Inputs
                    .Select(input => input.DefinitionId)
                    .Concat(recipe.Catalysts.Select(catalyst =>
                        catalyst.DefinitionId));
                if (!required.All(available.Contains))
                    continue;

                foreach (CraftingStackDefinition output in recipe.Outputs)
                    available.Add(output.DefinitionId);
                foreach (CraftingStackDefinition byproduct in recipe.Byproducts)
                    available.Add(byproduct.DefinitionId);
                remaining.Remove(recipeId);
                progressed = true;
            }
        }
        while (progressed);

        int cycles = HasRecipeDependencyCycle() ? 1 : 0;
        return new IndustryCatalogAnalysis(
            Items.Count,
            Resources.Count,
            Recipes.Count,
            Stations.Count,
            Technologies.Count,
            Recipes.Values.Count(recipe => recipe.RuntimeEnabled),
            Recipes.Values.Count(recipe => string.Equals(
                recipe.Category,
                "Chemistry",
                StringComparison.Ordinal)),
            Recipes.Values.Count(recipe =>
                recipe.RecipeId.Contains("compotium", StringComparison.Ordinal) ||
                recipe.Tags.Contains("compotium", StringComparer.Ordinal)),
            Recipes.Values.Count(recipe =>
                recipe.RecipeId.Contains("paraffinium", StringComparison.Ordinal) ||
                recipe.Tags.Contains("paraffinium", StringComparer.Ordinal)),
            Recipes.Values.Count(recipe => recipe.Catalysts.Count > 0),
            Recipes.Values.Count(recipe => recipe.Byproducts.Count > 0),
            Recipes.Values.Count(UsesEnvironmentControls),
            cycles,
            remaining.Count,
            remaining.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    public static bool IsStableId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            StableIdPattern.IsMatch(value);
    }

    private bool HasRecipeDependencyCycle()
    {
        Dictionary<string, string[]> producers = new(StringComparer.Ordinal);
        foreach (CraftingRecipeDefinition recipe in Recipes.Values)
        {
            foreach (CraftingStackDefinition output in recipe.Outputs)
            {
                if (!producers.ContainsKey(output.DefinitionId))
                    producers[output.DefinitionId] = new[] { recipe.RecipeId };
                else
                    producers[output.DefinitionId] = producers[output.DefinitionId]
                        .Append(recipe.RecipeId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
            }

            foreach (CraftingStackDefinition output in recipe.Byproducts)
            {
                if (!producers.ContainsKey(output.DefinitionId))
                    producers[output.DefinitionId] = new[] { recipe.RecipeId };
                else
                    producers[output.DefinitionId] = producers[output.DefinitionId]
                        .Append(recipe.RecipeId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
            }
        }

        Dictionary<string, int> color = Recipes.Keys.ToDictionary(
            id => id,
            _ => 0,
            StringComparer.Ordinal);
        bool Visit(string recipeId)
        {
            color[recipeId] = 1;
            CraftingRecipeDefinition recipe = Recipes[recipeId];
            IEnumerable<string> dependencyItems = recipe.Inputs
                .Select(input => input.DefinitionId)
                .Concat(recipe.Catalysts.Select(catalyst =>
                    catalyst.DefinitionId));
            foreach (string itemId in dependencyItems)
            {
                if (!producers.TryGetValue(itemId, out string[]? producerIds) ||
                    producerIds is null)
                {
                    continue;
                }

                foreach (string producerId in producerIds)
                {
                    if (string.Equals(producerId, recipeId, StringComparison.Ordinal))
                        return true;
                    if (color[producerId] == 1)
                        return true;
                    if (color[producerId] == 0 && Visit(producerId))
                        return true;
                }
            }

            color[recipeId] = 2;
            return false;
        }

        return Recipes.Keys.Any(recipeId =>
            color[recipeId] == 0 && Visit(recipeId));
    }

    private static bool UsesEnvironmentControls(
        CraftingRecipeDefinition recipe)
    {
        RecipeEnvironmentDefinition environment = recipe.Environment;
        return environment.RequiresVacuum ||
            environment.MinimumTemperatureKelvin != 273.0 ||
            environment.MaximumTemperatureKelvin != 373.0 ||
            environment.MinimumPressureKPa != 90.0 ||
            environment.MaximumPressureKPa != 130.0;
    }

    private static void ValidateMatchingSchemaVersions(params int[] versions)
    {
        if (versions.Length == 0)
            return;
        int expected = versions[0];
        if (versions.Any(version => version != expected))
        {
            throw new ContentValidationException(
                "Content JSON schema versions do not match: " +
                string.Join(", ", versions));
        }
    }

    private static T Deserialize<T>(string json, string fileName)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ContentValidationException(
                $"{fileName} is empty.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ??
                throw new ContentValidationException(
                    $"{fileName} deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"{fileName} JSON is invalid at path " +
                $"{exception.Path ?? "<unknown>"}: {exception.Message}");
        }
    }

    private static GameItemDefinition MapItem(
        ItemDefinitionDocument document)
    {
        return new GameItemDefinition(
            document.DefinitionId ?? string.Empty,
            document.LocalizationKey ?? string.Empty,
            document.Category ?? string.Empty,
            document.MaxStack,
            document.BasePrice,
            document.Mass,
            document.Rarity ?? string.Empty,
            document.TechnologyTier,
            document.StateOfMatter ?? string.Empty,
            document.Icon ?? string.Empty,
            document.WorldModel ?? string.Empty,
            CopyStrings(document.Tags));
    }

    private static GameResourceDefinition MapResource(
        ResourceDefinitionDocument document)
    {
        ResourceVisualDocument visual = document.Visual ??
            throw new ContentValidationException(
                $"Resource {document.ResourceId ?? "<missing>"} has no visual block.");
        return new GameResourceDefinition(
            document.ResourceId ?? string.Empty,
            document.ItemDefinitionId ?? string.Empty,
            document.MinimumYield,
            document.MaximumYield,
            document.ScanTier,
            document.ExtractionMethod ?? string.Empty,
            new ResourceVisualDefinition(
                visual.AlbedoR,
                visual.AlbedoG,
                visual.AlbedoB,
                visual.EmissionR,
                visual.EmissionG,
                visual.EmissionB,
                visual.EmissionEnergy,
                visual.Metallic,
                visual.Roughness),
            CopyStrings(document.Tags));
    }

    private static CraftingRecipeDefinition MapRecipe(
        RecipeDefinitionDocument document)
    {
        RecipeApplicationDocument application = document.Application ??
            throw new ContentValidationException(
                $"Recipe {document.RecipeId ?? "<missing>"} has no application block.");
        RecipeEnvironmentDocument environment = document.Environment ??
            throw new ContentValidationException(
                $"Recipe {document.RecipeId ?? "<missing>"} has no environment block.");
        RecipeQualityDocument quality = document.Quality ??
            throw new ContentValidationException(
                $"Recipe {document.RecipeId ?? "<missing>"} has no quality block.");
        return new CraftingRecipeDefinition(
            document.RecipeId ?? string.Empty,
            document.Category ?? string.Empty,
            document.TechnologyTier,
            MapStacks(document.Inputs),
            MapCatalysts(document.Catalysts),
            MapStacks(document.Outputs),
            MapStacks(document.Byproducts),
            MapStacks(document.DismantleReturns),
            document.RequiredTechnology ?? string.Empty,
            document.RequiredStation ?? string.Empty,
            document.StationTier,
            document.CraftTimeSeconds,
            document.EnergyCost,
            document.BatchSize,
            document.RuntimeEnabled,
            CopyStrings(document.RequiredTools),
            new RecipeEnvironmentDefinition(
                environment.MinimumTemperatureKelvin,
                environment.MaximumTemperatureKelvin,
                environment.MinimumPressureKPa,
                environment.MaximumPressureKPa,
                environment.RequiresVacuum),
            new RecipeQualityDefinition(
                quality.Minimum,
                quality.Maximum),
            CopyStrings(document.Hazards),
            new RecipeApplicationDefinition(
                application.Type ?? string.Empty,
                application.TargetId ?? string.Empty,
                application.ResultHealth),
            CopyStrings(document.Tags));
    }

    private static CraftingStationDefinition MapStation(
        StationDefinitionDocument document)
    {
        return new CraftingStationDefinition(
            document.StationId ?? string.Empty,
            document.LocalizationKey ?? string.Empty,
            document.Tier,
            document.EnergyCapacity,
            document.ParallelSlots,
            CopyStrings(document.SupportedCategories),
            CopyStrings(document.Tags));
    }

    private static TechnologyDefinition MapTechnology(
        TechnologyDefinitionDocument document)
    {
        return new TechnologyDefinition(
            document.TechnologyId ?? string.Empty,
            document.LocalizationKey ?? string.Empty,
            document.Tier,
            document.ResearchCost,
            CopyStrings(document.Prerequisites),
            CopyStrings(document.Tags));
    }

    private static IReadOnlyList<CraftingStackDefinition> MapStacks(
        CraftingStackDocument[]? documents)
    {
        return (documents ?? Array.Empty<CraftingStackDocument>())
            .Select(document => new CraftingStackDefinition(
                document.DefinitionId ?? string.Empty,
                document.Quantity))
            .ToArray();
    }

    private static IReadOnlyList<CatalystStackDefinition> MapCatalysts(
        CatalystStackDocument[]? documents)
    {
        return (documents ?? Array.Empty<CatalystStackDocument>())
            .Select(document => new CatalystStackDefinition(
                document.DefinitionId ?? string.Empty,
                document.Quantity,
                document.ConsumptionChance))
            .ToArray();
    }

    private static IReadOnlyList<string> CopyStrings(string[]? values)
    {
        return (values ?? Array.Empty<string>()).ToArray();
    }

    private static Dictionary<string, T> BuildUniqueMap<T>(
        IEnumerable<T> values,
        Func<T, string> idSelector,
        string kind)
    {
        Dictionary<string, T> result = new(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string id = idSelector(value);
            if (!result.TryAdd(id, value))
            {
                throw new ContentValidationException(
                    $"Duplicate {kind} ID {id}.");
            }
        }

        return result;
    }

    private static void ValidateItem(GameItemDefinition item)
    {
        ValidateStableId(item.DefinitionId, "DefinitionId");
        ValidateStableId(item.LocalizationKey, "LocalizationKey");
        RequireText(item.Category, $"{item.DefinitionId}.Category");
        RequireText(item.Rarity, $"{item.DefinitionId}.Rarity");
        RequireText(item.StateOfMatter, $"{item.DefinitionId}.StateOfMatter");
        ValidateTier(item.TechnologyTier, $"{item.DefinitionId}.TechnologyTier");
        if (item.MaxStack <= 0)
            throw new ContentValidationException(
                $"{item.DefinitionId}.MaxStack must be positive.");
        RequireFiniteNonNegative(item.BasePrice, $"{item.DefinitionId}.BasePrice");
        RequireFiniteNonNegative(item.Mass, $"{item.DefinitionId}.Mass");
        ValidateTags(item.Tags, item.DefinitionId);
    }

    private static void ValidateResource(
        GameResourceDefinition resource,
        IReadOnlyDictionary<string, GameItemDefinition> items)
    {
        ValidateStableId(resource.ResourceId, "ResourceId");
        ValidateStableId(resource.ItemDefinitionId, "ItemDefinitionId");
        if (!items.TryGetValue(
                resource.ItemDefinitionId,
                out GameItemDefinition? item) ||
            item is null)
        {
            throw new ContentValidationException(
                $"Resource {resource.ResourceId} references missing item " +
                $"{resource.ItemDefinitionId}.");
        }
        if (!string.Equals(item.Category, "Resource", StringComparison.Ordinal))
        {
            throw new ContentValidationException(
                $"Resource {resource.ResourceId} references item " +
                $"{item.DefinitionId} with category {item.Category}, " +
                "expected Resource.");
        }
        if (resource.MinimumYield <= 0 ||
            resource.MaximumYield < resource.MinimumYield)
        {
            throw new ContentValidationException(
                $"Resource {resource.ResourceId} has invalid yield " +
                $"{resource.MinimumYield}..{resource.MaximumYield}.");
        }
        ValidateTier(resource.ScanTier, $"{resource.ResourceId}.ScanTier");
        RequireText(
            resource.ExtractionMethod,
            $"{resource.ResourceId}.ExtractionMethod");
        ValidateColor(resource.Visual.AlbedoR, resource.ResourceId, "AlbedoR");
        ValidateColor(resource.Visual.AlbedoG, resource.ResourceId, "AlbedoG");
        ValidateColor(resource.Visual.AlbedoB, resource.ResourceId, "AlbedoB");
        ValidateColor(resource.Visual.EmissionR, resource.ResourceId, "EmissionR");
        ValidateColor(resource.Visual.EmissionG, resource.ResourceId, "EmissionG");
        ValidateColor(resource.Visual.EmissionB, resource.ResourceId, "EmissionB");
        RequireFiniteNonNegative(
            resource.Visual.EmissionEnergy,
            $"{resource.ResourceId}.EmissionEnergy");
        ValidateColor(resource.Visual.Metallic, resource.ResourceId, "Metallic");
        ValidateColor(resource.Visual.Roughness, resource.ResourceId, "Roughness");
        ValidateTags(resource.Tags, resource.ResourceId);
    }

    private static void ValidateStation(CraftingStationDefinition station)
    {
        ValidateStableId(station.StationId, "StationId");
        ValidateStableId(station.LocalizationKey, "LocalizationKey");
        if (station.Tier < 1 || station.Tier > 4)
            throw new ContentValidationException(
                $"{station.StationId}.Tier must be in range 1..4.");
        RequireFiniteNonNegative(
            station.EnergyCapacity,
            $"{station.StationId}.EnergyCapacity");
        if (station.ParallelSlots <= 0)
            throw new ContentValidationException(
                $"{station.StationId}.ParallelSlots must be positive.");
        if (station.SupportedCategories.Count == 0)
            throw new ContentValidationException(
                $"{station.StationId} has no supported categories.");
        foreach (string category in station.SupportedCategories)
            RequireText(category, $"{station.StationId}.SupportedCategories");
        ValidateTags(station.Tags, station.StationId);
    }

    private static void ValidateTechnology(
        TechnologyDefinition technology,
        IReadOnlyDictionary<string, TechnologyDefinition> technologies)
    {
        ValidateStableId(technology.TechnologyId, "TechnologyId");
        ValidateStableId(technology.LocalizationKey, "LocalizationKey");
        ValidateTier(technology.Tier, $"{technology.TechnologyId}.Tier");
        if (technology.ResearchCost < 0)
            throw new ContentValidationException(
                $"{technology.TechnologyId}.ResearchCost must be non-negative.");
        foreach (string prerequisite in technology.Prerequisites)
        {
            ValidateStableId(
                prerequisite,
                $"{technology.TechnologyId}.Prerequisite");
            if (!technologies.ContainsKey(prerequisite))
                throw new ContentValidationException(
                    $"Technology {technology.TechnologyId} references missing " +
                    $"prerequisite {prerequisite}.");
            if (string.Equals(
                    prerequisite,
                    technology.TechnologyId,
                    StringComparison.Ordinal))
                throw new ContentValidationException(
                    $"Technology {technology.TechnologyId} depends on itself.");
        }
        ValidateTags(technology.Tags, technology.TechnologyId);
    }

    private static void ValidateTechnologyGraph(
        IReadOnlyDictionary<string, TechnologyDefinition> technologies)
    {
        Dictionary<string, int> colors = technologies.Keys.ToDictionary(
            id => id,
            _ => 0,
            StringComparer.Ordinal);
        bool Visit(string technologyId)
        {
            colors[technologyId] = 1;
            foreach (string prerequisite in technologies[technologyId].Prerequisites)
            {
                if (colors[prerequisite] == 1)
                    return true;
                if (colors[prerequisite] == 0 && Visit(prerequisite))
                    return true;
            }
            colors[technologyId] = 2;
            return false;
        }
        if (technologies.Keys.Any(id => colors[id] == 0 && Visit(id)))
            throw new ContentValidationException(
                "Technology graph contains a dependency cycle.");
    }

    private static void ValidateRecipe(
        CraftingRecipeDefinition recipe,
        IReadOnlyDictionary<string, GameItemDefinition> items,
        IReadOnlyDictionary<string, CraftingStationDefinition> stations,
        IReadOnlyDictionary<string, TechnologyDefinition> technologies,
        bool validateIndustryReferences)
    {
        ValidateStableId(recipe.RecipeId, "RecipeId");
        RequireText(recipe.Category, $"{recipe.RecipeId}.Category");
        ValidateTier(recipe.TechnologyTier, $"{recipe.RecipeId}.TechnologyTier");
        if (recipe.Inputs.Count == 0)
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} has no inputs.");
        if (recipe.Outputs.Count == 0)
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} has no outputs.");

        ValidateStacks(recipe.Inputs, items, recipe.RecipeId, "input");
        ValidateStacks(recipe.Outputs, items, recipe.RecipeId, "output");
        ValidateStacks(recipe.Byproducts, items, recipe.RecipeId, "byproduct");
        ValidateStacks(
            recipe.DismantleReturns,
            items,
            recipe.RecipeId,
            "dismantleReturn");
        ValidateCatalysts(recipe.Catalysts, items, recipe.RecipeId);
        ValidateItemReferences(
            recipe.RequiredTools,
            items,
            recipe.RecipeId,
            "requiredTool");

        if (!string.IsNullOrEmpty(recipe.RequiredTechnology))
            ValidateStableId(
                recipe.RequiredTechnology,
                $"{recipe.RecipeId}.RequiredTechnology");
        ValidateStableId(
            recipe.RequiredStation,
            $"{recipe.RecipeId}.RequiredStation");
        if (recipe.StationTier < 1 || recipe.StationTier > 4)
            throw new ContentValidationException(
                $"{recipe.RecipeId}.StationTier must be in range 1..4.");
        RequireFiniteNonNegative(
            recipe.CraftTimeSeconds,
            $"{recipe.RecipeId}.CraftTimeSeconds");
        RequireFiniteNonNegative(
            recipe.EnergyCost,
            $"{recipe.RecipeId}.EnergyCost");
        if (recipe.BatchSize <= 0)
            throw new ContentValidationException(
                $"{recipe.RecipeId}.BatchSize must be positive.");
        ValidateEnvironment(recipe.RecipeId, recipe.Environment);
        if (recipe.Quality.Minimum < 0 ||
            recipe.Quality.Maximum > 100 ||
            recipe.Quality.Maximum < recipe.Quality.Minimum)
        {
            throw new ContentValidationException(
                $"{recipe.RecipeId}.Quality must be an ordered range in 0..100.");
        }
        ValidateTags(recipe.Hazards, recipe.RecipeId + ".Hazards");
        ValidateTags(recipe.Tags, recipe.RecipeId);

        bool repairsShip = string.Equals(
            recipe.Application.Type,
            "RepairShip",
            StringComparison.Ordinal);
        bool storesOutputs = string.Equals(
            recipe.Application.Type,
            "StoreOutputs",
            StringComparison.Ordinal);
        if (!repairsShip && !storesOutputs)
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} uses unsupported application " +
                $"{recipe.Application.Type}.");
        ValidateStableId(
            recipe.Application.TargetId,
            $"{recipe.RecipeId}.Application.TargetId");
        if (!double.IsFinite(recipe.Application.ResultHealth) ||
            (repairsShip && recipe.Application.ResultHealth <= 0.0) ||
            (storesOutputs && recipe.Application.ResultHealth != 0.0))
        {
            throw new ContentValidationException(
                repairsShip
                    ? $"{recipe.RecipeId}.Application.ResultHealth must be positive."
                    : $"{recipe.RecipeId}.Application.ResultHealth must be 0 for StoreOutputs.");
        }

        if (!validateIndustryReferences)
            return;
        if (!stations.TryGetValue(
                recipe.RequiredStation,
                out CraftingStationDefinition? station) ||
            station is null)
        {
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} references missing station " +
                $"{recipe.RequiredStation}.");
        }
        if (recipe.StationTier > station.Tier)
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} requires station tier " +
                $"{recipe.StationTier}, but {station.StationId} is tier " +
                $"{station.Tier}.");
        if (!station.SupportedCategories.Contains(
                recipe.Category,
                StringComparer.Ordinal))
            throw new ContentValidationException(
                $"Station {station.StationId} does not support recipe category " +
                $"{recipe.Category} ({recipe.RecipeId}).");
        if (!string.IsNullOrEmpty(recipe.RequiredTechnology))
        {
            if (!technologies.TryGetValue(
                    recipe.RequiredTechnology,
                    out TechnologyDefinition? technology) ||
                technology is null)
            {
                throw new ContentValidationException(
                    $"Recipe {recipe.RecipeId} references missing technology " +
                    $"{recipe.RequiredTechnology}.");
            }
            if (recipe.TechnologyTier < technology.Tier)
                throw new ContentValidationException(
                    $"Recipe {recipe.RecipeId} tier {recipe.TechnologyTier} " +
                    $"is below technology {technology.TechnologyId} tier " +
                    $"{technology.Tier}.");
        }
    }

    private static void ValidateEnvironment(
        string recipeId,
        RecipeEnvironmentDefinition environment)
    {
        if (!double.IsFinite(environment.MinimumTemperatureKelvin) ||
            !double.IsFinite(environment.MaximumTemperatureKelvin) ||
            environment.MinimumTemperatureKelvin < 0.0 ||
            environment.MaximumTemperatureKelvin <
                environment.MinimumTemperatureKelvin)
        {
            throw new ContentValidationException(
                $"{recipeId}.Environment temperature range is invalid.");
        }
        if (!double.IsFinite(environment.MinimumPressureKPa) ||
            !double.IsFinite(environment.MaximumPressureKPa) ||
            environment.MinimumPressureKPa < 0.0 ||
            environment.MaximumPressureKPa < environment.MinimumPressureKPa)
        {
            throw new ContentValidationException(
                $"{recipeId}.Environment pressure range is invalid.");
        }
    }

    private static void ValidateCatalysts(
        IReadOnlyList<CatalystStackDefinition> catalysts,
        IReadOnlyDictionary<string, GameItemDefinition> items,
        string recipeId)
    {
        HashSet<string> definitions = new(StringComparer.Ordinal);
        foreach (CatalystStackDefinition catalyst in catalysts)
        {
            ValidateStableId(
                catalyst.DefinitionId,
                $"{recipeId}.catalyst.DefinitionId");
            if (!items.ContainsKey(catalyst.DefinitionId))
                throw new ContentValidationException(
                    $"Recipe {recipeId} catalyst references missing item " +
                    $"{catalyst.DefinitionId}.");
            if (catalyst.Quantity <= 0)
                throw new ContentValidationException(
                    $"Recipe {recipeId} catalyst {catalyst.DefinitionId} " +
                    "must have a positive quantity.");
            if (!double.IsFinite(catalyst.ConsumptionChance) ||
                catalyst.ConsumptionChance < 0.0 ||
                catalyst.ConsumptionChance > 1.0)
            {
                throw new ContentValidationException(
                    $"Recipe {recipeId} catalyst {catalyst.DefinitionId} " +
                    "consumption chance must be in range 0..1.");
            }
            if (!definitions.Add(catalyst.DefinitionId))
                throw new ContentValidationException(
                    $"Recipe {recipeId} repeats catalyst " +
                    $"{catalyst.DefinitionId}.");
        }
    }

    private static void ValidateStacks(
        IReadOnlyList<CraftingStackDefinition> stacks,
        IReadOnlyDictionary<string, GameItemDefinition> items,
        string recipeId,
        string role)
    {
        HashSet<string> definitions = new(StringComparer.Ordinal);
        foreach (CraftingStackDefinition stack in stacks)
        {
            ValidateStableId(
                stack.DefinitionId,
                $"{recipeId}.{role}.DefinitionId");
            if (!items.ContainsKey(stack.DefinitionId))
                throw new ContentValidationException(
                    $"Recipe {recipeId} {role} references missing item " +
                    $"{stack.DefinitionId}.");
            if (stack.Quantity <= 0)
                throw new ContentValidationException(
                    $"Recipe {recipeId} {role} {stack.DefinitionId} " +
                    "must have a positive quantity.");
            if (!definitions.Add(stack.DefinitionId))
                throw new ContentValidationException(
                    $"Recipe {recipeId} repeats {role} " +
                    $"{stack.DefinitionId}.");
        }
    }

    private static void ValidateItemReferences(
        IReadOnlyList<string> definitionIds,
        IReadOnlyDictionary<string, GameItemDefinition> items,
        string ownerId,
        string role)
    {
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string definitionId in definitionIds)
        {
            ValidateStableId(definitionId, $"{ownerId}.{role}");
            if (!items.ContainsKey(definitionId))
                throw new ContentValidationException(
                    $"{ownerId} {role} references missing item {definitionId}.");
            if (!unique.Add(definitionId))
                throw new ContentValidationException(
                    $"{ownerId} repeats {role} {definitionId}.");
        }
    }

    private static void ValidateStableId(string value, string field)
    {
        if (!IsStableId(value))
            throw new ContentValidationException(
                $"{field} value '{value}' is not a stable dotted string ID.");
    }

    private static void ValidateTier(int value, string field)
    {
        if (value < 0 || value > 4)
            throw new ContentValidationException(
                $"{field} must be in range 0..4.");
    }

    private static void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ContentValidationException(
                $"{field} must not be empty.");
    }

    private static void RequireFiniteNonNegative(double value, string field)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ContentValidationException(
                $"{field} must be a finite non-negative number.");
    }

    private static void ValidateColor(
        double value,
        string definitionId,
        string field)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
            throw new ContentValidationException(
                $"{definitionId}.{field} must be in range 0..1.");
    }

    private static void ValidateTags(
        IReadOnlyList<string> tags,
        string definitionId)
    {
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || !TagPattern.IsMatch(tag))
                throw new ContentValidationException(
                    $"{definitionId} has invalid tag '{tag}'.");
            if (!unique.Add(tag))
                throw new ContentValidationException(
                    $"{definitionId} repeats tag '{tag}'.");
        }
    }

    private sealed class ItemCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public ItemDefinitionDocument[]? Definitions { get; set; }
    }

    private sealed class ItemDefinitionDocument
    {
        public string? DefinitionId { get; set; }
        public string? LocalizationKey { get; set; }
        public string? Category { get; set; }
        public int MaxStack { get; set; }
        public double BasePrice { get; set; }
        public double Mass { get; set; }
        public string? Rarity { get; set; }
        public int TechnologyTier { get; set; }
        public string? StateOfMatter { get; set; }
        public string? Icon { get; set; }
        public string? WorldModel { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class ResourceCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public ResourceDefinitionDocument[]? Definitions { get; set; }
    }

    private sealed class ResourceDefinitionDocument
    {
        public string? ResourceId { get; set; }
        public string? ItemDefinitionId { get; set; }
        public int MinimumYield { get; set; }
        public int MaximumYield { get; set; }
        public int ScanTier { get; set; }
        public string? ExtractionMethod { get; set; }
        public ResourceVisualDocument? Visual { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class ResourceVisualDocument
    {
        public double AlbedoR { get; set; }
        public double AlbedoG { get; set; }
        public double AlbedoB { get; set; }
        public double EmissionR { get; set; }
        public double EmissionG { get; set; }
        public double EmissionB { get; set; }
        public double EmissionEnergy { get; set; }
        public double Metallic { get; set; }
        public double Roughness { get; set; }
    }

    private sealed class RecipeCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public RecipeDefinitionDocument[]? Definitions { get; set; }
    }

    private sealed class RecipeDefinitionDocument
    {
        public string? RecipeId { get; set; }
        public string? Category { get; set; }
        public int TechnologyTier { get; set; }
        public CraftingStackDocument[]? Inputs { get; set; }
        public CatalystStackDocument[]? Catalysts { get; set; }
        public CraftingStackDocument[]? Outputs { get; set; }
        public CraftingStackDocument[]? Byproducts { get; set; }
        public CraftingStackDocument[]? DismantleReturns { get; set; }
        public string? RequiredTechnology { get; set; }
        public string? RequiredStation { get; set; }
        public int StationTier { get; set; }
        public double CraftTimeSeconds { get; set; }
        public double EnergyCost { get; set; }
        public int BatchSize { get; set; }
        public bool RuntimeEnabled { get; set; }
        public string[]? RequiredTools { get; set; }
        public RecipeEnvironmentDocument? Environment { get; set; }
        public RecipeQualityDocument? Quality { get; set; }
        public string[]? Hazards { get; set; }
        public RecipeApplicationDocument? Application { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class CraftingStackDocument
    {
        public string? DefinitionId { get; set; }
        public int Quantity { get; set; }
    }

    private sealed class CatalystStackDocument
    {
        public string? DefinitionId { get; set; }
        public int Quantity { get; set; }
        public double ConsumptionChance { get; set; }
    }

    private sealed class RecipeEnvironmentDocument
    {
        public double MinimumTemperatureKelvin { get; set; }
        public double MaximumTemperatureKelvin { get; set; }
        public double MinimumPressureKPa { get; set; }
        public double MaximumPressureKPa { get; set; }
        public bool RequiresVacuum { get; set; }
    }

    private sealed class RecipeQualityDocument
    {
        public int Minimum { get; set; }
        public int Maximum { get; set; }
    }

    private sealed class RecipeApplicationDocument
    {
        public string? Type { get; set; }
        public string? TargetId { get; set; }
        public double ResultHealth { get; set; }
    }

    private sealed class StationCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public StationDefinitionDocument[]? Definitions { get; set; }
    }

    private sealed class StationDefinitionDocument
    {
        public string? StationId { get; set; }
        public string? LocalizationKey { get; set; }
        public int Tier { get; set; }
        public double EnergyCapacity { get; set; }
        public int ParallelSlots { get; set; }
        public string[]? SupportedCategories { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class TechnologyCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public TechnologyDefinitionDocument[]? Definitions { get; set; }
    }

    private sealed class TechnologyDefinitionDocument
    {
        public string? TechnologyId { get; set; }
        public string? LocalizationKey { get; set; }
        public int Tier { get; set; }
        public int ResearchCost { get; set; }
        public string[]? Prerequisites { get; set; }
        public string[]? Tags { get; set; }
    }
}

public sealed record IndustryCatalogAcceptanceReport(
    bool Passed,
    string Result,
    IndustryCatalogAnalysis Analysis,
    double ElapsedMilliseconds);

public static class IndustryCatalogAcceptanceRunner
{
    public const int ExpectedRecipes = 128;
    public const int MinimumItems = 170;
    public const int ExpectedWorldResources = 42;
    public const int ExpectedStations = 15;
    public const int ExpectedTechnologies = 32;
    public const int MinimumChemistryRecipes = 30;
    public const int MinimumCompotiumRecipes = 12;
    public const int MinimumParaffiniumRecipes = 5;
    public const int ExpectedRuntimeEnabledRecipes = 10;

    public static IndustryCatalogAcceptanceReport Run(
        GameContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Stopwatch stopwatch = Stopwatch.StartNew();
        IndustryCatalogAnalysis analysis = catalog.AnalyzeIndustry();
        bool passed =
            catalog.SchemaVersion == GameContentCatalog.CurrentSchemaVersion &&
            analysis.RecipeCount == ExpectedRecipes &&
            analysis.ItemCount >= MinimumItems &&
            analysis.ResourceCount == ExpectedWorldResources &&
            analysis.StationCount == ExpectedStations &&
            analysis.TechnologyCount == ExpectedTechnologies &&
            analysis.ChemistryRecipes >= MinimumChemistryRecipes &&
            analysis.CompotiumRecipes >= MinimumCompotiumRecipes &&
            analysis.ParaffiniumRecipes >= MinimumParaffiniumRecipes &&
            analysis.RuntimeEnabledRecipes == ExpectedRuntimeEnabledRecipes &&
            analysis.DependencyCycles == 0 &&
            analysis.UnreachableRecipes == 0;
        stopwatch.Stop();
        string result = passed
            ? "the complete 128-recipe industry catalog, technology graph and Compotium chemistry line are structurally valid and fully reachable"
            : "industry catalog criteria failed";
        return new IndustryCatalogAcceptanceReport(
            passed,
            result,
            analysis,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}

public sealed record DataDrivenContentAcceptanceReport(
    bool Passed,
    string Result,
    int SchemaVersion,
    int ItemCount,
    int ResourceCount,
    int RecipeCount,
    string RecipeId,
    int ActualRequiredQuantity,
    int VariantRequiredQuantity,
    bool BlockedBelowVariantThreshold,
    bool RepairedAtVariantThreshold,
    int ProducedOutputQuantity,
    bool DuplicateIdRejected,
    bool MissingReferenceRejected,
    bool StableIdsValidated,
    double ElapsedMilliseconds);

public static class DataDrivenContentAcceptanceRunner
{
    public static DataDrivenContentAcceptanceReport Run(
        GameContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            CraftingRecipeDefinition actual = catalog.GetRecipe(
                StarterRepairContentIds.RecipeId);
            CraftingStackDefinition primaryInput = actual.Inputs.Single();
            int variantQuantity = primaryInput.Quantity + 1;
            CraftingRecipeDefinition variant = actual with
            {
                Inputs = new[]
                {
                    primaryInput with { Quantity = variantQuantity }
                }
            };

            StarterRepairSession session = new(variant);
            for (int index = 0; index < variantQuantity - 1; index++)
            {
                bool collected = session.TryCollect(
                    $"acceptance.node_{index + 1}",
                    primaryInput.DefinitionId,
                    1,
                    out string collectResult);
                if (!collected)
                {
                    throw new InvalidOperationException(collectResult);
                }
            }

            StarterRepairResult blocked = session.TryRepair(out _);
            bool blockedBelowThreshold =
                blocked == StarterRepairResult.InsufficientSalvage;
            bool finalCollected = session.TryCollect(
                $"acceptance.node_{variantQuantity}",
                primaryInput.DefinitionId,
                1,
                out string finalCollectResult);
            if (!finalCollected)
            {
                throw new InvalidOperationException(finalCollectResult);
            }

            StarterRepairResult repaired = session.TryRepair(out _);
            bool repairedAtThreshold =
                repaired == StarterRepairResult.Repaired &&
                session.ShipRepaired;
            int producedQuantity = session.LastCraftedOutputs.Sum(
                output => output.Quantity);

            GameItemDefinition firstItem = catalog.Items.Values.First();
            bool duplicateRejected = RejectsContent(() =>
                GameContentCatalog.Create(
                    catalog.SchemaVersion,
                    catalog.Items.Values.Concat(new[] { firstItem }),
                    catalog.Resources.Values,
                    catalog.Recipes.Values));

            CraftingRecipeDefinition missingReferenceRecipe = actual with
            {
                RecipeId = "recipe.acceptance.missing_reference",
                Inputs = new[]
                {
                    new CraftingStackDefinition(
                        "resource.acceptance_missing",
                        1)
                }
            };
            bool missingReferenceRejected = RejectsContent(() =>
                GameContentCatalog.Create(
                    catalog.SchemaVersion,
                    catalog.Items.Values,
                    catalog.Resources.Values,
                    catalog.Recipes.Values.Concat(
                        new[] { missingReferenceRecipe })));

            bool stableIds = catalog.Items.Keys.All(GameContentCatalog.IsStableId) &&
                catalog.Resources.Keys.All(GameContentCatalog.IsStableId) &&
                catalog.Recipes.Keys.All(GameContentCatalog.IsStableId);
            bool passed =
                catalog.SchemaVersion == GameContentCatalog.CurrentSchemaVersion &&
                catalog.Items.Count >= 2 &&
                catalog.Resources.Count >= 1 &&
                catalog.Recipes.Count >= 1 &&
                primaryInput.Quantity == 3 &&
                blockedBelowThreshold &&
                repairedAtThreshold &&
                producedQuantity == actual.Outputs.Sum(output => output.Quantity) &&
                duplicateRejected &&
                missingReferenceRejected &&
                stableIds;

            stopwatch.Stop();
            List<string> failures = new();
            if (primaryInput.Quantity != 3)
            {
                failures.Add($"actualRequired={primaryInput.Quantity}");
            }

            if (!blockedBelowThreshold)
            {
                failures.Add("blockedBelowVariant=0");
            }

            if (!repairedAtThreshold)
            {
                failures.Add("repairedAtVariant=0");
            }

            if (producedQuantity != actual.Outputs.Sum(output => output.Quantity))
            {
                failures.Add($"outputs={producedQuantity}");
            }

            if (!duplicateRejected)
            {
                failures.Add("duplicateRejected=0");
            }

            if (!missingReferenceRejected)
            {
                failures.Add("missingReferenceRejected=0");
            }

            if (!stableIds)
            {
                failures.Add("stableIds=0");
            }

            return new DataDrivenContentAcceptanceReport(
                passed,
                passed
                    ? "JSON catalog validated; recipe threshold changed in memory and domain behavior followed the data"
                    : $"content criteria failed: {string.Join(", ", failures)}",
                catalog.SchemaVersion,
                catalog.Items.Count,
                catalog.Resources.Count,
                catalog.Recipes.Count,
                actual.RecipeId,
                primaryInput.Quantity,
                variantQuantity,
                blockedBelowThreshold,
                repairedAtThreshold,
                producedQuantity,
                duplicateRejected,
                missingReferenceRejected,
                stableIds,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new DataDrivenContentAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                catalog.SchemaVersion,
                catalog.Items.Count,
                catalog.Resources.Count,
                catalog.Recipes.Count,
                StarterRepairContentIds.RecipeId,
                0,
                0,
                false,
                false,
                0,
                false,
                false,
                false,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static bool RejectsContent(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ContentValidationException)
        {
            return true;
        }
    }
}
