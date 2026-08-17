using System;
using System.Collections.Generic;
using System.Linq;

public sealed record VegetationRegionalDiagnostics(
    int RegionalGroups,
    int Regions,
    int Lod0Groups,
    int Lod1Groups,
    int NearVisibleGroups,
    int MidVisibleGroups,
    int CulledGroups,
    int PromotedEntities,
    bool WorldStreamingAware,
    bool RegionTypePartitioned);

public sealed record VegetationRegionalAcceptanceReport(
    bool Passed,
    bool RegionalPartition,
    bool LodPolicy,
    bool SmallObjectCulling,
    bool StreamingResidency,
    bool PromotionCoverage,
    bool LiveBinding,
    int RegionalGroups,
    int Regions,
    int PromotionReasons,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-196 regional vegetation acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"regional={(RegionalPartition ? 1 : 0)}; groups={RegionalGroups}; regions={Regions}; " +
        $"lod={(LodPolicy ? 1 : 0)}; smallCull={(SmallObjectCulling ? 1 : 0)}; " +
        $"streaming={(StreamingResidency ? 1 : 0)}; promotions={PromotionReasons}/5; " +
        $"live={(LiveBinding ? 1 : 0)}; result={Result}";
}

public static class VegetationRegionalAcceptanceRunner
{
    public static VegetationRegionalAcceptanceReport Evaluate(
        EcologyCatalog catalog,
        EcologyPlan plan,
        VegetationRegionalDiagnostics live)
    {
        IReadOnlyList<VegetationRegionBatch> batches =
            VegetationRegionRuntime.BuildRegionalBatches(plan.Flora, catalog);
        bool regional = batches.Count > 0 &&
            batches.Select(batch => batch.Region).Distinct().Count() >= 2 &&
            batches.GroupBy(batch => new { batch.Region, batch.FloraId })
                .All(group => group.Count() == 1) &&
            batches.Sum(batch => batch.Placements.Count) == plan.Flora.Count;
        bool lod =
            VegetationRegionRuntime.ResolveLod(5.0, false, WorldStreamingRegionDetail.Full) == VegetationLodTier.Near &&
            VegetationRegionRuntime.ResolveLod(45.0, false, WorldStreamingRegionDetail.Full) == VegetationLodTier.Mid &&
            VegetationRegionRuntime.ResolveLod(100.0, false, WorldStreamingRegionDetail.Full) == VegetationLodTier.Culled;
        bool smallCull =
            VegetationRegionRuntime.ResolveLod(60.0, true, WorldStreamingRegionDetail.Full) == VegetationLodTier.Culled &&
            VegetationRegionRuntime.ResolveLod(45.0, true, WorldStreamingRegionDetail.Full) == VegetationLodTier.Mid;
        bool streaming =
            VegetationRegionRuntime.ResolveLod(10.0, false, WorldStreamingRegionDetail.Simplified) == VegetationLodTier.Mid &&
            VegetationRegionRuntime.ResolveLod(10.0, false, WorldStreamingRegionDetail.Preload) == VegetationLodTier.Culled &&
            VegetationRegionRuntime.ResolveLod(10.0, false, null) == VegetationLodTier.Culled;
        VegetationPromotionReason[] reasons = Enum.GetValues<VegetationPromotionReason>();
        bool promotions = reasons.Length == 5 &&
            reasons.All(reason => VegetationRegionRuntime.ShouldPromote(
                reason,
                reason == VegetationPromotionReason.Proximity ? 3.0 : 20.0,
                questRelevant: reason == VegetationPromotionReason.Quest)) &&
            VegetationRegionRuntime.ShouldDemote(12.0, questRelevant: false) &&
            !VegetationRegionRuntime.ShouldDemote(12.0, questRelevant: true);
        bool liveBinding = live.RegionalGroups > 0 && live.Regions >= 2 &&
            live.Lod0Groups == live.RegionalGroups &&
            live.Lod1Groups == live.RegionalGroups &&
            live.WorldStreamingAware && live.RegionTypePartitioned;
        bool passed = regional && lod && smallCull && streaming && promotions && liveBinding;
        return new VegetationRegionalAcceptanceReport(
            passed,
            regional,
            lod,
            smallCull,
            streaming,
            promotions,
            liveBinding,
            live.RegionalGroups,
            live.Regions,
            reasons.Length,
            passed
                ? "spec section 11 regional MultiMesh, distance LOD/culling, streaming residency and five-trigger interactive promotion verified"
                : "one or more regional vegetation invariants failed");
    }
}
