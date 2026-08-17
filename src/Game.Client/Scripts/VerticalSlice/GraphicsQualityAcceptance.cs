using System;

public sealed record GraphicsQualityAcceptanceReport(
    bool ProfilesComplete,
    bool LowSurfaceRange,
    bool ShadowHierarchy,
    bool CloudHierarchy,
    bool WaterHierarchy,
    bool CompatibilitySimplified,
    bool UserPersistence,
    bool RendererOverride,
    bool AdaptiveCeiling,
    bool LiveHooks,
    GraphicsQualityProfile EffectiveProfile,
    int LiveCloudLimit)
{
    public bool Passed => ProfilesComplete && LowSurfaceRange && ShadowHierarchy &&
        CloudHierarchy && WaterHierarchy && CompatibilitySimplified &&
        UserPersistence && RendererOverride && AdaptiveCeiling && LiveHooks;

    public string BuildOutputLine() =>
        $"TASK-202 graphics quality acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"profiles={(ProfilesComplete ? 4 : 0)}/4; lowSurface={(LowSurfaceRange ? 1 : 0)}; " +
        $"shadows={(ShadowHierarchy ? 1 : 0)}; clouds={(CloudHierarchy ? 1 : 0)}; " +
        $"water={(WaterHierarchy ? 1 : 0)}; compatibility={(CompatibilitySimplified ? 1 : 0)}; " +
        $"persistence={(UserPersistence ? 1 : 0)}; rendererOverride={(RendererOverride ? 1 : 0)}; " +
        $"adaptiveCeiling={(AdaptiveCeiling ? 1 : 0)}; live={(LiveHooks ? 1 : 0)}; " +
        $"effective={EffectiveProfile}; cloudLimit={LiveCloudLimit}; " +
        "result=section-26.4-low-medium-high-compatibility-runtime-scalability.";
}

public static class GraphicsQualityAcceptanceRunner
{
    public static GraphicsQualityAcceptanceReport Evaluate(
        GraphicsQualityProfile requested,
        GraphicsQualityProfile effective,
        bool compatibilityRenderer,
        PlanetAtmosphereCloudNode? cloudNode,
        PlanetaryWaterSurfaceNode? waterNode,
        bool shadowsEnabled,
        double effectiveVegetationDistanceScale,
        double vegetationDensityScale)
    {
        GraphicsQualitySettings low = GraphicsQualityProfilePolicy.Low;
        GraphicsQualitySettings medium = GraphicsQualityProfilePolicy.Medium;
        GraphicsQualitySettings high = GraphicsQualityProfilePolicy.High;
        GraphicsQualitySettings compatibility = GraphicsQualityProfilePolicy.Compatibility;

        bool profiles = new[] { low, medium, high, compatibility }.Length == 4 &&
            low.Profile == GraphicsQualityProfile.Low &&
            medium.Profile == GraphicsQualityProfile.Medium &&
            high.Profile == GraphicsQualityProfile.High &&
            compatibility.Profile == GraphicsQualityProfile.Compatibility;
        bool lowRange = low.SurfaceDistanceScale is >= 0.50 and <= 0.60 &&
            low.VegetationDistanceScale is >= 0.50 and <= 0.60 &&
            low.VegetationDensityScale < medium.VegetationDensityScale &&
            high.VegetationDensityScale > medium.VegetationDensityScale;
        bool shadows = !compatibility.HeavyEffectsAllowed &&
            compatibility.ShadowQuality == GraphicsShadowQuality.Disabled &&
            low.ShadowMaxDistanceMeters < medium.ShadowMaxDistanceMeters &&
            medium.ShadowMaxDistanceMeters < high.ShadowMaxDistanceMeters;
        bool clouds = low.MaximumCloudLayers == 1 &&
            medium.MaximumCloudLayers == 2 && high.MaximumCloudLayers == 2 &&
            compatibility.MaximumCloudLayers == 1;
        bool water = low.WaterWaveScale < medium.WaterWaveScale &&
            high.WaterWaveScale > medium.WaterWaveScale &&
            compatibility.WaterDepthScale < low.WaterDepthScale;
        bool compatibilitySimple = compatibility.SimplifiedShaders &&
            !compatibility.GlowEnabled &&
            compatibility.ParticleAmountScale < low.ParticleAmountScale;
        bool persistence = GraphicsQualityProfilePolicy.IsValid(requested);
        bool rendererOverride = !compatibilityRenderer ||
            effective == GraphicsQualityProfile.Compatibility;
        RuntimePerformanceQualitySettings perfCritical =
            RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                GraphicsQualityProfilePolicy.ResolvePerformanceBudgetProfile(effective),
                RuntimePerformanceQualityState.Critical);
        GraphicsQualitySettings baseSettings = GraphicsQualityProfilePolicy.Get(effective);
        bool adaptiveCeiling = effectiveVegetationDistanceScale <=
                baseSettings.VegetationDistanceScale + 0.0001 &&
            perfCritical.VegetationDistanceScale <= 1.0;
        bool live = cloudNode is not null && waterNode is not null &&
            cloudNode.GraphicsQualityConfigured && waterNode.GraphicsQualityConfigured &&
            vegetationDensityScale > 0.0 &&
            (baseSettings.ShadowQuality == GraphicsShadowQuality.Disabled || shadowsEnabled);
        int cloudLimit = cloudNode?.EffectiveCloudLayerLimit ?? 0;

        return new GraphicsQualityAcceptanceReport(
            profiles,
            lowRange,
            shadows,
            clouds,
            water,
            compatibilitySimple,
            persistence,
            rendererOverride,
            adaptiveCeiling,
            live,
            effective,
            cloudLimit);
    }
}
