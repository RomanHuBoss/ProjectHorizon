using System;
using System.Collections.Generic;
using System.Linq;

public sealed record EcologyFloraPlacement(
    string InstanceId,
    string FloraId,
    string BiomeId,
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationDegrees,
    double Scale);

public sealed record EcologyFaunaSpawn(
    string InstanceId,
    string FaunaId,
    string BiomeId,
    double PositionX,
    double PositionY,
    double PositionZ,
    double HeadingDegrees,
    bool Simplified);

public sealed record EcologyPlan(
    IReadOnlyList<EcologyFloraPlacement> Flora,
    IReadOnlyList<EcologyFaunaSpawn> ActiveFauna,
    IReadOnlyList<EcologyFaunaSpawn> SimplifiedFauna);

public static class EcologyPlanner
{
    public const int GameplayFloraInstanceCount = 360;

    private static readonly string[] GameplayBiomes =
    {
        "biome.temperate_plain",
        "biome.marsh",
        "biome.desert",
        "biome.coast"
    };

    public static EcologyPlan Plan(EcologyCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return BuildPlan(
            catalog,
            catalog.WorldSeed,
            GameplayBiomes,
            GameplayFloraInstanceCount,
            catalog.ActiveFaunaLimit,
            catalog.SimplifiedFaunaLimit,
            allowAquatic: true,
            terrainProfile: null);
    }

    public static EcologyPlan PlanPlanet(
        EcologyCatalog catalog,
        long worldSeed,
        IReadOnlyList<string> activeBiomeIds,
        double waterCoverage,
        double habitability,
        PlanetSurfaceTerrainProfile? terrainProfile = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(activeBiomeIds);
        if (worldSeed <= 0 || activeBiomeIds.Count is < 1 or > 8 ||
            activeBiomeIds.Distinct(StringComparer.Ordinal).Count() !=
                activeBiomeIds.Count ||
            activeBiomeIds.Any(id => !catalog.Biomes.ContainsKey(id)) ||
            !double.IsFinite(waterCoverage) || waterCoverage is < 0.0 or > 1.0 ||
            !double.IsFinite(habitability) || habitability is < 0.0 or > 1.0)
        {
            throw new InvalidOperationException(
                "Planet ecology profile is invalid.");
        }

        int floraCount = Math.Clamp(
            (int)Math.Round(180.0 + habitability * 180.0),
            180,
            GameplayFloraInstanceCount);
        int activeFauna = Math.Clamp(
            (int)Math.Round(8.0 + habitability * 12.0),
            8,
            catalog.ActiveFaunaLimit);
        int simplifiedFauna = Math.Clamp(
            (int)Math.Round(32.0 + habitability * 48.0),
            32,
            catalog.SimplifiedFaunaLimit);
        return BuildPlan(
            catalog,
            worldSeed,
            activeBiomeIds,
            floraCount,
            activeFauna,
            simplifiedFauna,
            allowAquatic: waterCoverage >= 0.12,
            terrainProfile);
    }

    private static EcologyPlan BuildPlan(
        EcologyCatalog catalog,
        long worldSeed,
        IReadOnlyList<string> activeBiomeIds,
        int floraCount,
        int activeFaunaCount,
        int simplifiedFaunaCount,
        bool allowAquatic,
        PlanetSurfaceTerrainProfile? terrainProfile)
    {
        List<EcologyFloraPlacement> flora = BuildFlora(
            catalog,
            floraCount,
            worldSeed,
            activeBiomeIds,
            terrainProfile);
        List<EcologyFaunaSpawn> active = BuildFauna(
            catalog,
            activeFaunaCount,
            simplified: false,
            worldSeed ^ 0x5D39B1A7L,
            activeBiomeIds,
            allowAquatic,
            terrainProfile);
        List<EcologyFaunaSpawn> simplified = BuildFauna(
            catalog,
            simplifiedFaunaCount,
            simplified: true,
            worldSeed ^ 0x2F77C4D9L,
            activeBiomeIds,
            allowAquatic,
            terrainProfile);
        return new EcologyPlan(flora, active, simplified);
    }

    public static IReadOnlyList<EcologyFloraPlacement> PlanBiome(
        EcologyCatalog catalog,
        string biomeId,
        long seedOffset,
        int count = 32)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.GetBiome(biomeId);
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        EcologyFloraDefinition[] candidates = catalog.Flora.Values
            .Where(flora => flora.BiomeIds.Contains(
                biomeId,
                StringComparer.Ordinal))
            .OrderBy(flora => flora.FloraId, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Biome {biomeId} has no compatible flora.");
        }

