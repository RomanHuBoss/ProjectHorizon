using Xunit;

public sealed class ProductionAssetPipelineTests
{
    [Fact]
    public void CompleteProductionAssetPipelinePasses()
    {
        ProductionAssetPipelineAcceptanceReport report =
            ProductionAssetPipelineAcceptanceRunner.Evaluate(
                assetFamilies: 3,
                glbAssets: 9,
                lodChains: 3,
                mountMarkers: 14,
                playerAssetLoaded: true,
                stationAssetLoaded: true,
                npcAssetsLoaded: true,
                collisionSeparated: true,
                legacyFallbackHidden: true,
                lodControllerPresent: true);

        Assert.True(report.Passed);
        Assert.Contains("TASK-184 production asset pipeline acceptance PASS", report.BuildOutputLine());
    }

    [Fact]
    public void MissingLodChainFails()
    {
        ProductionAssetPipelineAcceptanceReport report =
            ProductionAssetPipelineAcceptanceRunner.Evaluate(
                3, 9, 2, 14, true, true, true, true, true, true);

        Assert.False(report.Passed);
    }

    [Fact]
    public void CollisionInsideProductionAssetFails()
    {
        ProductionAssetPipelineAcceptanceReport report =
            ProductionAssetPipelineAcceptanceRunner.Evaluate(
                3, 9, 3, 14, true, true, true, false, true, true);

        Assert.False(report.Passed);
    }
}
