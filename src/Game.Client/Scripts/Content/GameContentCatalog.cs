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

public sealed record RecipeApplicationDefinition(
    string Type,
    string TargetId,
    double ResultHealth);

public sealed record CraftingRecipeDefinition(
    string RecipeId,
    IReadOnlyList<CraftingStackDefinition> Inputs,
    IReadOnlyList<CraftingStackDefinition> Outputs,
    string RequiredTechnology,
    string RequiredStation,
    double CraftTimeSeconds,
    RecipeApplicationDefinition Application,
    IReadOnlyList<string> Tags);

public sealed class GameContentCatalog
{
    public const int CurrentSchemaVersion = 1;

    private static readonly Regex StableIdPattern = new(
        "^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$",
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

    private GameContentCatalog(
        int schemaVersion,
        Dictionary<string, GameItemDefinition> items,
        Dictionary<string, GameResourceDefinition> resources,
        Dictionary<string, CraftingRecipeDefinition> recipes)
    {
        SchemaVersion = schemaVersion;
        _items = items;
        _resources = resources;
        _recipes = recipes;
    }

    public int SchemaVersion { get; }

    public IReadOnlyDictionary<string, GameItemDefinition> Items => _items;

    public IReadOnlyDictionary<string, GameResourceDefinition> Resources =>
        _resources;

    public IReadOnlyDictionary<string, CraftingRecipeDefinition> Recipes =>
        _recipes;

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

        if (itemsDocument.SchemaVersion != resourcesDocument.SchemaVersion ||
            itemsDocument.SchemaVersion != recipesDocument.SchemaVersion)
        {
            throw new ContentValidationException(
                "Content JSON schema versions do not match: " +
                $"items={itemsDocument.SchemaVersion}, " +
                $"resources={resourcesDocument.SchemaVersion}, " +
                $"recipes={recipesDocument.SchemaVersion}.");
        }

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

    public static GameContentCatalog Create(
        int schemaVersion,
        IEnumerable<GameItemDefinition> items,
        IEnumerable<GameResourceDefinition> resources,
        IEnumerable<CraftingRecipeDefinition> recipes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(recipes);
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

        if (itemMap.Count == 0)
        {
            throw new ContentValidationException(
                "items.json must contain at least one definition.");
        }

        if (resourceMap.Count == 0)
        {
            throw new ContentValidationException(
                "resources.json must contain at least one definition.");
        }

        if (recipeMap.Count == 0)
        {
            throw new ContentValidationException(
                "recipes.json must contain at least one definition.");
        }

        foreach (GameItemDefinition item in itemMap.Values)
        {
            ValidateItem(item);
        }

        foreach (GameResourceDefinition resource in resourceMap.Values)
        {
            ValidateResource(resource, itemMap);
        }

        foreach (CraftingRecipeDefinition recipe in recipeMap.Values)
        {
            ValidateRecipe(recipe, itemMap);
        }

        return new GameContentCatalog(
            schemaVersion,
            itemMap,
            resourceMap,
            recipeMap);
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

    public static bool IsStableId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            StableIdPattern.IsMatch(value);
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
        return new CraftingRecipeDefinition(
            document.RecipeId ?? string.Empty,
            MapStacks(document.Inputs),
            MapStacks(document.Outputs),
            document.RequiredTechnology ?? string.Empty,
            document.RequiredStation ?? string.Empty,
            document.CraftTimeSeconds,
            new RecipeApplicationDefinition(
                application.Type ?? string.Empty,
                application.TargetId ?? string.Empty,
                application.ResultHealth),
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
        if (item.MaxStack <= 0)
        {
            throw new ContentValidationException(
                $"{item.DefinitionId}.MaxStack must be positive.");
        }

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

    private static void ValidateRecipe(
        CraftingRecipeDefinition recipe,
        IReadOnlyDictionary<string, GameItemDefinition> items)
    {
        ValidateStableId(recipe.RecipeId, "RecipeId");
        if (recipe.Inputs.Count == 0)
        {
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} has no inputs.");
        }

        if (recipe.Outputs.Count == 0)
        {
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} has no outputs.");
        }

        ValidateStacks(recipe.Inputs, items, recipe.RecipeId, "input");
        ValidateStacks(recipe.Outputs, items, recipe.RecipeId, "output");
        if (!string.IsNullOrEmpty(recipe.RequiredTechnology))
        {
            ValidateStableId(
                recipe.RequiredTechnology,
                $"{recipe.RecipeId}.RequiredTechnology");
        }

        ValidateStableId(
            recipe.RequiredStation,
            $"{recipe.RecipeId}.RequiredStation");
        RequireFiniteNonNegative(
            recipe.CraftTimeSeconds,
            $"{recipe.RecipeId}.CraftTimeSeconds");
        bool repairsShip = string.Equals(
            recipe.Application.Type,
            "RepairShip",
            StringComparison.Ordinal);
        bool storesOutputs = string.Equals(
            recipe.Application.Type,
            "StoreOutputs",
            StringComparison.Ordinal);
        if (!repairsShip && !storesOutputs)
        {
            throw new ContentValidationException(
                $"Recipe {recipe.RecipeId} uses unsupported application " +
                $"{recipe.Application.Type}.");
        }

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

        ValidateTags(recipe.Tags, recipe.RecipeId);
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
            {
                throw new ContentValidationException(
                    $"Recipe {recipeId} {role} references missing item " +
                    $"{stack.DefinitionId}.");
            }

            if (stack.Quantity <= 0)
            {
                throw new ContentValidationException(
                    $"Recipe {recipeId} {role} {stack.DefinitionId} " +
                    "must have a positive quantity.");
            }

            if (!definitions.Add(stack.DefinitionId))
            {
                throw new ContentValidationException(
                    $"Recipe {recipeId} repeats {role} " +
                    $"{stack.DefinitionId}.");
            }
        }
    }

    private static void ValidateStableId(string value, string field)
    {
        if (!IsStableId(value))
        {
            throw new ContentValidationException(
                $"{field} value '{value}' is not a stable dotted string ID.");
        }
    }

    private static void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ContentValidationException(
                $"{field} must not be empty.");
        }
    }

    private static void RequireFiniteNonNegative(double value, string field)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ContentValidationException(
                $"{field} must be a finite non-negative number.");
        }
    }

    private static void ValidateColor(
        double value,
        string definitionId,
        string field)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new ContentValidationException(
                $"{definitionId}.{field} must be in range 0..1.");
        }
    }

    private static void ValidateTags(
        IReadOnlyList<string> tags,
        string definitionId)
    {
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) ||
                !Regex.IsMatch(tag, "^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant))
            {
                throw new ContentValidationException(
                    $"{definitionId} has invalid tag '{tag}'.");
            }

            if (!unique.Add(tag))
            {
                throw new ContentValidationException(
                    $"{definitionId} repeats tag '{tag}'.");
            }
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

        public CraftingStackDocument[]? Inputs { get; set; }

        public CraftingStackDocument[]? Outputs { get; set; }

        public string? RequiredTechnology { get; set; }

        public string? RequiredStation { get; set; }

        public double CraftTimeSeconds { get; set; }

        public RecipeApplicationDocument? Application { get; set; }

        public string[]? Tags { get; set; }
    }

    private sealed class CraftingStackDocument
    {
        public string? DefinitionId { get; set; }

        public int Quantity { get; set; }
    }

    private sealed class RecipeApplicationDocument
    {
        public string? Type { get; set; }

        public string? TargetId { get; set; }

        public double ResultHealth { get; set; }
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
