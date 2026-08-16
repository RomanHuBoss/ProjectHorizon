using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record PlanetSurfaceSkyProfile(
    string PlanetId,
    string Archetype,
    GalaxyStarType StarType,
    PlanetEnvironmentColor SkyTopColor,
    PlanetEnvironmentColor SkyHorizonColor,
    PlanetEnvironmentColor GroundHorizonColor,
    PlanetEnvironmentColor SunColor,
    double SunEnergy,
    double SunAzimuthDegrees,
    double SunElevationDegrees,
    double SunAngularDiameterDegrees,
    bool AtmosphereEnabled,
    double FogDensity,
    double FogSunScatter,
    int CloudLayerCount,
    int CloudClusterCount,
    double CloudOpacity,
    long Seed);

public sealed record PlanetSurfaceResourcePlacement(
    string ResourceNodeId,
    string ResourceDefinitionId,
    int ChunkX,
    int ChunkZ,
    int Slot,
    double PositionX,
    double PositionY,
    double PositionZ,
    double SlopeDegrees,
    double Scale,
    double RotationDegrees);

/// <summary>
/// Pure deterministic surface-world composition. It owns no Godot objects and
/// can therefore be used by runtime, xUnit and F5 acceptance without touching
/// the scene tree. Terrain identity remains TASK-156/TASK-158; this layer only
/// derives presentation and chunk-scoped resource instances from that identity.
/// </summary>
public static class PlanetSurfaceWorldCompositionRuntime
{
    public const double StarterReserveRadiusMeters = 28.0;
    public const int MaximumResourcesPerChunk = 2;
    public const double PrimaryResourceSpawnProbability = 0.72;
    public const double SecondaryResourceSpawnProbability = 0.20;
    public const double ResourceChunkMarginMeters = 4.25;
    public const double MaximumResourceSlopeDegrees = 30.0;

    public static PlanetSurfaceSkyProfile BuildSkyProfile(
        PlanetEnvironmentProfile environment,
        GalaxyStarType starType)
    {
        ArgumentNullException.ThrowIfNull(environment);
        double atmosphere = Math.Clamp(environment.AtmosphereDensity, 0.0, 2.0);
        bool atmosphereEnabled = atmosphere > 0.02;
        PlanetEnvironmentColor starColor = ResolveStarColor(starType);
        double starEnergy = starType switch
        {
            GalaxyStarType.RedDwarf => 1.10,
            GalaxyStarType.OrangeDwarf => 1.30,
            GalaxyStarType.YellowStar => 1.42,
            GalaxyStarType.WhiteStar => 1.50,
            GalaxyStarType.BlueStar => 1.58,
            GalaxyStarType.BinaryDecorative => 1.46,
            _ => 1.35
        };

        double phaseA = Unit(environment.Seed, 0xA3UL);
        double phaseB = Unit(environment.Seed, 0xB7UL);
        double azimuth = 18.0 + phaseA * 124.0;
        double elevation = 28.0 + phaseB * 31.0;

        PlanetEnvironmentColor top = atmosphereEnabled
            ? Blend(
                Scale(environment.AtmosphereColor, 0.42 + atmosphere * 0.18),
                new PlanetEnvironmentColor(0.025, 0.045, 0.085),
                0.18)
            : new PlanetEnvironmentColor(0.004, 0.006, 0.014);
        PlanetEnvironmentColor horizon = atmosphereEnabled
            ? Blend(
                Scale(environment.AtmosphereColor, 0.95),
                environment.SunsetColor,
                0.18 + Math.Clamp(environment.CloudDensity, 0.0, 1.0) * 0.10)
            : new PlanetEnvironmentColor(0.012, 0.014, 0.022);
        PlanetEnvironmentColor groundHorizon = atmosphereEnabled
            ? Scale(horizon, 0.50)
            : new PlanetEnvironmentColor(0.008, 0.008, 0.010);

        double cloudOpacity = environment.CloudLayerCount <= 0
            ? 0.0
            : Math.Clamp(0.16 + environment.CloudDensity * 0.42, 0.15, 0.56);
        int cloudClusters = environment.CloudLayerCount <= 0
            ? 0
            : Math.Clamp(
                4 + (int)Math.Round(environment.CloudDensity * 10.0) +
                environment.CloudLayerCount * 2,
                6,
                18);
        double fogDensity = atmosphereEnabled
            ? Math.Clamp(0.0012 + atmosphere * 0.0011 +
                environment.CloudDensity * 0.00055, 0.0010, 0.0048)
            : 0.0;

        return new PlanetSurfaceSkyProfile(
            environment.PlanetId,
            environment.Archetype,
            starType,
            ClampColor(top),
            ClampColor(horizon),
            ClampColor(groundHorizon),
            starColor,
            starEnergy * Math.Clamp(0.82 + atmosphere * 0.18, 0.82, 1.18),
            azimuth,
            elevation,
            0.62,
            atmosphereEnabled,
            fogDensity,
            atmosphereEnabled ? Math.Clamp(0.32 + atmosphere * 0.22, 0.30, 0.68) : 0.0,
            environment.CloudLayerCount,
            cloudClusters,
            cloudOpacity,
            environment.Seed);
    }

