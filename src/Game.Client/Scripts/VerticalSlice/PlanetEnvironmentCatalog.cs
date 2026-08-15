using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public sealed record PlanetEnvironmentColor(
    double R,
    double G,
    double B);

public sealed record PlanetEnvironmentArchetypeDefinition(
    string Archetype,
    bool Landable,
    double RadiusMinKm,
    double RadiusMaxKm,
    double GravityMinG,
    double GravityMaxG,
    double BaseTemperatureC,
    double TemperatureVariationC,
    double BaseMoisture,
    double AtmosphereDensityMin,
    double AtmosphereDensityMax,
    double WaterCoverageMin,
    double WaterCoverageMax,
    int CloudLayersMin,
    int CloudLayersMax,
    double CloudDensityMin,
    double CloudDensityMax,
    double RadiationLevel,
    double ToxicityLevel,
    PlanetEnvironmentColor AtmosphereColor,
    PlanetEnvironmentColor SunsetColor,
    PlanetEnvironmentColor WaterColor,
    IReadOnlyList<string> BiomeIds);

public sealed record PlanetEnvironmentCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<PlanetEnvironmentArchetypeDefinition> Archetypes);

public sealed class PlanetEnvironmentCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int ExpectedArchetypeCount = 9;

    private static readonly HashSet<string> RequiredArchetypes = new(
        new[]
        {
            "temperate", "desert", "frozen", "volcanic", "toxic",
            "radioactive", "barren", "oceanic", "gas_giant"
        },
        StringComparer.Ordinal);

    private PlanetEnvironmentCatalog(PlanetEnvironmentCatalogDocument document)
    {
        SchemaVersion = document.SchemaVersion;
        Archetypes = document.Archetypes.ToDictionary(
            definition => definition.Archetype,
            StringComparer.Ordinal);
    }

    public int SchemaVersion { get; }

    public IReadOnlyDictionary<string, PlanetEnvironmentArchetypeDefinition>
        Archetypes { get; }

    public static PlanetEnvironmentCatalog LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        PlanetEnvironmentCatalogDocument document =
            JsonSerializer.Deserialize<PlanetEnvironmentCatalogDocument>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ??
            throw new InvalidOperationException(
                "Planet environment catalog deserialized to null.");

        ValidateDocument(document);
        return new PlanetEnvironmentCatalog(document);
    }

    public PlanetEnvironmentArchetypeDefinition Get(string archetype)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archetype);
        return Archetypes.TryGetValue(
            archetype,
            out PlanetEnvironmentArchetypeDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown planet environment archetype {archetype}.");
    }

    public void ValidateBiomeReferences(EcologyCatalog ecologyCatalog)
    {
        ArgumentNullException.ThrowIfNull(ecologyCatalog);
        foreach (PlanetEnvironmentArchetypeDefinition definition in
            Archetypes.Values)
        {
            if (definition.BiomeIds.Any(
                biomeId => !ecologyCatalog.Biomes.ContainsKey(biomeId)))
            {
                throw new InvalidOperationException(
                    $"Planet environment {definition.Archetype} references " +
                    "an unknown ecology biome.");
            }
        }
    }

    private static void ValidateDocument(
        PlanetEnvironmentCatalogDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.Archetypes is null ||
            document.Archetypes.Count != ExpectedArchetypeCount)
        {
            throw new InvalidOperationException(
                "Planet environment catalog header or baseline count is invalid.");
        }

        HashSet<string> archetypes = new(StringComparer.Ordinal);
        foreach (PlanetEnvironmentArchetypeDefinition definition in
            document.Archetypes)
        {
            if (string.IsNullOrWhiteSpace(definition.Archetype) ||
                !RequiredArchetypes.Contains(definition.Archetype) ||
                !archetypes.Add(definition.Archetype) ||
                !double.IsFinite(definition.RadiusMinKm) ||
                !double.IsFinite(definition.RadiusMaxKm) ||
                definition.RadiusMinKm < 20.0 ||
                definition.RadiusMaxKm > 80.0 ||
                definition.RadiusMinKm > definition.RadiusMaxKm ||
                definition.GravityMinG <= 0.0 ||
                definition.GravityMaxG < definition.GravityMinG ||
                !double.IsFinite(definition.BaseTemperatureC) ||
                definition.TemperatureVariationC < 0.0 ||
                !InUnitRange(definition.BaseMoisture) ||
                definition.AtmosphereDensityMin < 0.0 ||
                definition.AtmosphereDensityMax <
                    definition.AtmosphereDensityMin ||
                !InUnitRange(definition.WaterCoverageMin) ||
                !InUnitRange(definition.WaterCoverageMax) ||
                definition.WaterCoverageMax < definition.WaterCoverageMin ||
                definition.CloudLayersMin is < 0 or > 2 ||
                definition.CloudLayersMax is < 0 or > 2 ||
                definition.CloudLayersMax < definition.CloudLayersMin ||
                !InUnitRange(definition.CloudDensityMin) ||
                !InUnitRange(definition.CloudDensityMax) ||
                definition.CloudDensityMax < definition.CloudDensityMin ||
                !InUnitRange(definition.RadiationLevel) ||
                !InUnitRange(definition.ToxicityLevel) ||
                !IsColor(definition.AtmosphereColor) ||
                !IsColor(definition.SunsetColor) ||
                !IsColor(definition.WaterColor) ||
                definition.BiomeIds is null ||
                definition.BiomeIds.Distinct(StringComparer.Ordinal).Count() !=
                    definition.BiomeIds.Count)
            {
                throw new InvalidOperationException(
                    $"Invalid planet environment definition " +
                    $"{definition.Archetype}.");
            }

            if (definition.Landable)
            {
                if (definition.BiomeIds.Count is < 1 or > 8 ||
                    definition.BiomeIds.Any(
                        biomeId =>
                            !GameContentCatalog.IsStableId(biomeId) ||
                            !biomeId.StartsWith(
                                "biome.",
                                StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"Landable planet {definition.Archetype} must define " +
                        "between one and eight stable biome IDs.");
                }
            }
            else if (!string.Equals(
                    definition.Archetype,
                    "gas_giant",
                    StringComparison.Ordinal) ||
                definition.BiomeIds.Count != 0)
            {
                throw new InvalidOperationException(
                    "Only the gas giant is non-landable in the v1 baseline.");
            }
        }

        if (!archetypes.SetEquals(RequiredArchetypes))
        {
            throw new InvalidOperationException(
                "Planet environment catalog does not cover all nine archetypes.");
        }
    }

    private static bool InUnitRange(double value) =>
        double.IsFinite(value) && value >= 0.0 && value <= 1.0;

    private static bool IsColor(PlanetEnvironmentColor color) =>
        color is not null &&
        InUnitRange(color.R) &&
        InUnitRange(color.G) &&
        InUnitRange(color.B);
}
