using System;
using System.Collections.Generic;
using System.Linq;

public readonly record struct VegetationRegionCoordinate(int X, int Z);

public enum VegetationLodTier
{
    Near = 0,
    Mid = 1,
    Culled = 2
}

public enum VegetationPromotionReason
{
    Proximity = 0,
    Scan = 1,
    Damage = 2,
    Harvest = 3,
    Quest = 4
}

public sealed record VegetationRegionBatch(
    VegetationRegionCoordinate Region,
    string FloraId,
    bool SmallObject,
    IReadOnlyList<EcologyFloraPlacement> Placements);

public static class VegetationRegionRuntime
{
    public const double RegionSizeMeters = 32.0;
    public const double NearLodDistanceMeters = 30.0;
    public const double MidLodDistanceMeters = 82.0;
    public const double SmallObjectCullDistanceMeters = 52.0;
    public const double PromotionDistanceMeters = 5.0;
    public const double DemotionDistanceMeters = 7.0;
    public const int MaximumNearbyPromotions = 8;
    public const int MaximumQuestPromotions = 4;

    public static VegetationRegionCoordinate WorldToRegion(
        double eastMeters,
        double northMeters) =>
        new(
            (int)Math.Floor(eastMeters / RegionSizeMeters),
            (int)Math.Floor(northMeters / RegionSizeMeters));

    public static (double EastMeters, double NorthMeters) RegionCenter(
        VegetationRegionCoordinate region) =>
        ((region.X + 0.5) * RegionSizeMeters,
         (region.Z + 0.5) * RegionSizeMeters);

    public static IReadOnlyList<VegetationRegionBatch> BuildRegionalBatches(
        IReadOnlyList<EcologyFloraPlacement> placements,
        EcologyCatalog catalog,
        Func<string, bool>? removed = null)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(catalog);
        return placements
            .Where(placement => removed?.Invoke(placement.InstanceId) != true)
            .GroupBy(placement => new
            {
                Region = WorldToRegion(placement.PositionX, placement.PositionZ),
                placement.FloraId
            })
            .OrderBy(group => group.Key.Region.X)
            .ThenBy(group => group.Key.Region.Z)
            .ThenBy(group => group.Key.FloraId, StringComparer.Ordinal)
            .Select(group =>
            {
                EcologyFloraDefinition definition = catalog.GetFlora(group.Key.FloraId);
                EcologyFloraPlacement[] batch = group
                    .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
                    .ToArray();
                return new VegetationRegionBatch(
                    group.Key.Region,
                    group.Key.FloraId,
                    IsSmallObject(definition),
                    batch);
            })
            .ToArray();
    }

    public static bool IsSmallObject(EcologyFloraDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.ScaleMax <= 0.95 ||
            definition.Shape is "Tuft" or "Pad" or "Fungus";
    }

    public static VegetationLodTier ResolveLod(
        double distanceMeters,
        bool smallObject,
        WorldStreamingRegionDetail? residency)
    {
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0.0)
        {
            return VegetationLodTier.Culled;
        }
        if (residency is WorldStreamingRegionDetail.Preload || residency is null)
        {
            return VegetationLodTier.Culled;
        }
        if (smallObject && distanceMeters > SmallObjectCullDistanceMeters)
        {
            return VegetationLodTier.Culled;
        }
        if (residency == WorldStreamingRegionDetail.Simplified)
        {
            return distanceMeters <= MidLodDistanceMeters
                ? VegetationLodTier.Mid
                : VegetationLodTier.Culled;
        }
        if (distanceMeters <= NearLodDistanceMeters)
        {
            return VegetationLodTier.Near;
        }
        return distanceMeters <= MidLodDistanceMeters
            ? VegetationLodTier.Mid
            : VegetationLodTier.Culled;
    }

    public static bool ShouldPromote(
        VegetationPromotionReason reason,
        double distanceMeters,
        bool questRelevant = false) => reason switch
        {
            VegetationPromotionReason.Proximity =>
                distanceMeters <= PromotionDistanceMeters,
            VegetationPromotionReason.Quest => questRelevant,
            VegetationPromotionReason.Scan or
            VegetationPromotionReason.Damage or
            VegetationPromotionReason.Harvest => true,
            _ => false
        };

    public static bool ShouldDemote(
        double distanceMeters,
        bool questRelevant) =>
        !questRelevant && distanceMeters > DemotionDistanceMeters;
}