    public static IReadOnlyList<PlanetSurfaceResourcePlacement> BuildResourceWindow(
        PlanetSurfaceContentProfile surface,
        IReadOnlyDictionary<string, GameResourceDefinition> resources,
        PlanetSurfaceChunkCoordinate center)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(resources);
        return PlanetSurfaceStreamingRuntime.BuildPlan(center)
            .SelectMany(spec => BuildChunkResources(
                surface,
                resources,
                spec.Coordinate))
            .OrderBy(placement => placement.ResourceNodeId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<PlanetSurfaceResourcePlacement> BuildChunkResources(
        PlanetSurfaceContentProfile surface,
        IReadOnlyDictionary<string, GameResourceDefinition> resources,
        PlanetSurfaceChunkCoordinate chunk)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0)
        {
            return Array.Empty<PlanetSurfaceResourcePlacement>();
        }

        List<PlanetSurfaceResourcePlacement> result = new(MaximumResourcesPerChunk);
        for (int slot = 0; slot < MaximumResourcesPerChunk; slot++)
        {
            ulong identity = BuildIdentity(surface.WorldSeed, chunk, slot);
            double spawnProbability = slot == 0
                ? PrimaryResourceSpawnProbability
                : SecondaryResourceSpawnProbability;
            if (Unit(identity, 0x17UL) > spawnProbability)
            {
                continue;
            }

            GameResourceDefinition definition = SelectResource(
                resources.Values,
                surface.Environment.Archetype,
                identity);
            PlanetSurfaceResourcePlacement? placement = TryPlaceResource(
                surface,
                definition,
                chunk,
                slot,
                identity);
            if (placement is not null)
            {
                result.Add(placement);
            }
        }

