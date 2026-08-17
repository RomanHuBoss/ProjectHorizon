using Xunit;

public sealed class ProductionModelArtTests
{
    [Fact]
    public void CompleteProductionModelArtPasses()
    {
        ProductionModelArtAcceptanceReport report = ProductionModelArtAcceptanceRunner.Evaluate(
            49, 26, 178, 5, 15, 24, 0, true, true, true, true);
        Assert.True(report.Passed);
    }

    [Fact]
    public void ResourceFallbackFails()
    {
        ProductionModelArtAcceptanceReport report = ProductionModelArtAcceptanceRunner.Evaluate(
            49, 26, 178, 5, 15, 24, 1, true, true, true, true);
        Assert.False(report.Passed);
    }

    [Fact]
    public void DetailedSignaturesAreRequired()
    {
        ProductionModelArtAcceptanceReport report = ProductionModelArtAcceptanceRunner.Evaluate(
            49, 26, 178, 5, 15, 24, 0, false, true, true, true);
        Assert.False(report.Passed);
    }
}
