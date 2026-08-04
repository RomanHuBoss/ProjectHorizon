using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record PoiVector3Definition(double X, double Y, double Z);

public sealed record PoiColorDefinition(double R, double G, double B);

public sealed record PlanetaryPoiDefinition(
    string PoiTypeId,
    string LocalizationKey,
    string Category,
    string Shape,
    PoiVector3Definition Size,
    PoiColorDefinition Color,
    int Rarity,
    double MinimumSpacing,
    IReadOnlyList<string> AllowedBiomes,
    double MinimumSlopeDegrees,
    double MaximumSlopeDegrees,
    double MinimumHeight,
    double MaximumHeight,
    double MinimumWaterDistance,
    double MaximumWaterDistance,
    int MinimumDanger,
    int MaximumDanger,
    IReadOnlyList<string> QuestTags,
    double ScanRange,
    string InteractionKind,
    int DiscoveryPoints,
    int ResolutionPoints,
    bool CanBeNamed);

public sealed class PlanetaryPoiCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedPoiTypeCount = 20;

    private static readonly string[] RequiredPdfTypes =
    {
        "poi.emergency_beacon",
        "poi.ship_wreck",
        "poi.abandoned_base",
        "poi.trading_outpost",
        "poi.science_station",
        "poi.ancient_artifact",
        "poi.resource_deposit",
        "poi.cave_entrance",
        "poi.relay",
        "poi.industrial_site",
        "poi.pirate_base",
        "poi.landing_pad",
        "poi.monolith",
        "poi.upgrade_capsule",
        "poi.cargo_container"
    };

    private static readonly HashSet<string> SupportedInteractionKinds = new(
        new[] { "ScanOnly", "Activate", "Open", "Analyze", "Claim", "Land" },
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

    private readonly Dictionary<string, PlanetaryPoiDefinition> _definitions;

    private PlanetaryPoiCatalog(
        int schemaVersion,
        long worldSeed,
        string regionKey,
        double minimumPoiSpacing,
        Dictionary<string, PlanetaryPoiDefinition> definitions)
    {
        SchemaVersion = schemaVersion;
        WorldSeed = worldSeed;
        RegionKey = regionKey;
        MinimumPoiSpacing = minimumPoiSpacing;
        _definitions = definitions;
    }

    public int SchemaVersion { get; }

    public long WorldSeed { get; }

    public string RegionKey { get; }

    public double MinimumPoiSpacing { get; }

    public IReadOnlyDictionary<string, PlanetaryPoiDefinition> Definitions =>
        _definitions;

    public PlanetaryPoiDefinition GetDefinition(string poiTypeId)
    {
        return _definitions.TryGetValue(
            poiTypeId,
            out PlanetaryPoiDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown planetary POI type {poiTypeId}.");
    }

    public static PlanetaryPoiCatalog LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        PlanetaryPoiDocument document;
        try
        {
            document = JsonSerializer.Deserialize<PlanetaryPoiDocument>(
                json,
                JsonOptions) ?? throw new ContentValidationException(
                    "planetary_pois.json deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ContentValidationException(
                $"planetary_pois.json is invalid: {exception.Message}");
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ContentValidationException(
                $"planetary_pois.json schema {document.SchemaVersion} is not " +
                $"supported; expected {CurrentSchemaVersion}.");
        }

        if (document.WorldSeed <= 0 ||
            !GameContentCatalog.IsStableId(document.RegionKey) ||
            document.MinimumPoiSpacing is < 2.0 or > 50.0)
        {
            throw new ContentValidationException(
                "Planetary POI seed, region key or minimum spacing is invalid.");
        }

        Dictionary<string, PlanetaryPoiDefinition> definitions = new(
            StringComparer.Ordinal);
        foreach (PlanetaryPoiDefinition definition in
            document.Definitions ?? Array.Empty<PlanetaryPoiDefinition>())
        {
            ValidateDefinition(definition, document.MinimumPoiSpacing);
            if (!definitions.TryAdd(definition.PoiTypeId, definition))
            {
                throw new ContentValidationException(
                    $"Duplicate planetary POI type {definition.PoiTypeId}.");
            }
        }

        if (definitions.Count != ExpectedPoiTypeCount)
        {
            throw new ContentValidationException(
                "Planetary POI catalog must define exactly " +
                $"{ExpectedPoiTypeCount} types; found {definitions.Count}.");
        }

        string[] missingPdfTypes = RequiredPdfTypes
            .Where(typeId => !definitions.ContainsKey(typeId))
            .ToArray();
        if (missingPdfTypes.Length > 0)
        {
            throw new ContentValidationException(
                "Planetary POI catalog is missing PDF section 21 types: " +
                string.Join(", ", missingPdfTypes));
        }

        return new PlanetaryPoiCatalog(
            document.SchemaVersion,
            document.WorldSeed,
            document.RegionKey,
            document.MinimumPoiSpacing,
            definitions);
    }

    private static void ValidateDefinition(
        PlanetaryPoiDefinition definition,
        double catalogMinimumSpacing)
    {
        if (!GameContentCatalog.IsStableId(definition.PoiTypeId) ||
            !definition.PoiTypeId.StartsWith(
                "poi.",
                StringComparison.Ordinal) ||
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
            definition.Rarity is < 1 or > 5 ||
            definition.MinimumSpacing < catalogMinimumSpacing ||
            definition.AllowedBiomes is null ||
            definition.AllowedBiomes.Count == 0 ||
            definition.AllowedBiomes.Any(biome =>
                !GameContentCatalog.IsStableId(biome)) ||
            definition.MinimumSlopeDegrees < 0.0 ||
            definition.MaximumSlopeDegrees < definition.MinimumSlopeDegrees ||
            definition.MaximumSlopeDegrees > 90.0 ||
            definition.MaximumHeight < definition.MinimumHeight ||
            definition.MinimumWaterDistance < 0.0 ||
            definition.MaximumWaterDistance < definition.MinimumWaterDistance ||
            definition.MinimumDanger is < 0 or > 100 ||
            definition.MaximumDanger < definition.MinimumDanger ||
            definition.MaximumDanger > 100 ||
            definition.QuestTags is null ||
            definition.QuestTags.Count == 0 ||
            definition.QuestTags.Any(string.IsNullOrWhiteSpace) ||
            definition.ScanRange is < 5.0 or > 100.0 ||
            !SupportedInteractionKinds.Contains(definition.InteractionKind) ||
            definition.DiscoveryPoints <= 0 ||
            definition.ResolutionPoints < 0)
        {
            throw new ContentValidationException(
                $"Planetary POI {definition.PoiTypeId} contains invalid values.");
        }
    }

    private sealed record PlanetaryPoiDocument(
        int SchemaVersion,
        long WorldSeed,
        string RegionKey,
        double MinimumPoiSpacing,
        IReadOnlyList<PlanetaryPoiDefinition>? Definitions);
}
