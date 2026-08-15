using System.Text.Json;
using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Unit;

public sealed class WorldGenTests
{
    [Theory]
    [InlineData(20260805L, 0, 0, 0)]
    [InlineData(20260805L, 3, -2, 1)]
    [InlineData(73199217L, -7, 4, 9)]
    [InlineData(99000123L, 18, -11, -5)]
    public void SeedHierarchy_IsDeterministic(long seed, int x, int y, int z)
    {
        GalaxyNavigationRuntime left = new(seed);
        GalaxyNavigationRuntime right = new(seed);

        GalaxySystemDefinition a = left.GenerateSystem(x, y, z);
        GalaxySystemDefinition b = right.GenerateSystem(x, y, z);

        Assert.Equal(ProjectHorizonGenerator.Version, GalaxyNavigationRuntime.GeneratorVersion);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.InRange(a.Planets.Count, 1, 8);
        Assert.All(a.Planets, planet =>
        {
            Assert.True(GameContentCatalog.IsStableId(planet.PlanetId));
            Assert.InRange(planet.MoonCount, 0, 4);
            Assert.True(planet.Seed >= 0);
        });
    }

    [Fact]
    public void SystemAndPlanetIds_AreStableUniqueAndCoordinateDerived()
    {
        GalaxyNavigationRuntime runtime = new(44_221_551L);
        HashSet<string> systems = new(StringComparer.Ordinal);
        HashSet<string> planets = new(StringComparer.Ordinal);

        for (int x = -4; x <= 4; x++)
        for (int y = -2; y <= 2; y++)
        for (int z = -3; z <= 3; z++)
        {
            GalaxySystemDefinition system = runtime.GenerateSystem(x, y, z);
            Assert.True(systems.Add(system.SystemId), $"duplicate system {system.SystemId}");
            Assert.All(system.Planets, planet =>
                Assert.True(planets.Add(planet.PlanetId), $"duplicate planet {planet.PlanetId}"));
        }
    }

    [Fact]
    public void PoiPlanner_IsDeterministicUniqueAndConstraintSafe()
    {
        PlanetaryPoiCatalog catalog = RepositoryFixture.Pois;
        IReadOnlyList<PlanetaryPoiPlacement> first = PlanetaryPoiPlanner.Plan(catalog);
        IReadOnlyList<PlanetaryPoiPlacement> second = PlanetaryPoiPlanner.Plan(catalog);

        Assert.Equal(PlanetaryPoiCatalog.ExpectedPoiTypeCount, first.Count);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(first.Count, first.Select(value => value.InstanceId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(first, placement =>
        {
            PlanetaryPoiDefinition definition = catalog.GetDefinition(placement.PoiTypeId);
            Assert.True(PlanetaryPoiPlanner.MeetsDefinitionConstraints(definition, placement.Environment));
            Assert.True(PlanetaryPoiPlanner.ClearsVerticalSliceInfrastructure(
                definition, placement.PositionX, placement.PositionZ));
        });
    }

    [Fact]
    public void StarSystemAnalyticOrbits_PreserveRadiusAndSingleDetailedPlanet()
    {
        GalaxySystemDefinition system = new GalaxyNavigationRuntime(8_712_345L)
            .GenerateSystem(2, 3, -1);
        StarSystemSimulationRuntime runtime = new(system);
        StarSystemBodyDefinition planet = runtime.Definitions.First(definition =>
            definition.Kind == StarSystemBodyKind.Planet);
        double expectedRadius = planet.OrbitRadius;

        foreach (double time in new[] { 0.0, 1.0, 17.5, 1234.0, 9999.0 })
        {
            SystemDouble3 position = runtime.EvaluateBodyPosition(planet.BodyId, time);
            Assert.InRange(Math.Abs(position.Length() - expectedRadius), 0.0, 1e-8);
        }

        StarSystemSimulationSnapshot snapshot = runtime.CreateSnapshot(
            planet.BodyId,
            planet.BodyId,
            detailedPlanetRequested: true);
        Assert.Equal(1, snapshot.DetailedPlanetCount);
    }

    [Fact]
    public void CoordinateTransforms_DistanceIsSymmetricAndTranslationInvariant()
    {
        GalaxyNavigationRuntime runtime = new(91_177L);
        GalaxySystemDefinition a = runtime.GenerateSystem(-2, 1, 3);
        GalaxySystemDefinition b = runtime.GenerateSystem(4, -5, 2);

        double ab = GalaxyNavigationRuntime.Distance(a, b);
        double ba = GalaxyNavigationRuntime.Distance(b, a);
        Assert.Equal(ab, ba, precision: 10);
        Assert.True(ab > 0.0);

        SystemDouble3 p = new(11.0, -4.0, 7.0);
        SystemDouble3 q = new(-3.0, 9.0, 2.0);
        SystemDouble3 translation = new(1000.0, -500.0, 250.0);
        Assert.Equal((p - q).Length(), ((p + translation) - (q + translation)).Length(), precision: 10);
    }

    [Theory]
    [InlineData(1.0, 4.0)]
    [InlineData(50.0, 4.0)]
    [InlineData(125.0, 4.0)]
    [InlineData(241.0, 6.0)]
    public void FuelCost_UsesDeterministicJumpBands(double distance, double expected)
    {
        Assert.Equal(expected, GalaxyNavigationRuntime.CalculateFuelCost(distance), precision: 10);
    }

    [Fact]
    public void FuelCost_RejectsNonPositiveDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GalaxyNavigationRuntime.CalculateFuelCost(0.0));
    }

