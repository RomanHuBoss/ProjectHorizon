namespace ProjectHorizon.Tests.Unit;

public sealed class GalaxyExpeditionTests
{
    [Fact]
    public void ExpeditionRequiresOneHundredDistinctSystems()
    {
        Assert.Equal(100, GalaxyExpeditionRuntime.RequiredDistinctSystems);
        Assert.True(GalaxyNavigationRuntime.MaximumVisitedSystems >=
            GalaxyExpeditionRuntime.RequiredDistinctSystems);
    }

    [Fact]
    public void NeighborSectorJumpFitsValidationRange()
    {
        GalaxyNavigationRuntime runtime = new();
        GalaxySystemDefinition left = runtime.GenerateSystem(17, 0, 0);
        GalaxySystemDefinition right = runtime.GenerateSystem(18, 0, 0);
        Assert.InRange(
            GalaxyNavigationRuntime.Distance(left, right),
            0.001,
            GalaxyExpeditionRuntime.ValidationJumpRangeLightYears);
    }

    [Fact]
    public void GeneratedSystemSignatureIsDeterministic()
    {
        GalaxyNavigationRuntime first = new();
        GalaxyNavigationRuntime second = new();
        GalaxySystemDefinition left = first.GenerateSystem(37, -4, 2);
        GalaxySystemDefinition right = second.GenerateSystem(37, -4, 2);
        Assert.Equal(
            GalaxyExpeditionRuntime.BuildSignature(left),
            GalaxyExpeditionRuntime.BuildSignature(right));
    }

    [Fact]
    public void ExpeditionDoesNotRequireWholeGalaxyResidency()
    {
        Assert.Equal(1, GalaxyExpeditionRuntime.MaximumDetailedSystemResidency);
        Assert.InRange(
            GalaxyExpeditionRuntime.MaximumDefinitionReferencesDuringJump,
            1,
            2);
    }
}
