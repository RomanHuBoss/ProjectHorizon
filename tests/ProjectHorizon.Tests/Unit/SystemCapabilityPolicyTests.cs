namespace ProjectHorizon.Tests.Unit;

public sealed class SystemCapabilityPolicyTests
{
    private const long GiB = 1024L * 1024L * 1024L;

    [Fact]
    public void MinimumPlayerConfigurationMapsToLow()
    {
        SystemCapabilityEvaluation value = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 4, 8 * GiB, true, true, true, false, true,
            4 * GiB, true, 20 * GiB, true, true, true));
        Assert.True(value.MinimumSatisfied);
        Assert.False(value.RecommendedSatisfied);
        Assert.Equal(SystemCapabilityTier.Minimum, value.Tier);
        Assert.Equal(GraphicsQualityProfile.Low, value.RecommendedGraphicsProfile);
    }

    [Fact]
    public void RecommendedConfigurationMapsToMedium()
    {
        SystemCapabilityEvaluation value = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 8, 32 * GiB, true, true, true, false, true,
            8 * GiB, true, 40 * GiB, true, true, true));
        Assert.True(value.RecommendedSatisfied);
        Assert.Equal(SystemCapabilityTier.Recommended, value.Tier);
        Assert.Equal(GraphicsQualityProfile.Medium, value.RecommendedGraphicsProfile);
    }

    [Fact]
    public void CompatibilityRendererMapsToCompatibilityProfile()
    {
        SystemCapabilityEvaluation value = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 4, 8 * GiB, true, true, false, true, false,
            0, false, 25 * GiB, true, false, false));
        Assert.True(value.MinimumSatisfied);
        Assert.Equal(GraphicsQualityProfile.Compatibility, value.RecommendedGraphicsProfile);
    }

    [Fact]
    public void UnknownSsdAndVramDoNotInventHardFailure()
    {
        SystemCapabilityEvaluation value = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 6, 16 * GiB, true, true, true, false, true,
            0, false, 35 * GiB, true, false, false));
        Assert.True(value.MinimumSatisfied);
        Assert.False(value.MinimumEvidenceComplete);
    }

    [Fact]
    public void KnownMinimumViolationIsUnsupported()
    {
        SystemCapabilityEvaluation value = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 2, 4 * GiB, true, false, false, false, true,
            2 * GiB, true, 10 * GiB, true, false, true));
        Assert.False(value.MinimumSatisfied);
        Assert.Equal(SystemCapabilityTier.Unsupported, value.Tier);
        Assert.Equal(GraphicsQualityProfile.Compatibility, value.RecommendedGraphicsProfile);
    }
}
