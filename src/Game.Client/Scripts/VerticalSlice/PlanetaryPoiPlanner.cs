using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record PlanetaryPoiEnvironmentSample(
    string BiomeId,
    double Height,
    double SlopeDegrees,
    double DistanceToWater,
    int Danger);

public sealed record PlanetaryPoiPlacement(
    string InstanceId,
    string PoiTypeId,
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationDegrees,
    PlanetaryPoiEnvironmentSample Environment,
    bool QuestBiased);

public static class PlanetaryPoiPlanner
{
    private const double CandidateMinimum = -34.0;
    private const double CandidateMaximum = 34.0;
    private const double CandidateStep = 2.0;
    private const double CentralExclusionRadius = 23.0;
    private const double ResourceFieldMinimumX = -19.0;
    private const double ResourceFieldMaximumX = 19.0;
    private const double ResourceFieldMinimumZ = 20.0;
    private const double ResourceFieldMaximumZ = 40.0;

    public static IReadOnlyList<PlanetaryPoiPlacement> Plan(
        PlanetaryPoiCatalog catalog,
        IReadOnlyCollection<string>? activeQuestTags = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return PlanInternal(
            catalog,
            catalog.WorldSeed,
            catalog.RegionKey,
            activeQuestTags,
            environmentRuntime: null,
            environmentProfile: null,
            terrainProfile: null);
    }

    public static IReadOnlyList<PlanetaryPoiPlacement> PlanPlanet(
        PlanetaryPoiCatalog catalog,
        long worldSeed,
        string regionKey,
        PlanetEnvironmentRuntime environmentRuntime,
        PlanetEnvironmentProfile environmentProfile,
        PlanetSurfaceTerrainProfile? terrainProfile = null,
        IReadOnlyCollection<string>? activeQuestTags = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(environmentRuntime);
        ArgumentNullException.ThrowIfNull(environmentProfile);
        if (worldSeed <= 0 ||
            !GameContentCatalog.IsStableId(regionKey) ||
            !regionKey.StartsWith("region.", StringComparison.Ordinal) ||
            !environmentProfile.Landable)
        {
            throw new InvalidOperationException(
                "Planetary POI profile identity is invalid.");
        }

        return PlanInternal(
            catalog,
            worldSeed,
            regionKey,
            activeQuestTags,
            environmentRuntime,
            environmentProfile,
            terrainProfile);
    }

    private static IReadOnlyList<PlanetaryPoiPlacement> PlanInternal(
        PlanetaryPoiCatalog catalog,
        long worldSeed,
        string regionKey,
        IReadOnlyCollection<string>? activeQuestTags,
        PlanetEnvironmentRuntime? environmentRuntime,
        PlanetEnvironmentProfile? environmentProfile,
        PlanetSurfaceTerrainProfile? terrainProfile)
    {
        HashSet<string> questTags = activeQuestTags is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : activeQuestTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToHashSet(StringComparer.Ordinal);
        List<(double X, double Z)> candidates = BuildCandidates(
            worldSeed,
            regionKey);
        List<PlanetaryPoiPlacement> placements = new();
        PlanetaryPoiDefinition[] orderedDefinitions = catalog.Definitions.Values
            .OrderByDescending(definition => definition.Rarity)
            .ThenBy(definition => definition.PoiTypeId, StringComparer.Ordinal)
            .ToArray();

        for (int index = 0; index < orderedDefinitions.Length; index++)
        {
            PlanetaryPoiDefinition definition = orderedDefinitions[index];
            bool questBiased = definition.QuestTags.Any(questTags.Contains);
            (double X, double Z)[] validCandidates = candidates
                .Where(candidate => IsValidCandidate(
                    definition,
                    candidate.X,
                    candidate.Z,
                    placements,
                    catalog,
                    worldSeed,
                    environmentRuntime,
                    environmentProfile,
                    terrainProfile))
                .OrderBy(candidate => CandidateScore(
                    worldSeed,
                    regionKey,
                    definition.PoiTypeId,
                    candidate.X,
                    candidate.Z,
                    questBiased))
                .ThenBy(candidate => candidate.X)
                .ThenBy(candidate => candidate.Z)
                .ToArray();
            if (validCandidates.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Unable to place planetary POI {definition.PoiTypeId} " +
                    "within its biome/slope/height/water/danger constraints.");
            }

            (double X, double Z) selected = validCandidates[0];
            PlanetaryPoiEnvironmentSample environment = SampleEnvironment(
                worldSeed,
                selected.X,
                selected.Z,
                environmentRuntime,
                environmentProfile,
                terrainProfile);
            ulong hash = StableHash(
                $"{worldSeed}|{regionKey}|" +
                $"{definition.PoiTypeId}|rotation");
            placements.Add(new PlanetaryPoiPlacement(
                $"poi.instance.{index + 1:000000}",
                definition.PoiTypeId,
                selected.X,
                environment.Height + 0.1 + definition.Size.Y / 2.0,
                selected.Z,
                (hash % 4UL) * 90.0,
                environment,
                questBiased));
            candidates.Remove(selected);
        }

