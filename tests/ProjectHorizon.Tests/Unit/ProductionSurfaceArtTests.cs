using Xunit;

public sealed class ProductionSurfaceArtTests
{
    [Fact]
    public void CompleteProductionSurfaceArtPasses()
    {
        ProductionSurfaceArtAcceptanceReport report = ProductionSurfaceArtAcceptanceRunner.Evaluate(
            4, true, 9, 10, 30, 24, 0, true);
        Assert.True(report.Passed);
    }

    [Fact]
    public void MissingAtlasMapFails()
    {
        ProductionSurfaceArtAcceptanceReport report = ProductionSurfaceArtAcceptanceRunner.Evaluate(
            3, true, 9, 10, 30, 24, 0, true);
        Assert.False(report.Passed);
    }

    [Fact]
    public void ResourceFamilyRegressionFails()
    {
        ProductionSurfaceArtAcceptanceReport report = ProductionSurfaceArtAcceptanceRunner.Evaluate(
            4, true, 9, 9, 27, 24, 0, true);
        Assert.False(report.Passed);
    }

    [Fact]
    public void ResourceFallbackStillFails()
    {
        ProductionSurfaceArtAcceptanceReport report = ProductionSurfaceArtAcceptanceRunner.Evaluate(
            4, true, 9, 10, 30, 24, 1, true);
        Assert.False(report.Passed);
    }
}
