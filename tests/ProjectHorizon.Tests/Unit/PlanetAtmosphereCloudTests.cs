namespace ProjectHorizon.Tests.Unit;

public sealed class PlanetAtmosphereCloudTests
{
    [Fact]
    public void CloudLayerPolicyIsBoundedToTwoAndAtmosphereRequiresDensity()
    {
        PlanetEnvironmentProfile environment = CreateEnvironment(
            atmosphereDensity: 1.10,
            cloudLayers: 2,
            cloudDensity: 0.68);
        PlanetAtmosphereCloudProfile profile =
            PlanetAtmosphereCloudRuntime.BuildProfile(environment);

        Assert.True(profile.AtmosphereEnabled);
        Assert.Equal(2, profile.CloudLayers);
        Assert.InRange(profile.HorizonAmplification, 1.25, 2.7);
    }

    [Fact]
    public void WeatherControlsCloudDensityAndSurfaceShadowWithoutRayMarching()
    {
        PlanetEnvironmentProfile environment = CreateEnvironment(1.10, 2, 0.68);
        PlanetAtmosphereCloudProfile profile =
            PlanetAtmosphereCloudRuntime.BuildProfile(environment);
        PlanetWeatherState weather = PlanetWeatherRuntime.BuildState(environment, 12.0);
        PlanetAtmosphereCloudFrame frame =
            PlanetAtmosphereCloudRuntime.BuildFrame(profile, weather);

        Assert.InRange(frame.CloudDensity, 0.0, 1.0);
        Assert.InRange(frame.CloudShadowFactor, 0.45, 1.0);
        Assert.False(PlanetAtmosphereCloudNode.AtmosphereShaderSource.Contains(
            "raymarch",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains("TIME", PlanetAtmosphereCloudNode.CloudShaderSource);
    }

    [Fact]
    public void SurfaceContactLatchRequiresStableClearanceBeforeRecovery()
    {
        int frames = 0;
        for (int index = 0; index < SurfaceContactLatchRuntime.ReleaseStableFrames - 1; index++)
        {
            frames = SurfaceContactLatchRuntime.UpdateReleaseFrames(frames, 4.8);
        }
        Assert.False(SurfaceContactLatchRuntime.ShouldRelease(frames));
        frames = SurfaceContactLatchRuntime.UpdateReleaseFrames(frames, 4.8);
        Assert.True(SurfaceContactLatchRuntime.ShouldRelease(frames));
        Assert.Equal(0, SurfaceContactLatchRuntime.UpdateReleaseFrames(frames, 4.0));
    }

    [Fact]
    public void AcceptanceCoversAtmosphereCloudsAndContactLatch()
    {
        PlanetEnvironmentProfile environment = CreateEnvironment(1.10, 2, 0.68);
        PlanetWeatherState weather = PlanetWeatherRuntime.BuildState(environment, 12.0);
        PlanetAtmosphereCloudAcceptanceReport report =
            PlanetAtmosphereCloudAcceptanceRunner.Evaluate(
                environment,
                weather,
                liveNode: true,
                legacyCloudBlobsRetired: true);
        Assert.True(report.Passed, report.BuildOutputLine());
        Assert.True(report.NoVolumetricRayMarch);
        Assert.True(report.SurfaceContactLatch);
    }

    private static PlanetEnvironmentProfile CreateEnvironment(
        double atmosphereDensity,
        int cloudLayers,
        double cloudDensity) => new(
            "planet.test",
            "temperate",
            true,
            44.3,
            1.0,
            14.0,
            22.0,
            0.55,
            atmosphereDensity,
            0.44,
            cloudLayers,
            cloudDensity,
            0.0,
            0.0,
            new PlanetEnvironmentColor(0.22, 0.48, 0.82),
            new PlanetEnvironmentColor(0.96, 0.32, 0.12),
            new PlanetEnvironmentColor(0.08, 0.28, 0.46),
            new[] { "biome.temperate_plain" },
            1902026);
}
