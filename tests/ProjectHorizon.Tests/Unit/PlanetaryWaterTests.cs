namespace ProjectHorizon.Tests.Unit;

public sealed class PlanetaryWaterTests
{
    [Fact]
    public void WaterDepthStateUsesHysteresis()
    {
        Assert.True(PlanetaryWaterRuntime.ResolveSwimming(false, true, 0.20));
        Assert.True(PlanetaryWaterRuntime.ResolveSwimming(true, true, -0.05));
        Assert.False(PlanetaryWaterRuntime.ResolveSwimming(true, true, -0.30));
        Assert.True(PlanetaryWaterRuntime.ResolveUnderwater(false, true, 0.20));
        Assert.False(PlanetaryWaterRuntime.ResolveUnderwater(true, true, -0.10));
    }

    [Fact]
    public void CurvedWaterKeepsOneSemanticRadialLevel()
    {
        PlanetSurfaceCurvedPatchDescriptor patch = new(44_300.0, 0.0, 0.0);
        const double level = PlanetaryWaterRuntime.DefaultOceanSurfaceHeightMeters;
        double localY = level - patch.TangentSagMeters(300.0, 240.0);
        double semantic = PlanetaryWaterRuntime.SemanticHeightFromCurvedLocalY(
            patch, 300.0, 240.0, localY);
        Assert.InRange(Math.Abs(semantic - level), 0.0, 1e-8);
    }

    [Fact]
    public void AcceptanceCoversWaterRenderingSwimmingAndNoFluidSimulation()
    {
        PlanetaryWaterAcceptanceReport report =
            PlanetaryWaterAcceptanceRunner.Evaluate(
                44_300.0,
                liveWaterNode: true,
                legacyPoolRetired: true);
        Assert.True(report.Passed, report.Result);
        Assert.True(report.WaveShader);
        Assert.True(report.UnderwaterPostEffect);
        Assert.True(report.NoFluidSimulation);
    }
}