        return placements
            .OrderBy(placement => placement.InstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool MeetsDefinitionConstraints(
        PlanetaryPoiDefinition definition,
        PlanetaryPoiEnvironmentSample environment)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(environment);
        bool biomeAllowed = definition.AllowedBiomes.Contains(
                environment.BiomeId,
                StringComparer.Ordinal) ||
            definition.AllowedBiomes.Contains(
                "biome.test_plain",
                StringComparer.Ordinal);
        return biomeAllowed &&
            environment.SlopeDegrees >= definition.MinimumSlopeDegrees &&
            environment.SlopeDegrees <= definition.MaximumSlopeDegrees &&
            environment.Height >= definition.MinimumHeight &&
            environment.Height <= definition.MaximumHeight &&
            environment.DistanceToWater >= definition.MinimumWaterDistance &&
            environment.DistanceToWater <= definition.MaximumWaterDistance &&
            environment.Danger >= definition.MinimumDanger &&
            environment.Danger <= definition.MaximumDanger;
    }


    public static bool ClearsVerticalSliceInfrastructure(
        PlanetaryPoiDefinition definition,
        double x,
        double z)
    {
        ArgumentNullException.ThrowIfNull(definition);
        double halfWidth = definition.Size.X / 2.0 + 1.0;
        double halfDepth = definition.Size.Z / 2.0 + 1.0;
        double footprintRadius = Math.Max(
            definition.Size.X,
            definition.Size.Z) / 2.0 + 1.0;
        bool outsideCentralGameplayArea =
            Math.Sqrt(x * x + z * z) >=
            CentralExclusionRadius + footprintRadius;
        bool outsideCatalogResourceField =
            x + halfWidth < ResourceFieldMinimumX ||
            x - halfWidth > ResourceFieldMaximumX ||
            z + halfDepth < ResourceFieldMinimumZ ||
            z - halfDepth > ResourceFieldMaximumZ;
        return outsideCentralGameplayArea && outsideCatalogResourceField;
    }