    [Fact]
    public void NearbySystems_RoutePlanningAndSaveRestoreRemainDeterministic()
    {
        GalaxyNavigationRuntime runtime = new(20_260_805L);
        IReadOnlyList<GalaxySystemDefinition> nearby = runtime.GetNearbySystems(2, 20);
        Assert.Equal(20, nearby.Count);
        Assert.Equal(20, nearby.Select(system => system.SystemId).Distinct(StringComparer.Ordinal).Count());
        GalaxySystemDefinition destination = nearby.First();
        GalaxyRoutePlan route = runtime.PlanRoute(destination, 400.0);
        Assert.True(route.Reachable);
        Assert.True(route.Systems.Count >= 2);
        Assert.Equal(runtime.CurrentSystem.SystemId, route.Systems[0].SystemId);
        Assert.Equal(destination.SystemId, route.Systems[^1].SystemId);

        runtime.LoadSystemForDeveloper(2, -1, 4);
        GalaxyNavigationSaveData save = runtime.CreateSaveData();
        GalaxyNavigationRuntime restored = new(save);
        Assert.Equal(save.CurrentSystemId, restored.CurrentSystem.SystemId);
        Assert.Equal(save.VisitedSystemIds.OrderBy(id => id), restored.VisitedSystemIds.OrderBy(id => id));
    }