        StableRandom random = new(
            unchecked((ulong)(catalog.WorldSeed + seedOffset)) ^
            StableHash(biomeId));
        List<EcologyFloraPlacement> placements = new(count);
        for (int index = 0; index < count; index++)
        {
            EcologyFloraDefinition definition =
                candidates[random.NextInt(candidates.Length)];
            double x = random.NextRange(-28.0, 28.0);
            double z = random.NextRange(-28.0, 28.0);
            double scale = random.NextRange(
                definition.ScaleMin,
                definition.ScaleMax);
            placements.Add(new EcologyFloraPlacement(
                $"ecology.flora.biome_{StableToken(biomeId)}.{index:000}",
                definition.FloraId,
                biomeId,
                x,
                0.0,
                z,
                random.NextRange(0.0, 360.0),
                scale));
        }

        return placements;
    }

    public static bool HasInfrastructureClearance(
        double positionX,
        double positionZ)
    {
        (double X, double Z, double Radius)[] reserved =
        {
            (0.0, -10.0, 7.5),
            (7.0, -9.0, 4.0),
            (14.0, 12.0, 4.0),
            (0.0, 0.0, 5.0),
            (-8.0, 11.0, 3.5),
            (8.0, 11.0, 3.5)
        };
        foreach ((double x, double z, double radius) in reserved)
        {
            double dx = positionX - x;
            double dz = positionZ - z;
            if ((dx * dx) + (dz * dz) < radius * radius)
            {
                return false;
            }
        }

        return true;
    }

    public static ulong StableHash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
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

    private static List<EcologyFloraPlacement> BuildFlora(
        EcologyCatalog catalog,
        int count,
        long seed,
        IReadOnlyList<string> activeBiomeIds,
        PlanetSurfaceTerrainProfile? terrainProfile)
    {
        StableRandom random = new(unchecked((ulong)seed));
        List<EcologyFloraPlacement> placements = new(count);
        Dictionary<string, int> perSpeciesSequence = new(StringComparer.Ordinal);
        int attempts = 0;
        while (placements.Count < count)
        {
            attempts++;
            if (attempts > count * 80)
            {
                throw new InvalidOperationException(
                    "Ecology planner exhausted flora placement attempts.");
            }

            string biomeId = activeBiomeIds[
                placements.Count % activeBiomeIds.Count];
            EcologyFloraDefinition[] candidates = catalog.Flora.Values
                .Where(flora => flora.BiomeIds.Contains(
                    biomeId,
                    StringComparer.Ordinal))
                .OrderBy(flora => flora.FloraId, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            EcologyFloraDefinition definition =
                candidates[random.NextInt(candidates.Length)];
            (double x, double z) = SampleGameplayZone(random, biomeId);
            if (!HasInfrastructureClearance(x, z))
            {
                continue;
            }

            bool tooClose = placements.Any(existing =>
            {
                if (!string.Equals(
                    existing.FloraId,
                    definition.FloraId,
                    StringComparison.Ordinal))
                {
                    return false;
                }

                double dx = existing.PositionX - x;
                double dz = existing.PositionZ - z;
                return (dx * dx) + (dz * dz) <
                    definition.MinimumSpacing * definition.MinimumSpacing;
            });
            if (tooClose)
            {
                continue;
            }

            perSpeciesSequence.TryGetValue(
                definition.FloraId,
                out int sequence);
            sequence++;
            perSpeciesSequence[definition.FloraId] = sequence;
            placements.Add(new EcologyFloraPlacement(
                $"ecology.flora.{StableToken(definition.FloraId)}.{sequence:000}",
                definition.FloraId,
                biomeId,
                x,
                terrainProfile is null
                    ? 0.0
                    : PlanetSurfaceTerrainRuntime.SampleHeight(terrainProfile, x, z),
                z,
                random.NextRange(0.0, 360.0),
                random.NextRange(definition.ScaleMin, definition.ScaleMax)));
        }

        return placements;
    }

    private static List<EcologyFaunaSpawn> BuildFauna(
        EcologyCatalog catalog,
        int count,
        bool simplified,
        long seed,
        IReadOnlyList<string> activeBiomeIds,
        bool allowAquatic,
        PlanetSurfaceTerrainProfile? terrainProfile)
    {
        StableRandom random = new(unchecked((ulong)seed));
        HashSet<string> activeBiomes = activeBiomeIds.ToHashSet(
            StringComparer.Ordinal);
        EcologyFaunaDefinition[] definitions = catalog.Fauna.Values
            .Where(fauna =>
                fauna.BiomeIds.Any(activeBiomes.Contains) &&
                (allowAquatic || !string.Equals(
                    fauna.MovementMode,
                    "Aquatic",
                    StringComparison.Ordinal)))
            .OrderBy(fauna => fauna.FaunaId, StringComparer.Ordinal)
            .ToArray();
        if (definitions.Length == 0)
        {
            definitions = catalog.Fauna.Values
                .Where(fauna => !string.Equals(
                    fauna.MovementMode,
                    "Aquatic",
                    StringComparison.Ordinal))
                .OrderBy(fauna => fauna.FaunaId, StringComparer.Ordinal)
                .ToArray();
        }
        List<EcologyFaunaSpawn> spawns = new(count);
        for (int index = 0; index < count; index++)
        {
            EcologyFaunaDefinition definition = definitions[index % definitions.Length];
            string[] compatibleBiomes = definition.BiomeIds
                .Where(activeBiomes.Contains)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            string biomeId = compatibleBiomes.Length > 0
                ? compatibleBiomes[random.NextInt(compatibleBiomes.Length)]
                : activeBiomeIds[random.NextInt(activeBiomeIds.Count)];
            (double x, double y, double z) = SampleFaunaHabitat(
                random,
                definition.MovementMode,
                simplified);
            if (terrainProfile is not null &&
                !string.Equals(definition.MovementMode, "Aquatic", StringComparison.Ordinal))
            {
                double terrainY = PlanetSurfaceTerrainRuntime.SampleHeight(
                    terrainProfile,
                    x,
                    z);
                y += terrainY;
            }
            spawns.Add(new EcologyFaunaSpawn(
                $"ecology.fauna.{StableToken(definition.FaunaId)}.{index:000}",
                definition.FaunaId,
                biomeId,
                x,
                y,
                z,
                random.NextRange(0.0, 360.0),
                simplified));
        }

        return spawns;
    }

    private static (double X, double Z) SampleGameplayZone(
        StableRandom random,
        string biomeId)
    {
        return biomeId switch
        {
            "biome.marsh" => (
                random.NextRange(-34.0, -13.0),
                random.NextRange(12.0, 34.0)),
            "biome.desert" => (
                random.NextRange(13.0, 34.0),
                random.NextRange(10.0, 34.0)),
            "biome.coast" => (
                random.NextRange(-34.0, -13.0),
                random.NextRange(-34.0, -13.0)),
            _ => (
                random.NextRange(-30.0, 30.0),
                random.NextRange(-30.0, 30.0))
        };
    }

    private static (double X, double Y, double Z) SampleFaunaHabitat(
        StableRandom random,
        string movementMode,
        bool simplified)
    {
        double extent = simplified ? 240.0 : 31.0;
        if (string.Equals(movementMode, "Aquatic", StringComparison.Ordinal))
        {
            return (
                random.NextRange(-33.0, -18.0),
                random.NextRange(0.35, 0.75),
                random.NextRange(18.0, 33.0));
        }

        if (string.Equals(movementMode, "Flying", StringComparison.Ordinal))
        {
            return (
                random.NextRange(-extent, extent),
                random.NextRange(4.0, 9.0),
                random.NextRange(-extent, extent));
        }

        return (
            random.NextRange(-extent, extent),
            0.75,
            random.NextRange(-extent, extent));
    }

    private static string StableToken(string id)
    {
        int dot = id.LastIndexOf('.');
        string token = dot >= 0 ? id[(dot + 1)..] : id;
        return token.Replace('-', '_');
    }

    private sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMaximum));
            }

            return (int)(NextUInt64() % (ulong)exclusiveMaximum);
        }

        public double NextRange(double minimum, double maximum)
        {
            if (!double.IsFinite(minimum) ||
                !double.IsFinite(maximum) ||
                maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            double unit = (NextUInt64() >> 11) *
                (1.0 / 9007199254740992.0);
            return minimum + ((maximum - minimum) * unit);
        }

        private ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
