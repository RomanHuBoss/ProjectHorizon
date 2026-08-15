using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public sealed record EcologyBiomeDefinition(
    string BiomeId,
    string LocalizationKey,
    double TemperatureMin,
    double TemperatureMax,
    double MoistureMin,
    double MoistureMax,
    bool SupportsWater);

public sealed record EcologyFloraDefinition(
    string FloraId,
    string LocalizationKey,
    string Shape,
    IReadOnlyList<string> BiomeIds,
    double Density,
    double MinimumSpacing,
    double ScaleMin,
    double ScaleMax,
    double ColorR,
    double ColorG,
    double ColorB,
    string HarvestDefinitionId,
    int ScanPoints,
    string Hazard);

public sealed record EcologyFaunaDefinition(
    string FaunaId,
    string LocalizationKey,
    string MovementMode,
    string BodyPlan,
    string Diet,
    string Activity,
    double Aggression,
    double Speed,
    int GroupMin,
    int GroupMax,
    double Health,
    double TerritoryRadius,
    IReadOnlyList<string> BiomeIds,
    IReadOnlyList<string> Behaviors,
    double ColorR,
    double ColorG,
    double ColorB,
    double Scale,
    int ScanPoints);

public sealed record EcologyCatalogDocument(
    int SchemaVersion,
    long WorldSeed,
    string RegionKey,
    int ActiveFaunaLimit,
    int SimplifiedFaunaLimit,
    IReadOnlyList<EcologyBiomeDefinition> Biomes,
    IReadOnlyList<EcologyFloraDefinition> Flora,
    IReadOnlyList<EcologyFaunaDefinition> Fauna);

public sealed class EcologyCatalog
{
    public const int ExpectedBiomeCount = 16;
    public const int ExpectedFloraCount = 60;
    public const int ExpectedFaunaCount = 20;
    public const int ExpectedGroundFaunaCount = 12;
    public const int ExpectedFlyingFaunaCount = 4;
    public const int ExpectedAquaticFaunaCount = 4;
    public const int ExpectedActiveFaunaLimit = 20;
    public const int ExpectedSimplifiedFaunaLimit = 80;

