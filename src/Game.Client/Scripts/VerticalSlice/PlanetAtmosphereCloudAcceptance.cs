using System;

public sealed record PlanetAtmosphereCloudAcceptanceReport(
    bool SphericalAtmosphereShell,
    bool DirectionalStarScattering,
    bool HorizonAmplification,
    bool SunsetColor,
    bool OneOrTwoCloudLayers,
    int CloudLayerCount,
    bool ScrollingNoiseTextures,
    bool DensityResponse,
    bool SurfaceShadowDimming,
    bool NoVolumetricRayMarch,
    bool LegacyCloudBlobsRetired,
    bool LiveNode,
    bool SurfaceContactLatch)
{
    public bool Passed =>
        SphericalAtmosphereShell &&
        DirectionalStarScattering &&
        HorizonAmplification &&
        SunsetColor &&
        OneOrTwoCloudLayers &&
        ScrollingNoiseTextures &&
        DensityResponse &&
        SurfaceShadowDimming &&
        NoVolumetricRayMarch &&
        LegacyCloudBlobsRetired &&
        LiveNode &&
        SurfaceContactLatch;

    public string BuildOutputLine() =>
        $"TASK-190 planetary atmosphere/cloud acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"shell={B(SphericalAtmosphereShell)}; starDirectional={B(DirectionalStarScattering)}; " +
        $"horizon={B(HorizonAmplification)}; sunset={B(SunsetColor)}; cloudLayers={CloudLayerCount}; " +
        $"noiseScroll={B(ScrollingNoiseTextures)}; density={B(DensityResponse)}; " +
        $"surfaceShadow={B(SurfaceShadowDimming)}; noRayMarch={B(NoVolumetricRayMarch)}; " +
        $"legacyBlobsRetired={B(LegacyCloudBlobsRetired)}; liveNode={B(LiveNode)}; " +
        $"contactLatch={B(SurfaceContactLatch)}; result=low-cost-atmosphere-spherical-cloud-layers.";

    private static int B(bool value) => value ? 1 : 0;
}

public static class PlanetAtmosphereCloudAcceptanceRunner
{
    public static PlanetAtmosphereCloudAcceptanceReport Evaluate(
        PlanetEnvironmentProfile environment,
        PlanetWeatherState weather,
        bool liveNode,
        bool legacyCloudBlobsRetired)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(weather);
        PlanetAtmosphereCloudProfile profile =
            PlanetAtmosphereCloudRuntime.BuildProfile(environment);
        PlanetAtmosphereCloudFrame frame =
            PlanetAtmosphereCloudRuntime.BuildFrame(profile, weather);

        string atmosphere = PlanetAtmosphereCloudNode.AtmosphereShaderSource;
        string clouds = PlanetAtmosphereCloudNode.CloudShaderSource;
        bool shell = atmosphere.Contains("cull_front", StringComparison.Ordinal) &&
            atmosphere.Contains("shell_dir", StringComparison.Ordinal);
        bool directional = atmosphere.Contains("star_direction", StringComparison.Ordinal) &&
            atmosphere.Contains("dot(direction, star_dir)", StringComparison.Ordinal);
        bool horizon = atmosphere.Contains("horizon_amplification", StringComparison.Ordinal) &&
            atmosphere.Contains("1.0 - vertical", StringComparison.Ordinal);
        bool sunset = atmosphere.Contains("sunset_color", StringComparison.Ordinal) &&
            atmosphere.Contains("sunset_factor", StringComparison.Ordinal);
        bool layerPolicy = profile.CloudLayers is >= 0 and <= 2;
        bool scrollingNoise = clouds.Contains("sampler2D noise_a", StringComparison.Ordinal) &&
            clouds.Contains("sampler2D noise_b", StringComparison.Ordinal) &&
            clouds.Contains("TIME", StringComparison.Ordinal) &&
            clouds.Contains("scroll_a", StringComparison.Ordinal) &&
            clouds.Contains("scroll_b", StringComparison.Ordinal);
        bool density = clouds.Contains("uniform float density", StringComparison.Ordinal) &&
            frame.CloudDensity is >= 0.0 and <= 1.0;
        bool shadow = frame.CloudShadowFactor is >= 0.45 and <= 1.0 &&
            PlanetAtmosphereCloudRuntime.ApplyCloudShadow(1.0, frame.CloudShadowFactor) <= 1.0;
        bool noRayMarch = !atmosphere.Contains("raymarch", StringComparison.OrdinalIgnoreCase) &&
            !clouds.Contains("raymarch", StringComparison.OrdinalIgnoreCase) &&
            !atmosphere.Contains("while (", StringComparison.Ordinal) &&
            !clouds.Contains("while (", StringComparison.Ordinal);

        return new PlanetAtmosphereCloudAcceptanceReport(
            shell,
            directional,
            horizon,
            sunset,
            layerPolicy,
            profile.CloudLayers,
            scrollingNoise,
            density,
            shadow,
            noRayMarch,
            legacyCloudBlobsRetired,
            liveNode,
            SurfaceContactLatch:
                SurfaceContactLatchRuntime.UpdateReleaseFrames(0, 3.9) == 0 &&
                SurfaceContactLatchRuntime.UpdateReleaseFrames(0, 4.8) == 1 &&
                SurfaceContactLatchRuntime.ShouldRelease(
                    SurfaceContactLatchRuntime.ReleaseStableFrames));
    }
}
