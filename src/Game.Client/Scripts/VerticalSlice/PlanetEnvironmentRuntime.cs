using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetEnvironmentProfile(
    string PlanetId,
    string Archetype,
    bool Landable,
    double RadiusKm,
    double SurfaceGravityG,
    double MeanTemperatureC,
    double TemperatureVariationC,
    double BaseMoisture,
    double AtmosphereDensity,
    double WaterCoverage,
    int CloudLayerCount,
    double CloudDensity,
    double RadiationLevel,
    double ToxicityLevel,
    PlanetEnvironmentColor AtmosphereColor,
    PlanetEnvironmentColor SunsetColor,
    PlanetEnvironmentColor WaterColor,
    IReadOnlyList<string> ActiveBiomeIds,
    long Seed);

public sealed record PlanetEnvironmentSample(
    string BiomeId,
    double TemperatureC,
    double Moisture,
    double LatitudeDegrees,
    double NormalizedElevation,
    double DistanceToWaterKm,
    double LocalNoise);

public sealed class PlanetEnvironmentRuntime
{
    private readonly PlanetEnvironmentCatalog _catalog;
    private readonly EcologyCatalog? _ecology;

    public PlanetEnvironmentRuntime(PlanetEnvironmentCatalog catalog)
    {
        _catalog = catalog ??
            throw new ArgumentNullException(nameof(catalog));
    }

    public PlanetEnvironmentRuntime(
        PlanetEnvironmentCatalog catalog,
        EcologyCatalog ecology)
        : this(catalog)
    {
        _ecology = ecology ??
            throw new ArgumentNullException(nameof(ecology));
        _catalog.ValidateBiomeReferences(_ecology);
    }

    public PlanetEnvironmentProfile BuildProfile(
        GalaxyPlanetDefinition planet,
        GalaxyStarType starType)
    {
        ArgumentNullException.ThrowIfNull(planet);
        PlanetEnvironmentArchetypeDefinition definition =
            _catalog.Get(planet.Archetype);

        double radius = Lerp(
            definition.RadiusMinKm,
            definition.RadiusMaxKm,
            Unit(planet.Seed, 0x11UL));
        double gravity = Lerp(
            definition.GravityMinG,
            definition.GravityMaxG,
            Unit(planet.Seed, 0x23UL));
        double starTemperatureOffset = starType switch
        {
            GalaxyStarType.RedDwarf => -5.0,
            GalaxyStarType.OrangeDwarf => -2.0,
            GalaxyStarType.YellowStar => 0.0,
            GalaxyStarType.WhiteStar => 4.0,
            GalaxyStarType.BlueStar => 7.0,
            GalaxyStarType.BinaryDecorative => 2.0,
            _ => 0.0
        };
        double meanTemperature = definition.BaseTemperatureC +
            starTemperatureOffset +
            (Unit(planet.Seed, 0x37UL) - 0.5) * 6.0;

        double atmosphereDensity = planet.HasAtmosphere
            ? Lerp(
                definition.AtmosphereDensityMin,
                definition.AtmosphereDensityMax,
                Unit(planet.Seed, 0x49UL))
            : 0.0;
        double waterCoverage = planet.HasWater
            ? Lerp(
                definition.WaterCoverageMin,
                definition.WaterCoverageMax,
                Unit(planet.Seed, 0x5BUL))
            : 0.0;
        int cloudLayers = planet.HasAtmosphere
            ? InterpolateInteger(
                definition.CloudLayersMin,
                definition.CloudLayersMax,
                Unit(planet.Seed, 0x6DUL))
            : 0;
        double cloudDensity = cloudLayers > 0
            ? Lerp(
                definition.CloudDensityMin,
                definition.CloudDensityMax,
                Unit(planet.Seed, 0x7FUL))
            : 0.0;

        return new PlanetEnvironmentProfile(
            planet.PlanetId,
            planet.Archetype,
            definition.Landable,
            radius,
            gravity,
            meanTemperature,
            definition.TemperatureVariationC,
            definition.BaseMoisture,
            atmosphereDensity,
            waterCoverage,
            cloudLayers,
            cloudDensity,
            definition.RadiationLevel,
            definition.ToxicityLevel,
            definition.AtmosphereColor,
            definition.SunsetColor,
            definition.WaterColor,
            definition.BiomeIds.ToArray(),
            planet.Seed);
    }

