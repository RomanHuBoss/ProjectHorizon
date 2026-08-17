using Xunit;

public sealed class ProductionArtRecoveryTests
{
    [Fact]
    public void CorrectedVisualRecoveryPasses()
    {
        ProductionArtRecoveryAcceptanceReport report = ProductionArtRecoveryAcceptanceRunner.Evaluate(
            9, 30, 24, 0, 0.70f, 1.43f, 1.35f, true);
        Assert.True(report.Passed);
    }

    [Fact]
    public void DarkPrimaryHullFails()
    {
        ProductionArtRecoveryAcceptanceReport report = ProductionArtRecoveryAcceptanceRunner.Evaluate(
            9, 30, 24, 0, 0.22f, 1.43f, 1.35f, true);
        Assert.False(report.Passed);
    }

    [Fact]
    public void PancakeCrystalFails()
    {
        ProductionArtRecoveryAcceptanceReport report = ProductionArtRecoveryAcceptanceRunner.Evaluate(
            9, 30, 24, 0, 0.70f, 0.72f, 1.35f, true);
        Assert.False(report.Passed);
    }

    [Fact]
    public void FlatIceFails()
    {
        ProductionArtRecoveryAcceptanceReport report = ProductionArtRecoveryAcceptanceRunner.Evaluate(
            9, 30, 24, 0, 0.70f, 1.43f, 0.75f, true);
        Assert.False(report.Passed);
    }
}