        return result;
    }

    public static string BuildResourceNodeId(
        string planetId,
        PlanetSurfaceChunkCoordinate chunk,
        int slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planetId);
        if (slot < 0 || slot >= MaximumResourcesPerChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
        string planetToken = "p" + StableHash(planetId).ToString(
            "x16", CultureInfo.InvariantCulture);
        string x = EncodeCoordinate(chunk.X);
        string z = EncodeCoordinate(chunk.Z);
        string id = $"surface_resource.{planetToken}.x{x}.z{z}.s{slot}";
        if (!GameContentCatalog.IsStableId(id))
        {
            throw new InvalidOperationException(
                $"Surface resource identity {id} is not a stable ID.");
        }
        return id;
    }


    public static (double X, double Z) BuildPoiPresentationPosition(
        PlanetSurfaceContentProfile surface,
        string instanceId)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ulong identity = Mix(unchecked((ulong)surface.WorldSeed) ^
            StableHash(instanceId));
        for (int attempt = 0; attempt < 16; attempt++)
        {
            ulong candidate = Mix(identity +
                (ulong)(attempt + 1) * 0x9E3779B97F4A7C15UL);
            double angle = Unit(candidate, 0x91UL) * Math.PI * 2.0 +
                attempt * 2.399963229728653;
            double radius = 78.0 + Unit(candidate, 0xA7UL) * 342.0;
            double x = Math.Cos(angle) * radius;
            double z = Math.Sin(angle) * radius;
            PlanetSurfaceTerrainSample terrain =
                PlanetSurfaceTerrainRuntime.Sample(surface.Terrain, x, z);
            if (terrain.SlopeDegrees <=
                surface.Terrain.MaximumWalkableSlopeDegrees)
            {
                return (x, z);
            }
        }

        double fallbackAngle = Unit(identity, 0xC1UL) * Math.PI * 2.0;
        return (Math.Cos(fallbackAngle) * 96.0,
            Math.Sin(fallbackAngle) * 96.0);
    }

    public static bool IsOutsideStarterReserve(double x, double z) =>
        (x * x) + (z * z) >=
        StarterReserveRadiusMeters * StarterReserveRadiusMeters;

    private static PlanetSurfaceResourcePlacement? TryPlaceResource(
        PlanetSurfaceContentProfile surface,
        GameResourceDefinition definition,
        PlanetSurfaceChunkCoordinate chunk,
        int slot,
        ulong identity)
    {
        double half = PlanetSurfaceStreamingRuntime.ChunkSizeMeters * 0.5;
        double usable = half - ResourceChunkMarginMeters;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            ulong attemptSeed = Mix(identity + (ulong)(attempt + 1) * 0x9E3779B97F4A7C15UL);
            double localX = SignedUnit(attemptSeed, 0x31UL) * usable;
            double localZ = SignedUnit(attemptSeed, 0x43UL) * usable;
            double x = chunk.X * PlanetSurfaceStreamingRuntime.ChunkSizeMeters + localX;
            double z = chunk.Z * PlanetSurfaceStreamingRuntime.ChunkSizeMeters + localZ;
            if (!IsOutsideStarterReserve(x, z))
            {
                continue;
            }

            PlanetSurfaceTerrainSample terrain = PlanetSurfaceTerrainRuntime.Sample(
                surface.Terrain,
                x,
                z);
            double maximumSlope = Math.Min(
                MaximumResourceSlopeDegrees,
                surface.Terrain.MaximumWalkableSlopeDegrees);
            if (terrain.SlopeDegrees > maximumSlope)
            {
                continue;
            }

            return new PlanetSurfaceResourcePlacement(
                BuildResourceNodeId(surface.PlanetId, chunk, slot),
                definition.ResourceId,
                chunk.X,
                chunk.Z,
                slot,
                x,
                terrain.Height,
                z,
                terrain.SlopeDegrees,
                0.80 + Unit(attemptSeed, 0x59UL) * 0.72,
                Unit(attemptSeed, 0x6BUL) * 360.0);
        }

        return null;
    }

    private static GameResourceDefinition SelectResource(
        IEnumerable<GameResourceDefinition> resources,
        string archetype,
        ulong identity)
    {
        GameResourceDefinition[] ordered = resources
            .Where(resource => resource.Tags.Contains("surface", StringComparer.Ordinal))
            .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            throw new InvalidOperationException(
                "Surface resource planner has no surface-tagged resources.");
        }

        double[] weights = ordered
            .Select(resource => ResourceWeight(resource, archetype))
            .ToArray();
        double total = weights.Sum();
        double roll = Unit(identity, 0x7DUL) * total;
        for (int index = 0; index < ordered.Length; index++)
        {
            roll -= weights[index];
            if (roll <= 0.0)
            {
                return ordered[index];
            }
        }
        return ordered[^1];
    }

    private static double ResourceWeight(
        GameResourceDefinition resource,
        string archetype)
    {
        bool Has(string tag) => resource.Tags.Contains(tag, StringComparer.Ordinal);
        double weight = Has("salvage") ? 0.06 : 0.35;
        if (Has("ore") || Has("metal") || Has("crystal") || Has("rock"))
        {
            weight += 0.95;
        }

        weight += archetype switch
        {
            "temperate" =>
                (Has("bio") || Has("flora") || Has("fungal") || Has("microbe") ? 2.2 : 0.0) +
                (Has("water") || Has("brine") ? 1.25 : 0.0) +
                (Has("ore") || Has("crystal") ? 0.80 : 0.0),
            "desert" =>
                (Has("ore") || Has("crystal") || Has("salt") || Has("silicon") ? 2.4 : 0.0) +
                (Has("rare_earth") || Has("iridium") || Has("tungsten") ? 1.15 : 0.0) +
                (Has("water") || Has("ice") || Has("bio") ? -0.22 : 0.0),
            "frozen" =>
                (Has("ice") || Has("water") || Has("gas") || Has("volatile") || Has("clathrate") ? 2.8 : 0.0) +
                (Has("ore") || Has("salt") ? 0.75 : 0.0),
            "volcanic" =>
                (Has("volcanic") || Has("sulfur") || Has("high_temp") ? 3.0 : 0.0) +
                (Has("ore") || Has("metal") || Has("nuclear") ? 1.5 : 0.0) +
                (Has("water") || Has("ice") || Has("bio") ? -0.28 : 0.0),
            "toxic" =>
                (Has("acid") || Has("brine") || Has("sulfur") || Has("chemistry") ? 2.4 : 0.0),
            "radioactive" =>
                (Has("uranium") || Has("thorium") || Has("nuclear") || Has("rare_earth") ? 3.0 : 0.0),
            "oceanic" =>
                (Has("water") || Has("brine") || Has("bio") || Has("microbe") ? 2.6 : 0.0),
            _ => Has("ore") || Has("rock") || Has("crystal") ? 1.1 : 0.2
        };
        return Math.Max(0.05, weight);
    }

    private static PlanetEnvironmentColor ResolveStarColor(GalaxyStarType starType) =>
        starType switch
        {
            GalaxyStarType.RedDwarf => new PlanetEnvironmentColor(1.00, 0.36, 0.20),
            GalaxyStarType.OrangeDwarf => new PlanetEnvironmentColor(1.00, 0.66, 0.34),
            GalaxyStarType.YellowStar => new PlanetEnvironmentColor(1.00, 0.91, 0.68),
            GalaxyStarType.WhiteStar => new PlanetEnvironmentColor(0.92, 0.96, 1.00),
            GalaxyStarType.BlueStar => new PlanetEnvironmentColor(0.58, 0.75, 1.00),
            GalaxyStarType.BinaryDecorative => new PlanetEnvironmentColor(0.95, 0.86, 1.00),
            _ => new PlanetEnvironmentColor(1.00, 0.90, 0.72)
        };

    private static PlanetEnvironmentColor Scale(PlanetEnvironmentColor color, double scale) =>
        new(color.R * scale, color.G * scale, color.B * scale);

    private static PlanetEnvironmentColor Blend(
        PlanetEnvironmentColor first,
        PlanetEnvironmentColor second,
        double amount)
    {
        double t = Math.Clamp(amount, 0.0, 1.0);
        return new PlanetEnvironmentColor(
            first.R + (second.R - first.R) * t,
            first.G + (second.G - first.G) * t,
            first.B + (second.B - first.B) * t);
    }

    private static PlanetEnvironmentColor ClampColor(PlanetEnvironmentColor color) =>
        new(
            Math.Clamp(color.R, 0.0, 1.0),
            Math.Clamp(color.G, 0.0, 1.0),
            Math.Clamp(color.B, 0.0, 1.0));

    private static ulong BuildIdentity(
        long worldSeed,
        PlanetSurfaceChunkCoordinate chunk,
        int slot)
    {
        ulong value = unchecked((ulong)worldSeed);
        value ^= Mix(unchecked((ulong)(long)chunk.X) + 0xA24BAED4963EE407UL);
        value ^= Mix(unchecked((ulong)(long)chunk.Z) + 0x9FB21C651E98DF25UL);
        value ^= Mix((ulong)(slot + 1) * 0xD6E8FEB86659FD93UL);
        return Mix(value);
    }

    private static string EncodeCoordinate(int value) => value < 0
        ? "n" + Math.Abs((long)value).ToString(CultureInfo.InvariantCulture)
        : "p" + value.ToString(CultureInfo.InvariantCulture);

    private static double Unit(long seed, ulong salt) =>
        Unit(unchecked((ulong)seed), salt);

    private static double Unit(ulong seed, ulong salt)
    {
        ulong value = Mix(seed ^ salt);
        return (value >> 11) * (1.0 / 9007199254740992.0);
    }

    private static double SignedUnit(ulong seed, ulong salt) =>
        Unit(seed, salt) * 2.0 - 1.0;

    private static ulong StableHash(string text)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char character in text)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