    public PlanetEnvironmentSample SampleBiome(
        PlanetEnvironmentProfile profile,
        double latitudeDegrees,
        double normalizedElevation,
        double distanceToWaterKm,
        double localNoise)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Landable || profile.ActiveBiomeIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Planet {profile.PlanetId} has no landable biome surface.");
        }

        double latitude = Math.Clamp(latitudeDegrees, -90.0, 90.0);
        double elevation = Math.Clamp(normalizedElevation, 0.0, 1.0);
        double noise = Math.Clamp(localNoise, -1.0, 1.0);
        double waterDistance = Math.Max(0.0, distanceToWaterKm);
        double latitudeCooling = Math.Abs(latitude) / 90.0;
        double temperature = profile.MeanTemperatureC -
            profile.TemperatureVariationC *
            (0.58 * latitudeCooling + 0.32 * elevation) +
            noise * profile.TemperatureVariationC * 0.10;
        double waterMoisture = profile.WaterCoverage <= 0.0
            ? 0.0
            : profile.WaterCoverage * Math.Exp(-waterDistance / 18.0) * 0.36;
        double moisture = Math.Clamp(
            profile.BaseMoisture +
            waterMoisture -
            elevation * 0.16 +
            noise * 0.08,
            0.0,
            1.0);

        EcologyCatalog ecology = _ecology ??
            throw new InvalidOperationException(
                "Biome sampling requires an ecology catalog.");

        string bestBiome = profile.ActiveBiomeIds[0];
        double bestScore = double.PositiveInfinity;
        foreach (string biomeId in profile.ActiveBiomeIds)
        {
            EcologyBiomeDefinition biome = ecology.GetBiome(biomeId);
            double temperaturePenalty = RangeDistance(
                temperature,
                biome.TemperatureMin,
                biome.TemperatureMax) / 32.0;
            double moisturePenalty = RangeDistance(
                moisture,
                biome.MoistureMin,
                biome.MoistureMax);
            double waterPenalty = biome.SupportsWater
                ? Math.Min(1.0, waterDistance / 24.0) * 0.28
                : Math.Max(0.0, 0.20 - waterDistance / 120.0) *
                    profile.WaterCoverage;
            double deterministicTieBreak =
                Unit(profile.Seed ^ StableStringHash(biomeId), 0x91UL) *
                0.0001;
            double score = temperaturePenalty + moisturePenalty +
                waterPenalty + deterministicTieBreak;
            if (score < bestScore)
            {
                bestScore = score;
                bestBiome = biomeId;
            }
        }

        return new PlanetEnvironmentSample(
            bestBiome,
            temperature,
            moisture,
            latitude,
            elevation,
            waterDistance,
            noise);
    }

    private static double RangeDistance(
        double value,
        double minimum,
        double maximum)
    {
        if (value < minimum)
        {
            return minimum - value;
        }

        return value > maximum ? value - maximum : 0.0;
    }

    private static int InterpolateInteger(
        int minimum,
        int maximum,
        double unit)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        int span = maximum - minimum + 1;
        return minimum + Math.Min(span - 1, (int)(unit * span));
    }

    private static double Lerp(double minimum, double maximum, double unit) =>
        minimum + (maximum - minimum) * Math.Clamp(unit, 0.0, 1.0);

    private static double Unit(long seed, ulong salt)
    {
        ulong value = Mix(unchecked((ulong)seed) ^ salt);
        return (value >> 11) * (1.0 / 9007199254740992.0);
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static long StableStringHash(string value)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            return (long)(hash & 0x7FFF_FFFF_FFFF_FFFFUL);
        }
    }
}
