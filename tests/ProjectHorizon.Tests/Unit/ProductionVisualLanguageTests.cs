namespace ProjectHorizon.Tests.Unit;

public sealed class ProductionVisualLanguageTests
{
    [Fact]
    public void Task180_ProductionVisualContractPassesAtDeclaredBudgets()
    {
        ProductionVisualLanguageAcceptanceReport report =
            ProductionVisualLanguageAcceptanceRunner.Evaluate(
                playerExteriorParts: 11,
                cockpitDetailParts: 9,
                stationDetailParts: 6,
                npcShipDetailParts: 9,
                planetMaterialVariants: 6,
                semanticMaterialProfiles: 12,
                shipCollisionPreserved: true,
                stationCollisionPreserved: true,
                visualOnlyDetails: true);

        Assert.True(report.Passed, report.Result);
        Assert.Equal(6, report.PlanetMaterialVariants);
        Assert.True(report.VisualOnlyDetails);
        Assert.True(report.ShipCollisionPreserved);
        Assert.True(report.StationCollisionPreserved);
    }

    [Theory]
    [InlineData(10, 9, 6, 9, 6, 12)]
    [InlineData(11, 8, 6, 9, 6, 12)]
    [InlineData(11, 9, 5, 9, 6, 12)]
    [InlineData(11, 9, 6, 8, 6, 12)]
    [InlineData(11, 9, 6, 9, 5, 12)]
    [InlineData(11, 9, 6, 9, 6, 0)]
    public void Task180_RejectsUnderBudgetVisualContracts(
        int player,
        int cockpit,
        int station,
        int npc,
        int materials,
        int semanticProfiles)
    {
        ProductionVisualLanguageAcceptanceReport report =
            ProductionVisualLanguageAcceptanceRunner.Evaluate(
                player,
                cockpit,
                station,
                npc,
                materials,
                semanticProfiles,
                shipCollisionPreserved: true,
                stationCollisionPreserved: true,
                visualOnlyDetails: true);

        Assert.False(report.Passed);
    }

    [Fact]
    public void Task180_RejectsGameplayCollisionMutation()
    {
        ProductionVisualLanguageAcceptanceReport report =
            ProductionVisualLanguageAcceptanceRunner.Evaluate(
                11, 9, 6, 9, 6, 12,
                shipCollisionPreserved: false,
                stationCollisionPreserved: true,
                visualOnlyDetails: true);

        Assert.False(report.Passed);
        Assert.False(report.ShipCollisionPreserved);
    }
}
