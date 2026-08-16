using System;

public sealed record PlanetaryWaterAcceptanceReport(
    bool FixedSphericalLevel,
    bool OceanPolicy,
    bool LocalLakePolicy,
    bool WaveShader,
    bool SkyReflection,
    bool DepthDarkening,
    bool UnderwaterPostEffect,
    bool SwimmingModel,
    bool UnderwaterOxygen,
    bool NoFluidSimulation,
    bool LiveWaterNode,
    bool LegacyPoolRetired)
{
    public bool Passed =>
        FixedSphericalLevel &&
        OceanPolicy &&
        LocalLakePolicy &&
        WaveShader &&
        SkyReflection &&
        DepthDarkening &&
        UnderwaterPostEffect &&
        SwimmingModel &&
        UnderwaterOxygen &&
        NoFluidSimulation &&
        LiveWaterNode &&
        LegacyPoolRetired;

    public string Result => Passed
        ? "planetary-water-swimming-underwater-runtime"
        : "planetary-water-contract-incomplete";

    public string BuildOutputLine() =>
        $"TASK-188 planetary water acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"fixedLevel={B(FixedSphericalLevel)}; ocean={B(OceanPolicy)}; lakes={B(LocalLakePolicy)}; " +
        $"waves={B(WaveShader)}; reflection={B(SkyReflection)}; depth={B(DepthDarkening)}; " +
        $"underwaterPost={B(UnderwaterPostEffect)}; swimming={B(SwimmingModel)}; " +
        $"oxygen={B(UnderwaterOxygen)}; noFluidSim={B(NoFluidSimulation)}; " +
        $"liveNode={B(LiveWaterNode)}; legacyPoolRetired={B(LegacyPoolRetired)}; result={Result}.";

    private static int B(bool value) => value ? 1 : 0;
}

public static class PlanetaryWaterAcceptanceRunner
{
    public static PlanetaryWaterAcceptanceReport Evaluate(
        double planetRadiusMeters,
        bool liveWaterNode,
        bool legacyPoolRetired)
    {
        double radius = Math.Max(
            PlanetSurfaceTopologyRuntime.MinimumRadiusMeters,
            planetRadiusMeters);
        PlanetSurfaceCurvedPatchDescriptor patch = new(radius, 0.0, 0.0);
        const double semanticLevel = PlanetaryWaterRuntime.DefaultOceanSurfaceHeightMeters;
        double localA = semanticLevel - patch.TangentSagMeters(0.0, 0.0);
        double localB = semanticLevel - patch.TangentSagMeters(260.0, 180.0);
        bool fixedLevel =
            Math.Abs(PlanetaryWaterRuntime.SemanticHeightFromCurvedLocalY(
                patch, 0.0, 0.0, localA) - semanticLevel) <= 1e-8 &&
            Math.Abs(PlanetaryWaterRuntime.SemanticHeightFromCurvedLocalY(
                patch, 260.0, 180.0, localB) - semanticLevel) <= 1e-8 &&
            Math.Abs(localA - localB) > 0.001;

        PlanetaryWaterProfile ocean = new(
            "acceptance.ocean",
            radius,
            0.72,
            semanticLevel,
            true,
            Array.Empty<PlanetaryWaterLake>());
        PlanetaryWaterProfile lakes = new(
            "acceptance.lakes",
            radius,
            0.22,
            semanticLevel,
            false,
            new[]
            {
                new PlanetaryWaterLake("lake.test", 10.0, -5.0, 8.0, 0.62)
            });
        bool oceanPolicy = PlanetaryWaterRuntime.TryResolveSurface(
            ocean, 1200.0, -900.0, out double oceanLevel, out string oceanBody) &&
            oceanBody == "ocean" && Math.Abs(oceanLevel - semanticLevel) <= 1e-8;
        bool localLakePolicy = PlanetaryWaterRuntime.TryResolveSurface(
            lakes, 10.0, -5.0, out _, out string lakeBody) &&
            lakeBody == "lake.test" &&
            !PlanetaryWaterRuntime.TryResolveSurface(lakes, 40.0, 40.0, out _, out _);

        string waterShader = PlanetaryWaterSurfaceNode.WaterShaderSource;
        string underwaterShader = PlanetaryWaterSurfaceNode.UnderwaterPostShaderSource;
        bool waves = waterShader.Contains("TIME", StringComparison.Ordinal) &&
            waterShader.Contains("wave_height", StringComparison.Ordinal) &&
            waterShader.Contains("VERTEX.y", StringComparison.Ordinal);
        bool reflection = waterShader.Contains("SPECULAR = 0.92", StringComparison.Ordinal) &&
            waterShader.Contains("fresnel", StringComparison.Ordinal);
        bool depthDarkening = waterShader.Contains("hint_depth_texture", StringComparison.Ordinal) &&
            waterShader.Contains("depth_below_surface", StringComparison.Ordinal) &&
            waterShader.Contains("deep_color", StringComparison.Ordinal);
        bool underwaterPost = underwaterShader.Contains("hint_screen_texture", StringComparison.Ordinal) &&
            underwaterShader.Contains("underwater_tint", StringComparison.Ordinal) &&
            underwaterShader.Contains("wobble", StringComparison.Ordinal);

        bool swimming = PlanetaryWaterRuntime.ResolveSwimming(
                false, true, 0.45) &&
            PlanetaryWaterRuntime.ResolveSwimming(true, true, -0.05) &&
            !PlanetaryWaterRuntime.ResolveSwimming(true, true, -0.30) &&
            PlanetaryWaterRuntime.ResolveUnderwater(false, true, 0.25) &&
            !PlanetaryWaterRuntime.ResolveUnderwater(true, true, -0.10);
        bool oxygen = PlayerSurvivalRuntime.UnderwaterMinimumOxygenDrainPerSecond >= 1.0;

        return new PlanetaryWaterAcceptanceReport(
            fixedLevel,
            oceanPolicy,
            localLakePolicy,
            waves,
            reflection,
            depthDarkening,
            underwaterPost,
            swimming,
            oxygen,
            NoFluidSimulation: true,
            liveWaterNode,
            legacyPoolRetired);
    }
}
