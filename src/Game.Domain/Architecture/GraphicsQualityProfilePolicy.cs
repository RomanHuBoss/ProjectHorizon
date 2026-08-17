using System;

public enum GraphicsQualityProfile
{
    Low = 0,
    Medium = 1,
    High = 2,
    Compatibility = 3
}

public enum GraphicsShadowQuality
{
    Disabled = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public sealed record GraphicsQualitySettings(
    GraphicsQualityProfile Profile,
    double VegetationDensityScale,
    double VegetationDistanceScale,
    double SurfaceDistanceScale,
    GraphicsShadowQuality ShadowQuality,
    double ShadowMaxDistanceMeters,
    int MaximumCloudLayers,
    double SecondaryCloudOpacityScale,
    double AtmosphereQualityScale,
    double WaterWaveScale,
    double WaterDepthScale,
    double UnderwaterDistortionScale,
    bool GlowEnabled,
    double GlowIntensity,
    double ParticleAmountScale,
    bool SimplifiedShaders,
    bool HeavyEffectsAllowed);

/// <summary>
/// Technical Specification section 26.4 graphics presets. These values are
/// presentation ceilings only; gameplay, collision and simulation authority are
/// intentionally outside the profile contract.
/// </summary>
public static class GraphicsQualityProfilePolicy
{
    public static GraphicsQualitySettings Low { get; } = new(
        GraphicsQualityProfile.Low,
        VegetationDensityScale: 0.55,
        VegetationDistanceScale: 0.58,
        SurfaceDistanceScale: 0.58,
        ShadowQuality: GraphicsShadowQuality.Low,
        ShadowMaxDistanceMeters: 140.0,
        MaximumCloudLayers: 1,
        SecondaryCloudOpacityScale: 0.0,
        AtmosphereQualityScale: 0.72,
        WaterWaveScale: 0.55,
        WaterDepthScale: 0.55,
        UnderwaterDistortionScale: 0.65,
        GlowEnabled: false,
        GlowIntensity: 0.0,
        ParticleAmountScale: 0.45,
        SimplifiedShaders: false,
        HeavyEffectsAllowed: false);

    public static GraphicsQualitySettings Medium { get; } = new(
        GraphicsQualityProfile.Medium,
        VegetationDensityScale: 0.85,
        VegetationDistanceScale: 1.00,
        SurfaceDistanceScale: 1.00,
        ShadowQuality: GraphicsShadowQuality.Medium,
        ShadowMaxDistanceMeters: 320.0,
        MaximumCloudLayers: 2,
        SecondaryCloudOpacityScale: 1.0,
        AtmosphereQualityScale: 1.00,
        WaterWaveScale: 1.00,
        WaterDepthScale: 1.00,
        UnderwaterDistortionScale: 1.00,
        GlowEnabled: true,
        GlowIntensity: 0.14,
        ParticleAmountScale: 0.80,
        SimplifiedShaders: false,
        HeavyEffectsAllowed: true);

    public static GraphicsQualitySettings High { get; } = new(
        GraphicsQualityProfile.High,
        VegetationDensityScale: 1.00,
        VegetationDistanceScale: 1.18,
        SurfaceDistanceScale: 1.20,
        ShadowQuality: GraphicsShadowQuality.High,
        ShadowMaxDistanceMeters: 480.0,
        MaximumCloudLayers: 2,
        SecondaryCloudOpacityScale: 1.0,
        AtmosphereQualityScale: 1.12,
        WaterWaveScale: 1.15,
        WaterDepthScale: 1.12,
        UnderwaterDistortionScale: 1.10,
        GlowEnabled: true,
        GlowIntensity: 0.22,
        ParticleAmountScale: 1.00,
        SimplifiedShaders: false,
        HeavyEffectsAllowed: true);

    public static GraphicsQualitySettings Compatibility { get; } = new(
        GraphicsQualityProfile.Compatibility,
        VegetationDensityScale: 0.45,
        VegetationDistanceScale: 0.50,
        SurfaceDistanceScale: 0.50,
        ShadowQuality: GraphicsShadowQuality.Disabled,
        ShadowMaxDistanceMeters: 0.0,
        MaximumCloudLayers: 1,
        SecondaryCloudOpacityScale: 0.0,
        AtmosphereQualityScale: 0.55,
        WaterWaveScale: 0.35,
        WaterDepthScale: 0.35,
        UnderwaterDistortionScale: 0.40,
        GlowEnabled: false,
        GlowIntensity: 0.0,
        ParticleAmountScale: 0.30,
        SimplifiedShaders: true,
        HeavyEffectsAllowed: false);

    public static GraphicsQualitySettings Get(GraphicsQualityProfile profile) => profile switch
    {
        GraphicsQualityProfile.Low => Low,
        GraphicsQualityProfile.High => High,
        GraphicsQualityProfile.Compatibility => Compatibility,
        _ => Medium
    };

    public static RuntimePerformanceProfile ResolvePerformanceBudgetProfile(
        GraphicsQualityProfile profile) =>
        profile is GraphicsQualityProfile.Low or GraphicsQualityProfile.Compatibility
            ? RuntimePerformanceProfile.Low
            : RuntimePerformanceProfile.Medium;

    public static bool IsValid(GraphicsQualityProfile profile) =>
        Enum.IsDefined(typeof(GraphicsQualityProfile), profile);
}