    private static List<(double X, double Z)> BuildCandidates(
        long worldSeed,
        string regionKey)
    {
        List<(double X, double Z)> candidates = new();
        for (double x = CandidateMinimum;
             x <= CandidateMaximum;
             x += CandidateStep)
        {
            for (double z = CandidateMinimum;
                 z <= CandidateMaximum;
                 z += CandidateStep)
            {
                bool outsideCentralGameplayArea =
                    Math.Sqrt(x * x + z * z) >= CentralExclusionRadius;
                bool outsideCatalogResourceField =
                    x < ResourceFieldMinimumX || x > ResourceFieldMaximumX ||
                    z < ResourceFieldMinimumZ || z > ResourceFieldMaximumZ;
                if (!outsideCentralGameplayArea ||
                    !outsideCatalogResourceField)
                {
                    continue;
                }

                candidates.Add((x, z));
            }
        }

        return candidates
            .OrderBy(candidate => StableHash(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1}|{2:0.0}|{3:0.0}",
                    worldSeed,
                    regionKey,
                    candidate.X,
                    candidate.Z)))
            .ThenBy(candidate => candidate.X)
            .ThenBy(candidate => candidate.Z)
            .ToList();
    }

    private static bool IsValidCandidate(
        PlanetaryPoiDefinition definition,
        double x,
        double z,
        IReadOnlyList<PlanetaryPoiPlacement> placements,
        PlanetaryPoiCatalog catalog,
        long worldSeed,
        PlanetEnvironmentRuntime? environmentRuntime,
        PlanetEnvironmentProfile? environmentProfile,
        PlanetSurfaceTerrainProfile? terrainProfile)
    {
        if (!ClearsVerticalSliceInfrastructure(definition, x, z))
        {
            return false;
        }

        PlanetaryPoiEnvironmentSample environment = SampleEnvironment(
            worldSeed,
            x,
            z,
            environmentRuntime,
            environmentProfile,
            terrainProfile);
        if (!MeetsDefinitionConstraints(definition, environment))
        {
            return false;
        }

        foreach (PlanetaryPoiPlacement placement in placements)
        {
            PlanetaryPoiDefinition placedDefinition =
                catalog.GetDefinition(placement.PoiTypeId);
            double requiredSpacing = Math.Max(
                catalog.MinimumPoiSpacing,
                Math.Max(
                    definition.MinimumSpacing,
                    placedDefinition.MinimumSpacing));
            double dx = placement.PositionX - x;
            double dz = placement.PositionZ - z;
            if (Math.Sqrt(dx * dx + dz * dz) < requiredSpacing)
            {
                return false;
            }
        }

        return true;
    }

    private static PlanetaryPoiEnvironmentSample SampleEnvironment(
        long worldSeed,
        double x,
        double z,
        PlanetEnvironmentRuntime? environmentRuntime = null,
        PlanetEnvironmentProfile? environmentProfile = null,
        PlanetSurfaceTerrainProfile? terrainProfile = null)
    {
        ulong hash = StableHash(string.Format(
            CultureInfo.InvariantCulture,
            "{0}|environment|{1:0.0}|{2:0.0}",
            worldSeed,
            x,
            z));
        double syntheticHeight = ((long)(hash % 401UL) - 200L) / 100.0;
        double syntheticSlope = ((hash / 401UL) % 1201UL) / 100.0;
        PlanetSurfaceTerrainSample terrain = terrainProfile is null
            ? new PlanetSurfaceTerrainSample(
                syntheticHeight,
                syntheticSlope,
                0.5)
            : PlanetSurfaceTerrainRuntime.Sample(terrainProfile, x, z);
        // POI catalog height is a local-relief constraint band rather than a
        // world-space Y coordinate. Terrain-backed planning therefore keeps
        // it inside the historical [-2, 2] band while slope is physical.
        double height = terrainProfile is null
            ? terrain.Height
            : Math.Clamp(terrain.Height, -2.0, 2.0);
        double slope = terrain.SlopeDegrees;
        int localDanger = (int)((hash / 481601UL) % 31UL);
        if (environmentRuntime is null || environmentProfile is null)
        {
            return new PlanetaryPoiEnvironmentSample(
                "biome.test_plain",
                height,
                slope,
                Math.Abs(x + 34.0),
                (int)((hash / 481601UL) % 101UL));
        }

        double coverage = Math.Clamp(environmentProfile.WaterCoverage, 0.0, 1.0);
        double shorelineX = 34.0 - coverage * 68.0;
        double distanceToWater = coverage <= 0.001
            ? 80.0
            : Math.Clamp(Math.Abs(x - shorelineX), 0.0, 80.0);
        double latitude = Math.Clamp(z / CandidateMaximum * 58.0, -58.0, 58.0);
        double elevation01 = Math.Clamp(
            (syntheticHeight + 2.0) / 4.0,
            0.0,
            1.0);
        double localNoise = (((hash >> 23) & 0xFFFFUL) / 32767.5) - 1.0;
        PlanetEnvironmentSample climate = environmentRuntime.SampleBiome(
            environmentProfile,
            latitude,
            elevation01,
            distanceToWater * 0.5,
            localNoise);
        double hazard = Math.Max(
            environmentProfile.RadiationLevel,
            environmentProfile.ToxicityLevel);
        int danger = Math.Clamp(
            (int)Math.Round(hazard * 60.0) + localDanger,
            0,
            100);
        return new PlanetaryPoiEnvironmentSample(
            climate.BiomeId,
            height,
            slope,
            distanceToWater,
            danger);
    }

    private static ulong CandidateScore(
        long worldSeed,
        string regionKey,
        string poiTypeId,
        double x,
        double z,
        bool questBiased)
    {
        ulong score = StableHash(string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3:0.0}|{4:0.0}",
            worldSeed,
            regionKey,
            poiTypeId,
            x,
            z));
        return questBiased ? score / 4UL : score;
    }

    private static ulong StableHash(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }
}
