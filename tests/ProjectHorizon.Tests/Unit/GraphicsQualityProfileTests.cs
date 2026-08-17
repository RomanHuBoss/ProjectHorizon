namespace ProjectHorizon.Tests.Unit;

public sealed class GraphicsQualityProfileTests
{
    [Fact]
    public void LowUsesFiftyToSixtyPercentSurfaceAndVegetationDistance()
    {
        GraphicsQualitySettings low = GraphicsQualityProfilePolicy.Low;
        Assert.InRange(low.SurfaceDistanceScale, 0.50, 0.60);
        Assert.InRange(low.VegetationDistanceScale, 0.50, 0.60);
        Assert.True(low.VegetationDensityScale < GraphicsQualityProfilePolicy.Medium.VegetationDensityScale);
    }

    [Fact]
    public void HighExceedsMediumPresentationQuality()
    {
        GraphicsQualitySettings medium = GraphicsQualityProfilePolicy.Medium;
        GraphicsQualitySettings high = GraphicsQualityProfilePolicy.High;
        Assert.True(high.VegetationDensityScale > medium.VegetationDensityScale);
        Assert.True(high.VegetationDistanceScale > medium.VegetationDistanceScale);
        Assert.True(high.SurfaceDistanceScale > medium.SurfaceDistanceScale);
        Assert.True(high.ShadowMaxDistanceMeters > medium.ShadowMaxDistanceMeters);
        Assert.True(high.AtmosphereQualityScale > medium.AtmosphereQualityScale);
        Assert.True(high.WaterWaveScale > medium.WaterWaveScale);
        Assert.True(high.ParticleAmountScale > medium.ParticleAmountScale);
    }

    [Fact]
    public void CompatibilityDisablesHeavyEffectsAndUsesSimplifiedShaders()
    {
        GraphicsQualitySettings compatibility = GraphicsQualityProfilePolicy.Compatibility;
        Assert.Equal(GraphicsShadowQuality.Disabled, compatibility.ShadowQuality);
        Assert.False(compatibility.GlowEnabled);
        Assert.Equal(1, compatibility.MaximumCloudLayers);
        Assert.True(compatibility.SimplifiedShaders);
        Assert.False(compatibility.HeavyEffectsAllowed);
        Assert.True(compatibility.WaterDepthScale < GraphicsQualityProfilePolicy.Low.WaterDepthScale);
    }

    [Theory]
    [InlineData(GraphicsQualityProfile.Low, RuntimePerformanceProfile.Low)]
    [InlineData(GraphicsQualityProfile.Compatibility, RuntimePerformanceProfile.Low)]
    [InlineData(GraphicsQualityProfile.Medium, RuntimePerformanceProfile.Medium)]
    [InlineData(GraphicsQualityProfile.High, RuntimePerformanceProfile.Medium)]
    public void PerformanceBudgetMappingRespectsProfileCeiling(
        GraphicsQualityProfile profile,
        RuntimePerformanceProfile expected)
    {
        Assert.Equal(expected, GraphicsQualityProfilePolicy.ResolvePerformanceBudgetProfile(profile));
    }

    [Fact]
    public void WorldStreamingPresentationScalePreservesNormativeDefault()
    {
        WorldStreamingObserverSample observer = new(
            0.0, 0.0, 25.0, 0.0, WorldStreamingTravelMode.OnFoot);
        WorldStreamingPlan normative = WorldStreamingRuntime.BuildPlan(observer);
        WorldStreamingPlan low = WorldStreamingRuntime.BuildPlan(
            observer,
            GraphicsQualityProfilePolicy.Low.SurfaceDistanceScale);
        WorldStreamingPlan high = WorldStreamingRuntime.BuildPlan(
            observer,
            GraphicsQualityProfilePolicy.High.SurfaceDistanceScale);

        Assert.Equal(WorldStreamingRuntime.OnFootFullDetailRadiusMeters,
            normative.FullDetailRadiusMeters);
        Assert.Equal(1160.0, low.FullDetailRadiusMeters, 3);
        Assert.Equal(2400.0, high.FullDetailRadiusMeters, 3);
        Assert.True(low.FullCount < normative.FullCount);
        Assert.True(high.FullCount > normative.FullCount);
    }
}
