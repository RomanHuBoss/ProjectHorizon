namespace ProjectHorizon.Tests.Unit;

public sealed class VegetationRegionalTests
{
    [Fact]
    public void RegionalCoordinatesAreDeterministic()
    {
        Assert.Equal(new VegetationRegionCoordinate(0, 0),
            VegetationRegionRuntime.WorldToRegion(0.0, 0.0));
        Assert.Equal(new VegetationRegionCoordinate(-1, 1),
            VegetationRegionRuntime.WorldToRegion(-0.1, 32.1));
    }

    [Fact]
    public void LodPolicyRespectsDistanceAndResidency()
    {
        Assert.Equal(VegetationLodTier.Near,
            VegetationRegionRuntime.ResolveLod(5.0, false, WorldStreamingRegionDetail.Full));
        Assert.Equal(VegetationLodTier.Mid,
            VegetationRegionRuntime.ResolveLod(45.0, false, WorldStreamingRegionDetail.Full));
        Assert.Equal(VegetationLodTier.Culled,
            VegetationRegionRuntime.ResolveLod(60.0, true, WorldStreamingRegionDetail.Full));
        Assert.Equal(VegetationLodTier.Mid,
            VegetationRegionRuntime.ResolveLod(10.0, false, WorldStreamingRegionDetail.Simplified));
        Assert.Equal(VegetationLodTier.Culled,
            VegetationRegionRuntime.ResolveLod(10.0, false, WorldStreamingRegionDetail.Preload));
    }

    [Theory]
    [InlineData(VegetationPromotionReason.Proximity, 3.0, false)]
    [InlineData(VegetationPromotionReason.Scan, 20.0, false)]
    [InlineData(VegetationPromotionReason.Damage, 20.0, false)]
    [InlineData(VegetationPromotionReason.Harvest, 20.0, false)]
    [InlineData(VegetationPromotionReason.Quest, 20.0, true)]
    public void AllSpecPromotionTriggersAreSupported(
        VegetationPromotionReason reason,
        double distance,
        bool questRelevant)
    {
        Assert.True(VegetationRegionRuntime.ShouldPromote(reason, distance, questRelevant));
    }
}