    private static readonly HashSet<string> AllowedShapes = new(
        new[] { "Spire", "Canopy", "Tuft", "Frond", "Pad", "Fungus" },
        StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedMovementModes = new(
        new[] { "Ground", "Flying", "Aquatic" },
        StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedBodyPlans = new(
        new[] { "Biped", "Quadruped", "Hexapod", "Flying", "Aquatic", "Crawler" },
        StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedBehaviors = new(
        new[]
        {
            "Idle", "Wander", "Graze", "Drink", "Sleep", "Investigate",
            "Flee", "Threaten", "Attack", "ReturnToTerritory", "FollowGroup"
        },
        StringComparer.Ordinal);

    private EcologyCatalog(EcologyCatalogDocument document)
    {
        SchemaVersion = document.SchemaVersion;
        WorldSeed = document.WorldSeed;
        RegionKey = document.RegionKey;
        ActiveFaunaLimit = document.ActiveFaunaLimit;
        SimplifiedFaunaLimit = document.SimplifiedFaunaLimit;
        Biomes = document.Biomes.ToDictionary(
            biome => biome.BiomeId,
            StringComparer.Ordinal);
        Flora = document.Flora.ToDictionary(
            flora => flora.FloraId,
            StringComparer.Ordinal);
        Fauna = document.Fauna.ToDictionary(
            fauna => fauna.FaunaId,
            StringComparer.Ordinal);
    }

    public int SchemaVersion { get; }

    public long WorldSeed { get; }

    public string RegionKey { get; }

    public int ActiveFaunaLimit { get; }

    public int SimplifiedFaunaLimit { get; }

    public IReadOnlyDictionary<string, EcologyBiomeDefinition> Biomes { get; }

    public IReadOnlyDictionary<string, EcologyFloraDefinition> Flora { get; }

    public IReadOnlyDictionary<string, EcologyFaunaDefinition> Fauna { get; }

    public static EcologyCatalog LoadFromJson(
        string json,
        GameContentCatalog contentCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        EcologyCatalogDocument document =
            JsonSerializer.Deserialize<EcologyCatalogDocument>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ??
            throw new InvalidOperationException(
                "Ecology catalog deserialized to null.");

        ValidateDocument(document, contentCatalog);
        return new EcologyCatalog(document);
    }

    public EcologyBiomeDefinition GetBiome(string biomeId)
    {
        return Biomes.TryGetValue(
            biomeId,
            out EcologyBiomeDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown ecology biome {biomeId}.");
    }

    public EcologyFloraDefinition GetFlora(string floraId)
    {
        return Flora.TryGetValue(
            floraId,
            out EcologyFloraDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown flora definition {floraId}.");
    }

    public EcologyFaunaDefinition GetFauna(string faunaId)
    {
        return Fauna.TryGetValue(
            faunaId,
            out EcologyFaunaDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown fauna definition {faunaId}.");
    }

    private static void ValidateDocument(
        EcologyCatalogDocument document,
        GameContentCatalog contentCatalog)
    {
        if (document.SchemaVersion != 1 ||
            document.WorldSeed <= 0 ||
            !GameContentCatalog.IsStableId(document.RegionKey) ||
            document.ActiveFaunaLimit != ExpectedActiveFaunaLimit ||
            document.SimplifiedFaunaLimit != ExpectedSimplifiedFaunaLimit ||
            document.Biomes is null ||
            document.Flora is null ||
            document.Fauna is null ||
            document.Biomes.Count != ExpectedBiomeCount ||
            document.Flora.Count != ExpectedFloraCount ||
            document.Fauna.Count != ExpectedFaunaCount)
        {
            throw new InvalidOperationException(
                "Ecology catalog header or baseline counts are invalid.");
        }

        HashSet<string> biomeIds = new(StringComparer.Ordinal);
        foreach (EcologyBiomeDefinition biome in document.Biomes)
        {
            if (!GameContentCatalog.IsStableId(biome.BiomeId) ||
                !biome.BiomeId.StartsWith("biome.", StringComparison.Ordinal) ||
                !biomeIds.Add(biome.BiomeId) ||
                !GameContentCatalog.IsStableId(biome.LocalizationKey) ||
                !double.IsFinite(biome.TemperatureMin) ||
                !double.IsFinite(biome.TemperatureMax) ||
                biome.TemperatureMin >= biome.TemperatureMax ||
                biome.MoistureMin < 0.0 ||
                biome.MoistureMax > 1.0 ||
                biome.MoistureMin > biome.MoistureMax)
            {
                throw new InvalidOperationException(
                    $"Invalid ecology biome {biome.BiomeId}.");
            }
        }

        HashSet<string> floraIds = new(StringComparer.Ordinal);
        foreach (EcologyFloraDefinition flora in document.Flora)
        {
            if (!GameContentCatalog.IsStableId(flora.FloraId) ||
                !flora.FloraId.StartsWith("flora.", StringComparison.Ordinal) ||
                !floraIds.Add(flora.FloraId) ||
                !GameContentCatalog.IsStableId(flora.LocalizationKey) ||
                !AllowedShapes.Contains(flora.Shape) ||
                flora.BiomeIds is null ||
                flora.BiomeIds.Count is < 1 or > 8 ||
                flora.BiomeIds.Distinct(StringComparer.Ordinal).Count() !=
                    flora.BiomeIds.Count ||
                flora.BiomeIds.Any(id => !biomeIds.Contains(id)) ||
                flora.Density <= 0.0 ||
                flora.Density > 1.0 ||
                flora.MinimumSpacing < 0.5 ||
                flora.ScaleMin <= 0.0 ||
                flora.ScaleMax < flora.ScaleMin ||
                flora.ScanPoints <= 0 ||
                !GameContentCatalog.IsStableId(flora.HarvestDefinitionId) ||
                !contentCatalog.Items.ContainsKey(flora.HarvestDefinitionId) ||
                !IsUnitColor(flora.ColorR, flora.ColorG, flora.ColorB))
            {
                throw new InvalidOperationException(
                    $"Invalid ecology flora definition {flora.FloraId}.");
            }
        }

        HashSet<string> faunaIds = new(StringComparer.Ordinal);
        HashSet<string> bodyPlans = new(StringComparer.Ordinal);
        HashSet<string> behaviorCoverage = new(StringComparer.Ordinal);
        foreach (EcologyFaunaDefinition fauna in document.Fauna)
        {
            if (!GameContentCatalog.IsStableId(fauna.FaunaId) ||
                !fauna.FaunaId.StartsWith("fauna.", StringComparison.Ordinal) ||
                !faunaIds.Add(fauna.FaunaId) ||
                !GameContentCatalog.IsStableId(fauna.LocalizationKey) ||
                !AllowedMovementModes.Contains(fauna.MovementMode) ||
                !AllowedBodyPlans.Contains(fauna.BodyPlan) ||
                fauna.BiomeIds is null ||
                fauna.BiomeIds.Count is < 1 or > 8 ||
                fauna.BiomeIds.Any(id => !biomeIds.Contains(id)) ||
                fauna.Behaviors is null ||
                fauna.Behaviors.Count < 4 ||
                fauna.Behaviors.Any(behavior =>
                    !AllowedBehaviors.Contains(behavior)) ||
                fauna.Aggression is < 0.0 or > 1.0 ||
                fauna.Speed <= 0.0 ||
                fauna.GroupMin <= 0 ||
                fauna.GroupMax < fauna.GroupMin ||
                fauna.GroupMax > 20 ||
                fauna.Health <= 0.0 ||
                fauna.TerritoryRadius <= 0.0 ||
                fauna.Scale <= 0.0 ||
                fauna.ScanPoints <= 0 ||
                !IsUnitColor(fauna.ColorR, fauna.ColorG, fauna.ColorB))
            {
                throw new InvalidOperationException(
                    $"Invalid ecology fauna definition {fauna.FaunaId}.");
            }

            bodyPlans.Add(fauna.BodyPlan);
            behaviorCoverage.UnionWith(fauna.Behaviors);
        }

        int ground = document.Fauna.Count(fauna =>
            string.Equals(
                fauna.MovementMode,
                "Ground",
                StringComparison.Ordinal));
        int flying = document.Fauna.Count(fauna =>
            string.Equals(
                fauna.MovementMode,
                "Flying",
                StringComparison.Ordinal));
        int aquatic = document.Fauna.Count(fauna =>
            string.Equals(
                fauna.MovementMode,
                "Aquatic",
                StringComparison.Ordinal));
        if (ground != ExpectedGroundFaunaCount ||
            flying != ExpectedFlyingFaunaCount ||
            aquatic != ExpectedAquaticFaunaCount ||
            bodyPlans.Count != AllowedBodyPlans.Count ||
            !AllowedBehaviors.SetEquals(behaviorCoverage))
        {
            throw new InvalidOperationException(
                "Ecology fauna movement/body-plan/behavior coverage is incomplete.");
        }
    }

    private static bool IsUnitColor(double red, double green, double blue)
    {
        return red is >= 0.0 and <= 1.0 &&
            green is >= 0.0 and <= 1.0 &&
            blue is >= 0.0 and <= 1.0;
    }
}
