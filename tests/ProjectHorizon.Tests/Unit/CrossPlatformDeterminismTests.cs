using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Unit;

public sealed class CrossPlatformDeterminismTests
{
    private static PlanetEnvironmentRuntime EnvironmentRuntime() =>
        new(RepositoryFixture.PlanetEnvironments, RepositoryFixture.Ecology);

    [Fact]
    public void PlayerPlatformPolicyCoversWindowsAndLinuxX64()
    {
        Assert.Equal(
            ProjectHorizonPlayerPlatform.WindowsX64,
            CrossPlatformDeterminismPolicy.ClassifyPlatform("Windows", true));
        Assert.Equal(
            ProjectHorizonPlayerPlatform.LinuxX64,
            CrossPlatformDeterminismPolicy.ClassifyPlatform("Linux", true));
        Assert.Equal(
            ProjectHorizonPlayerPlatform.Unsupported,
            CrossPlatformDeterminismPolicy.ClassifyPlatform("Windows", false));
        Assert.Equal(2, CrossPlatformDeterminismPolicy.RequiredPlatformFamilies);
    }

    [Fact]
    public void SameSeedReplaysSameCanonicalWorldSignature()
    {
        PlanetEnvironmentRuntime environment = EnvironmentRuntime();
        string first = CrossPlatformDeterminismRuntime.BuildCanonicalWorldSignature(
            GalaxyNavigationRuntime.DefaultUniverseSeed,
            environment,
            RepositoryFixture.Pois);
        string second = CrossPlatformDeterminismRuntime.BuildCanonicalWorldSignature(
            GalaxyNavigationRuntime.DefaultUniverseSeed,
            environment,
            RepositoryFixture.Pois);

        Assert.Equal(first, second);
        Assert.Matches("^[a-f0-9]{64}$", first);
    }

    [Fact]
    public void CanonicalWorldSignatureIsCultureInvariant()
    {
        CrossPlatformDeterminismReport report = CrossPlatformDeterminismRuntime.Run(
            EnvironmentRuntime(),
            RepositoryFixture.Pois);

        Assert.True(report.CultureInvariant);
        Assert.Equal(3, report.CulturesTested);
        Assert.True(report.SurfaceSignatureStable);
    }

    [Fact]
    public void GeneratorChangesRemainBoundToExplicitVersioning()
    {
        Assert.True(CrossPlatformDeterminismPolicy.GeneratorVersionBumpRequiredForWorldChanges);
        Assert.Equal(ProjectHorizonGenerator.Version, GalaxyNavigationRuntime.GeneratorVersion);
        Assert.Equal(3, ProjectHorizonGenerator.Version);
    }

    [Fact]
    public void SinglePlayerIsOfflineFirstByPolicy()
    {
        Assert.False(CrossPlatformDeterminismPolicy.SinglePlayerRequiresInternet);
        Assert.True(CrossPlatformDeterminismPolicy.CloudFeaturesOptional);
        Assert.Equal(0, CrossPlatformDeterminismPolicy.PermittedProductionNetworkDependencies);
    }
}
