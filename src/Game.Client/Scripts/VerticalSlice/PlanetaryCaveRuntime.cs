using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record PlanetaryCaveArchetype(
    string ArchetypeId,
    string LocalizationKey,
    double InteriorDepthMeters,
    double CorridorLengthMeters,
    int ChamberCount,
    IReadOnlyList<string> ResourceDefinitionIds,
    double AccentR,
    double AccentG,
    double AccentB);

public sealed record PlanetaryCaveDepositPlan(
    string DepositId,
    string ResourceDefinitionId,
    double LocalX,
    double LocalY,
    double LocalZ,
    double RotationDegrees);

public sealed record PlanetaryCavePlan(
    string CaveInstanceId,
    string PlanetId,
    string EntrancePoiInstanceId,
    PlanetaryCaveArchetype Archetype,
    IReadOnlyList<PlanetaryCaveDepositPlan> Deposits,
    double EntryLocalX,
    double EntryLocalY,
    double EntryLocalZ,
    double ExitLocalX,
    double ExitLocalY,
    double ExitLocalZ)
{
    public bool GlobalProceduralCaveNetwork => false;
    public bool TerrainDeformationEnabled => false;
}

public static class PlanetaryCaveRuntime
{
    public const int RequiredArchetypeCount = 3;
    public const int DepositsPerCave = 3;
    public const double MinimumInteriorDepthMeters = 36.0;

    private static readonly PlanetaryCaveArchetype[] Archetypes =
    {
        new(
            "cave.basalt_lava_tube",
            "cave.basalt_lava_tube",
            48.0,
            31.0,
            3,
            new[]
            {
                "resource.ferric_ore",
                "resource.sulfur_crystal",
                "resource.volcanic_glass"
            },
            0.92,
            0.34,
            0.12),
        new(
            "cave.crystal_grotto",
            "cave.crystal_grotto",
            52.0,
            29.0,
            3,
            new[]
            {
                "resource.conductive_crystal",
                "resource.silicon_crystal",
                "resource.rare_earth_ore"
            },
            0.24,
            0.72,
            0.96),
        new(
            "cave.hydrothermal_hollow",
            "cave.hydrothermal_hollow",
            44.0,
            33.0,
            3,
            new[]
            {
                "resource.acidic_brine",
                "resource.raw_compotium",
                "resource.paraffinium"
            },
            0.24,
            0.86,
            0.58)
    };

    public static IReadOnlyList<PlanetaryCaveArchetype> SupportedArchetypes =>
        Archetypes;

    public static PlanetaryCavePlan BuildPlan(
        string planetId,
        string entrancePoiInstanceId,
        long worldSeed)
    {
        if (!GameContentCatalog.IsStableId(planetId))
        {
            throw new ArgumentException(
                "Planetary cave planet ID must be stable.",
                nameof(planetId));
        }
        if (!GameContentCatalog.IsStableId(entrancePoiInstanceId))
        {
            throw new ArgumentException(
                "Planetary cave entrance POI ID must be stable.",
                nameof(entrancePoiInstanceId));
        }
        if (worldSeed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldSeed));
        }

        ulong hash = StableHash(
            $"{worldSeed}|{planetId}|{entrancePoiInstanceId}|TASK-192");
        PlanetaryCaveArchetype archetype =
            Archetypes[(int)(hash % (ulong)Archetypes.Length)];
        string planetToken = StableHash(planetId).ToString("x8", CultureInfo.InvariantCulture);
        string caveToken = StableHash(entrancePoiInstanceId).ToString("x8", CultureInfo.InvariantCulture);

        double half = archetype.CorridorLengthMeters * 0.5;
        PlanetaryCaveDepositPlan[] deposits = archetype.ResourceDefinitionIds
            .Take(DepositsPerCave)
            .Select((resourceId, index) =>
            {
                double side = index % 2 == 0 ? -1.0 : 1.0;
                double z = -7.0 - index * Math.Max(7.0, half * 0.45);
                double jitter = ((hash >> (index * 7)) & 0x1f) / 31.0;
                return new PlanetaryCaveDepositPlan(
                    $"cave.deposit.p{planetToken}.c{caveToken}.d{index}",
                    resourceId,
                    side * (1.65 + jitter * 0.55),
                    0.52,
                    Math.Max(-archetype.CorridorLengthMeters + 3.5, z),
                    (double)((hash >> (index * 11 + 3)) % 360));
            })
            .ToArray();

        return new PlanetaryCavePlan(
            $"cave.instance.p{planetToken}.c{caveToken}",
            planetId,
            entrancePoiInstanceId,
            archetype,
            deposits,
            0.0,
            1.10,
            0.0,
            0.0,
            1.05,
            2.1);
    }

    public static bool ValidatePlan(
        PlanetaryCavePlan plan,
        IReadOnlyDictionary<string, GameResourceDefinition> resources,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resources);
        if (!GameContentCatalog.IsStableId(plan.CaveInstanceId) ||
            !GameContentCatalog.IsStableId(plan.PlanetId) ||
            !GameContentCatalog.IsStableId(plan.EntrancePoiInstanceId))
        {
            failure = "unstable cave identity";
            return false;
        }
        if (plan.Archetype.InteriorDepthMeters < MinimumInteriorDepthMeters ||
            plan.Archetype.ChamberCount < 2 ||
            plan.Deposits.Count != DepositsPerCave)
        {
            failure = "invalid cave geometry/deposit budget";
            return false;
        }
        if (plan.GlobalProceduralCaveNetwork || plan.TerrainDeformationEnabled)
        {
            failure = "global caves or terrain deformation must remain disabled";
            return false;
        }
        if (plan.Deposits.Select(deposit => deposit.DepositId)
            .Distinct(StringComparer.Ordinal).Count() != plan.Deposits.Count)
        {
            failure = "duplicate cave deposit IDs";
            return false;
        }
        foreach (PlanetaryCaveDepositPlan deposit in plan.Deposits)
        {
            if (!GameContentCatalog.IsStableId(deposit.DepositId) ||
                !resources.ContainsKey(deposit.ResourceDefinitionId))
            {
                failure = $"invalid cave deposit {deposit.DepositId}";
                return false;
            }
        }
        failure = string.Empty;
        return true;
    }

    private static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }
}
