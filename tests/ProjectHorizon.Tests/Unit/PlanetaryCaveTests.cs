namespace ProjectHorizon.Tests.Unit;

public sealed class PlanetaryCaveTests
{
    [Fact]
    public void CavePlanIsDeterministicAndPrefabOnly()
    {
        PlanetaryCavePlan first = PlanetaryCaveRuntime.BuildPlan(
            "planet.vertical_slice",
            "poi.instance.cave_entrance",
            1082026);
        PlanetaryCavePlan second = PlanetaryCaveRuntime.BuildPlan(
            "planet.vertical_slice",
            "poi.instance.cave_entrance",
            1082026);

        Assert.Equal(first, second);
        Assert.False(first.GlobalProceduralCaveNetwork);
        Assert.False(first.TerrainDeformationEnabled);
        Assert.Equal(PlanetaryCaveRuntime.DepositsPerCave, first.Deposits.Count);
        Assert.True(first.Archetype.InteriorDepthMeters >=
            PlanetaryCaveRuntime.MinimumInteriorDepthMeters);
    }

    [Fact]
    public void CaveDepositsUseStableUniquePersistentIds()
    {
        PlanetaryCavePlan plan = PlanetaryCaveRuntime.BuildPlan(
            "planet.vertical_slice",
            "poi.instance.cave_entrance",
            1082026);

        Assert.Equal(
            plan.Deposits.Count,
            plan.Deposits.Select(deposit => deposit.DepositId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(plan.Deposits, deposit =>
        {
            Assert.True(GameContentCatalog.IsStableId(deposit.DepositId));
            Assert.True(deposit.DepositId.StartsWith("cave.deposit.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AcceptanceRequiresPrefabCollisionEntryExitAndNoTerrainEditing()
    {
        PlanetaryCavePlan plan = PlanetaryCaveRuntime.BuildPlan(
            "planet.vertical_slice",
            "poi.instance.cave_entrance",
            1082026);
        Dictionary<string, GameResourceDefinition> resources = plan.Deposits
            .Select(deposit => deposit.ResourceDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                id => id,
                id => new GameResourceDefinition(
                    id,
                    "item.test_resource",
                    1,
                    1,
                    0,
                    "Mining",
                    new ResourceVisualDefinition(
                        0.3, 0.35, 0.4,
                        0.0, 0.0, 0.0,
                        0.0, 0.1, 0.8),
                    new[] { "cave" }),
                StringComparer.Ordinal);

        PlanetaryCaveAcceptanceReport report =
            PlanetaryCaveAcceptanceRunner.Evaluate(
                plan,
                resources,
                liveCollisionShapeCount: 17,
                entryExitReady: true,
                livePrefabReady: true);

        Assert.True(report.Passed, report.BuildOutputLine());
        Assert.True(report.GlobalProceduralCavesDisabled);
        Assert.True(report.TerrainDeformationDisabled);
        Assert.True(report.PersistenceReady);
    }
}