    [Fact]
    public void ResourceFieldPlanner_CoversEveryMissingCatalogResourceWithoutOverlap()
    {
        GameContentCatalog catalog = RepositoryFixture.Content;
        string[] existing = catalog.Resources.Keys.OrderBy(id => id, StringComparer.Ordinal).Take(12).ToArray();
        IReadOnlyList<CatalogResourcePlacement> placements = CatalogResourceFieldPlanner.BuildMissingPlacements(
            catalog.Resources,
            existing);
        Assert.Equal(catalog.Resources.Count - existing.Length, placements.Count);
        Assert.Equal(placements.Count, placements.Select(value => value.ResourceNodeId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(placements.Count, placements.Select(value => (value.PositionX, value.PositionY, value.PositionZ)).Distinct().Count());
        Assert.True(CatalogResourceFieldPlanner.CoversCatalog(
            catalog.Resources,
            existing.Concat(placements.Select(value => value.ResourceDefinitionId))));
    }

    [Fact]
    public void StarSystemSnapshots_ExposeAllRepresentationLevelsAcrossFarBodies()
    {
        GalaxyNavigationRuntime galaxy = new(8_982_331L);
        GalaxySystemDefinition selected = Enumerable.Range(-6, 13)
            .SelectMany(x => Enumerable.Range(-6, 13).Select(z => galaxy.GenerateSystem(x, 0, z)))
            .First(system => system.Planets.Count >= 6);
        StarSystemSimulationRuntime runtime = new(selected);
        StarSystemBodyDefinition firstPlanet = runtime.Definitions.First(body => body.Kind == StarSystemBodyKind.Planet);
        StarSystemSimulationSnapshot snapshot = runtime.CreateSnapshot(
            firstPlanet.BodyId,
            firstPlanet.BodyId,
            detailedPlanetRequested: true);
        Assert.Equal(1, snapshot.DetailedPlanetCount);
        Assert.Contains(snapshot.Bodies, body => body.Representation == StarSystemRepresentation.DetailedPlanet);
        Assert.Contains(snapshot.Bodies, body => body.Representation != StarSystemRepresentation.DetailedPlanet);
    }

    [Fact]
    public void StarterSystem_ProvidesFourDistinctStageTwoPlanets()
    {
        GalaxyNavigationRuntime runtime = new(GalaxyNavigationRuntime.DefaultUniverseSeed);
        GalaxySystemDefinition starter = runtime.CurrentSystem;

        Assert.Equal(GalaxyNavigationRuntime.StarterSystemId, starter.SystemId);
        Assert.Equal(4, starter.Planets.Count);
        Assert.Equal(4, starter.Planets.Select(planet => planet.Archetype)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new[] { "temperate", "desert", "frozen", "volcanic" },
            starter.Planets.Select(planet => planet.Archetype).ToArray());
    }

    [Fact]
    public void PlanetEnvironmentProfiles_AreDeterministicBoundedAndBiomeSafe()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        GalaxyNavigationRuntime galaxy = new(GalaxyNavigationRuntime.DefaultUniverseSeed);

        foreach (GalaxyPlanetDefinition planet in galaxy.CurrentSystem.Planets)
        {
            PlanetEnvironmentProfile first = environment.BuildProfile(
                planet,
                galaxy.CurrentSystem.StarType);
            PlanetEnvironmentProfile second = environment.BuildProfile(
                planet,
                galaxy.CurrentSystem.StarType);
            Assert.Equal(
                JsonSerializer.Serialize(first),
                JsonSerializer.Serialize(second));
            Assert.InRange(first.RadiusKm, 20.0, 80.0);
            Assert.InRange(first.CloudLayerCount, 0, 2);
            Assert.InRange(first.ActiveBiomeIds.Count, 1, 8);
            PlanetEnvironmentSample sample = environment.SampleBiome(
                first,
                latitudeDegrees: 42.0,
                normalizedElevation: 0.35,
                distanceToWaterKm: 8.0,
                localNoise: 0.25);
            Assert.Contains(sample.BiomeId, first.ActiveBiomeIds);
            Assert.InRange(sample.Moisture, 0.0, 1.0);
        }
    }

    [Fact]
    public void CurrentPlanetSelection_RoundTripsThroughGalaxySave()
    {
        GalaxyNavigationRuntime runtime = new(GalaxyNavigationRuntime.DefaultUniverseSeed);
        GalaxyPlanetDefinition target = runtime.CurrentSystem.Planets[2];

        Assert.True(runtime.TrySelectCurrentPlanet(target.PlanetId, out _));
        GalaxyNavigationRuntime restored = new(runtime.CreateSaveData());

        Assert.Equal(target.PlanetId, restored.CurrentPlanetId);
        Assert.Equal(target.Archetype, restored.CurrentPlanet.Archetype);
    }

    [Fact]
    public void GasGiantEnvironment_IsNonLandableAndHasNoSurfaceBiomes()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        GalaxyPlanetDefinition gasGiant = new(
            "planet.test.gas_giant",
            "gas_giant",
            1,
            0,
            true,
            false,
            8_881_331L);
        PlanetEnvironmentProfile profile = environment.BuildProfile(
            gasGiant,
            GalaxyStarType.YellowStar);

        Assert.False(profile.Landable);
        Assert.Empty(profile.ActiveBiomeIds);
        Assert.Throws<InvalidOperationException>(() => environment.SampleBiome(
            profile,
            0.0,
            0.0,
            0.0,
            0.0));
    }

}
