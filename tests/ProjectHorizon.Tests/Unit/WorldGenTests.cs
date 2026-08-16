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

    [Fact]
    public void PlanetDestinationSelection_PersistsWithoutChangingCurrentPlanet()
    {
        GalaxyNavigationRuntime runtime = new(GalaxyNavigationRuntime.DefaultUniverseSeed);
        string source = runtime.CurrentPlanetId;
        GalaxyPlanetDefinition target = runtime.CurrentSystem.Planets[1];

        Assert.True(runtime.TrySelectPlanetDestination(target.PlanetId, out _));
        GalaxyNavigationRuntime restored = new(runtime.CreateSaveData());

        Assert.Equal(source, restored.CurrentPlanetId);
        Assert.Equal(target.PlanetId, restored.SelectedPlanetId);
        Assert.Equal(0, restored.InterplanetaryTransferCount);
    }

    [Fact]
    public void InterplanetaryTransfer_UpdatesPlanetCountersFuelAndRoundTrips()
    {
        GalaxyNavigationRuntime galaxy = new(GalaxyNavigationRuntime.DefaultUniverseSeed);
        GalaxyPlanetDefinition target = galaxy.CurrentSystem.Planets[1];
        Assert.True(galaxy.TrySelectPlanetDestination(target.PlanetId, out _));
        ShipSystemsRuntime ship = new(RepositoryFixture.Ships, commissioned: true);
        StageOneVoyageRuntime voyage = new();
        Assert.Equal(StageOneVoyageActionResult.Applied, voyage.TryBoard(ship, out _));
        Assert.Equal(StageOneVoyageActionResult.Applied, voyage.TryLaunch(ship, out _));
        double fuelBefore = ship.Fuel;
        InterplanetaryTravelRuntime travel = new();
        travel.SynchronizeSelection(galaxy);

        Assert.Equal(
            InterplanetaryTravelActionResult.Applied,
            travel.TryBeginCruise(galaxy, voyage, ship, 192.0, out _));
        Assert.True(ship.Fuel < fuelBefore);
        Assert.True(travel.BuildGuidance(8.0, 5.0).ArrivalReady);
        Assert.True(travel.TryCompleteArrival(galaxy, 192.0, out _));
        voyage.ArriveAtPlanetaryApproach();

        GalaxyNavigationRuntime restored = new(galaxy.CreateSaveData());
        Assert.Equal(target.PlanetId, restored.CurrentPlanetId);
        Assert.Equal(string.Empty, restored.SelectedPlanetId);
        Assert.Equal(1, restored.InterplanetaryTransferCount);
        Assert.True(restored.TotalInterplanetaryDistanceMeters >= 192.0);
        Assert.Equal(StageOneVoyageLocation.InboundFlight, voyage.Location);
        Assert.Equal("planet.approach", voyage.LastCheckpoint);
    }

    [Fact]
    public void PlanetSurfaceContent_VariesAcrossFourStarterPlanets()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        PlanetSurfaceContentProfile[] profiles = galaxy.CurrentSystem.Planets
            .Select(planet => surface.BuildProfile(
                planet,
                galaxy.CurrentSystem.StarType))
            .ToArray();

        Assert.Equal(4, profiles.Length);
        Assert.Equal(4, profiles.Select(profile => profile.RegionKey)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, profiles.Select(profile =>
                string.Join("|", profile.ActiveBiomeIds))
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(profiles, profile =>
        {
            EcologyPlan plan = surface.BuildEcologyPlan(profile);
            Assert.InRange(plan.Flora.Count, 180, EcologyPlanner.GameplayFloraInstanceCount);
            Assert.All(plan.Flora, flora => Assert.Contains(
                flora.BiomeId,
                profile.ActiveBiomeIds));

            // TASK-164 regression: macro relief must not starve the deterministic
            // POI planner of low-slope candidates (notably the landing pad on the
            // desert starter planet).
            IReadOnlyList<PlanetaryPoiPlacement> pois = surface.BuildPoiPlan(profile);
            Assert.Equal(PlanetaryPoiCatalog.ExpectedPoiTypeCount, pois.Count);
            Assert.All(pois, poi => Assert.True(
                PlanetaryPoiPlanner.MeetsDefinitionConstraints(
                    RepositoryFixture.Pois.GetDefinition(poi.PoiTypeId),
                    poi.Environment)));
        });
    }

    [Theory]
    [InlineData("crystal", "crystal")]
    [InlineData("fiber", "fiber")]
    [InlineData("bio", "organic")]
    [InlineData("metal", "ore")]
    public void SurfaceVisualLanguage_ResourceFamiliesAreDeterministic(
        string tag,
        string expectedFamily)
    {
        GameResourceDefinition resource = new(
            "resource.test.visual",
            "item.test.visual",
            1,
            1,
            0,
            "hand",
            new ResourceVisualDefinition(
                0.3, 0.4, 0.5,
                0.0, 0.0, 0.0,
                0.0, 0.2, 0.8),
            new[] { "surface", tag });

        Assert.Equal(
            expectedFamily,
            ProceduralSurfaceVisualFactory.ResolveResourceFamily(resource));
    }

    [Fact]
    public void PlanetSurfaceContent_DryPlanetExcludesAquaticFauna()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        GalaxyPlanetDefinition volcanic = galaxy.CurrentSystem.Planets
            .Single(planet => string.Equals(
                planet.Archetype,
                "volcanic",
                StringComparison.Ordinal));
        PlanetSurfaceContentProfile profile = surface.BuildProfile(
            volcanic,
            galaxy.CurrentSystem.StarType);
        EcologyPlan plan = surface.BuildEcologyPlan(profile);

        Assert.False(profile.WaterHabitatEnabled);
        Assert.DoesNotContain(
            plan.ActiveFauna.Concat(plan.SimplifiedFauna),
            spawn => string.Equals(
                RepositoryFixture.Ecology.GetFauna(spawn.FaunaId).MovementMode,
                "Aquatic",
                StringComparison.Ordinal));
    }

    [Fact]
    public void PlanetSurfaceContent_PoiAndEcologyStateRoundTripByPlanetIdentity()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        GalaxyPlanetDefinition planet = galaxy.CurrentSystem.Planets[1];
        PlanetSurfaceContentProfile profile = surface.BuildProfile(
            planet,
            galaxy.CurrentSystem.StarType);

        IReadOnlyList<PlanetaryPoiPlacement> firstPois = surface.BuildPoiPlan(profile);
        IReadOnlyList<PlanetaryPoiPlacement> secondPois = surface.BuildPoiPlan(profile);
        Assert.Equal(JsonSerializer.Serialize(firstPois), JsonSerializer.Serialize(secondPois));
        Assert.All(firstPois, poi => Assert.Contains(
            poi.Environment.BiomeId,
            profile.ActiveBiomeIds));

        PlanetaryExplorationRuntime exploration = new(
            RepositoryFixture.Pois,
            firstPois,
            profile.WorldSeed,
            profile.RegionKey);
        Assert.Equal(
            PlanetaryPoiScanResult.Discovered,
            exploration.Scan(firstPois[0].InstanceId, out _));
        PlanetaryExplorationRuntime explorationRestore = new(
            RepositoryFixture.Pois,
            firstPois,
            profile.WorldSeed,
            profile.RegionKey,
            exploration.CreateSaveData());
        Assert.Equal(1, explorationRestore.DiscoveredCount);

        EcologyPlan ecologyPlan = surface.BuildEcologyPlan(profile);
        EcologyRuntime ecology = new(
            RepositoryFixture.Ecology,
            ecologyPlan,
            profile.WorldSeed,
            profile.RegionKey);
        Assert.True(ecology.TryScanFlora(
            ecologyPlan.Flora[0].InstanceId,
            out _,
            out _));
        EcologyRuntime ecologyRestore = new(
            RepositoryFixture.Ecology,
            ecologyPlan,
            profile.WorldSeed,
            profile.RegionKey,
            ecology.CreateSaveData());
        Assert.Equal(1, ecologyRestore.DiscoveredFloraCount);
    }

    [Fact]
    public void PlanetSurfaceTerrain_FourStarterPlanetsHaveDistinctDeterministicMorphology()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        PlanetSurfaceContentProfile[] profiles = galaxy.CurrentSystem.Planets
            .Select(planet => surface.BuildProfile(
                planet,
                galaxy.CurrentSystem.StarType))
            .ToArray();

        Assert.Equal(4, profiles.Length);
        Assert.Equal(4, profiles.Select(profile =>
                PlanetSurfaceTerrainRuntime.MorphologySignature(profile.Terrain))
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(profiles, profile =>
        {
            PlanetSurfaceTerrainSample first = PlanetSurfaceTerrainRuntime.Sample(
                profile.Terrain,
                27.25,
                -18.75);
            PlanetSurfaceTerrainSample second = PlanetSurfaceTerrainRuntime.Sample(
                profile.Terrain,
                27.25,
                -18.75);
            Assert.Equal(first, second);
            Assert.InRange(first.SlopeDegrees, 0.0, 89.0);
        });
    }

    [Fact]
    public void PlanetSurfaceTerrain_PreservesCentralTerraceAndWetWorldBasins()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();

        foreach (GalaxyPlanetDefinition planet in galaxy.CurrentSystem.Planets)
        {
            PlanetSurfaceContentProfile profile = surface.BuildProfile(
                planet,
                galaxy.CurrentSystem.StarType);
            Assert.InRange(
                Math.Abs(PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile.Terrain,
                    10.0,
                    -7.0)),
                0.0,
                0.001);
            if (profile.WaterHabitatEnabled)
            {
                Assert.True(PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile.Terrain,
                    -25.5,
                    25.5) <= -0.35);
            }
            else
            {
                Assert.False(profile.Terrain.WaterBasinsEnabled);
            }
        }
    }

    [Fact]
    public void PlanetSurfaceTerrain_GroundsEcologyAndTerrainAwarePois()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        PlanetSurfaceContentProfile profile = surface.BuildProfile(
            galaxy.CurrentSystem.Planets[1],
            galaxy.CurrentSystem.StarType);

        EcologyPlan ecology = surface.BuildEcologyPlan(profile);
        Assert.All(ecology.Flora.Take(24), placement => Assert.InRange(
            Math.Abs(placement.PositionY - PlanetSurfaceTerrainRuntime.SampleHeight(
                profile.Terrain,
                placement.PositionX,
                placement.PositionZ)),
            0.0,
            0.000001));

        IReadOnlyList<PlanetaryPoiPlacement> pois = surface.BuildPoiPlan(profile);
        Assert.Equal(PlanetaryPoiCatalog.ExpectedPoiTypeCount, pois.Count);
        Assert.All(pois, placement =>
        {
            Assert.True(double.IsFinite(placement.Environment.SlopeDegrees));
            Assert.True(PlanetaryPoiPlanner.MeetsDefinitionConstraints(
                RepositoryFixture.Pois.GetDefinition(placement.PoiTypeId),
                placement.Environment));
        });
    }

    [Fact]
    public void PlanetSurfaceStreaming_PlanIsBoundedAndUsesTwoLods()
    {
        PlanetSurfaceChunkCoordinate center = new(0, 0);
        IReadOnlyList<PlanetSurfaceStreamingSpec> plan =
            PlanetSurfaceStreamingRuntime.BuildPlan(center);

        Assert.Equal(PlanetSurfaceStreamingRuntime.ExpectedActiveChunks, plan.Count);
        Assert.Equal(
            PlanetSurfaceStreamingRuntime.ExpectedHighDetailChunks,
            plan.Count(spec => spec.LodLevel == 0));
        Assert.Equal(
            PlanetSurfaceStreamingRuntime.ExpectedLowDetailChunks,
            plan.Count(spec => spec.LodLevel == 1));
        Assert.Equal(
            PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks,
            plan.Count(spec => spec.GenerateCollision));
        PlanetSurfaceStreamingSpec eastHigh = plan.Single(spec =>
            spec.Coordinate == new PlanetSurfaceChunkCoordinate(1, 0));
        Assert.True((eastHigh.StitchMask & TerrainEdgeStitchMask.East) != 0);
        Assert.Equal(20, PlanetSurfaceStreamingRuntime.ExpectedRetainedChunkCount(
            new PlanetSurfaceChunkCoordinate(0, 0),
            new PlanetSurfaceChunkCoordinate(1, 0)));
        Assert.Equal(16, PlanetSurfaceStreamingRuntime.ExpectedRetainedChunkCount(
            new PlanetSurfaceChunkCoordinate(0, 0),
            new PlanetSurfaceChunkCoordinate(1, 1)));
    }

    [Fact]
    public void PlanetSurfaceStreaming_ChunkSamplesAreDeterministicAndSeamSafe()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        PlanetSurfaceContentProfile profile = surface.BuildProfile(
            galaxy.CurrentSystem.Planets[3],
            galaxy.CurrentSystem.StarType);
        PlanetSurfaceChunkCoordinate coordinate = new(7, -4);

        string first = PlanetSurfaceStreamingRuntime.BuildChunkSignature(
            profile.Terrain,
            coordinate);
        string second = PlanetSurfaceStreamingRuntime.BuildChunkSignature(
            profile.Terrain,
            coordinate);

        Assert.Equal(first, second);
        Assert.InRange(
            PlanetSurfaceStreamingRuntime.MeasureSharedEdgeError(
                profile.Terrain,
                new PlanetSurfaceChunkCoordinate(1, 0),
                new PlanetSurfaceChunkCoordinate(2, 0)),
            0.0,
            0.000001);
    }

    [Fact]
    public void PlanetSurfaceStreaming_TraversalAddressUsesPlanetRadius()
    {
        PlanetSurfaceChunkCoordinate origin =
            PlanetSurfaceStreamingRuntime.WorldToChunk(0.0, 0.0);
        PlanetSurfaceChunkCoordinate distant =
            PlanetSurfaceStreamingRuntime.WorldToChunk(161.0, -97.0);
        PlanetSurfaceGeodesicAddress small =
            PlanetSurfaceStreamingRuntime.BuildGeodesicAddress(
                20.0,
                12_500.0,
                -7_500.0);
        PlanetSurfaceGeodesicAddress large =
            PlanetSurfaceStreamingRuntime.BuildGeodesicAddress(
                80.0,
                12_500.0,
                -7_500.0);

        Assert.Equal(new PlanetSurfaceChunkCoordinate(0, 0), origin);
        Assert.NotEqual(origin, distant);
        Assert.InRange(small.LatitudeDegrees, -90.0, 90.0);
        Assert.InRange(small.LongitudeDegrees, -180.0, 180.0);
        Assert.True(large.CircumferenceMeters > small.CircumferenceMeters);
        Assert.Equal(small.SurfaceDistanceMeters, large.SurfaceDistanceMeters, 6);
        Assert.True(Math.Abs(small.LatitudeDegrees) > Math.Abs(large.LatitudeDegrees));
    }

    [Fact]
    public void PlanetSurfaceWorldComposition_SkyProfilesExposeStarAtmosphereAndCloudPolicy()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        GalaxySystemDefinition system = galaxy.CurrentSystem;

        PlanetSurfaceSkyProfile[] skies = system.Planets
            .Select(planet => surface.BuildProfile(planet, system.StarType))
            .Select(profile => PlanetSurfaceWorldCompositionRuntime.BuildSkyProfile(
                profile.Environment,
                system.StarType))
            .ToArray();

        Assert.Equal(4, skies.Length);
        Assert.All(skies, sky =>
        {
            Assert.True(sky.AtmosphereEnabled);
            Assert.True(sky.SunEnergy >= 0.8);
            Assert.InRange(sky.SunElevationDegrees, 20.0, 75.0);
            Assert.True(sky.FogDensity > 0.0);
        });
        Assert.All(skies.Where(sky => sky.CloudLayerCount == 0),
            sky => Assert.Equal(0, sky.CloudClusterCount));
        Assert.All(skies.Where(sky => sky.CloudLayerCount > 0),
            sky => Assert.True(sky.CloudClusterCount >= 6));
    }

    [Fact]
    public void PlanetSurfaceWorldComposition_ResourcesAreDeterministicDistributedAndPlanetScoped()
    {
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        GalaxySystemDefinition system = galaxy.CurrentSystem;
        PlanetSurfaceChunkCoordinate center = new(4, -3);
        HashSet<string> allIds = new(StringComparer.Ordinal);

        foreach (GalaxyPlanetDefinition planet in system.Planets)
        {
            PlanetSurfaceContentProfile profile = surface.BuildProfile(
                planet,
                system.StarType);
            IReadOnlyList<PlanetSurfaceResourcePlacement> first =
                PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                    profile,
                    RepositoryFixture.Content.Resources,
                    center);
            IReadOnlyList<PlanetSurfaceResourcePlacement> second =
                PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                    profile,
                    RepositoryFixture.Content.Resources,
                    center);

            Assert.NotEmpty(first);
            Assert.Equal(
                string.Join("|", first.Select(value => value.ResourceNodeId)),
                string.Join("|", second.Select(value => value.ResourceNodeId)));
            Assert.All(first, placement =>
            {
                Assert.True(PlanetSurfaceWorldCompositionRuntime.IsOutsideStarterReserve(
                    placement.PositionX,
                    placement.PositionZ));
                Assert.InRange(
                    placement.SlopeDegrees,
                    0.0,
                    PlanetSurfaceWorldCompositionRuntime.MaximumResourceSlopeDegrees);
                Assert.True(RepositoryFixture.Content.Resources.ContainsKey(
                    placement.ResourceDefinitionId));
                Assert.True(allIds.Add(placement.ResourceNodeId));
            });
        }

        PlanetSurfaceContentProfile starterProfile = surface.BuildProfile(
            system.Planets[0],
            system.StarType);
        (double X, double Z)[] livePois = surface.BuildPoiPlan(starterProfile)
            .Select(placement =>
                PlanetSurfaceWorldCompositionRuntime.BuildPoiPresentationPosition(
                    starterProfile,
                    placement.InstanceId))
            .ToArray();
        Assert.Equal(PlanetaryPoiCatalog.ExpectedPoiTypeCount, livePois.Length);
        Assert.All(livePois, point => Assert.InRange(
            Math.Sqrt(point.X * point.X + point.Z * point.Z),
            78.0,
            421.0));
        Assert.True(livePois
            .Select(point => PlanetSurfaceStreamingRuntime.WorldToChunk(point.X, point.Z))
            .Distinct()
            .Count() >= 12);
    }

    [Fact]
    public void PlanetSurfaceWorldComposition_DynamicDepletionSurvivesColdRestoreWithoutUntouchedDeltas()
    {
        GameContentCatalog content = RepositoryFixture.Content;
        CraftingRecipeDefinition repair = content.GetRecipe("recipe.ship.starter_repair");
        PlanetEnvironmentRuntime environment = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        PlanetSurfaceContentRuntime surface = new(
            environment,
            RepositoryFixture.Ecology,
            RepositoryFixture.Pois);
        GalaxyNavigationRuntime galaxy = new();
        PlanetSurfaceContentProfile profile = surface.BuildProfile(
            galaxy.CurrentSystem.Planets[0],
            galaxy.CurrentSystem.StarType);
        PlanetSurfaceResourcePlacement placement =
            PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                profile,
                content.Resources,
                new PlanetSurfaceChunkCoordinate(3, 2))[0];
        GameResourceDefinition resource = content.Resources[
            placement.ResourceDefinitionId];
        StarterRepairSession session = new(repair, static _ => true);

        Assert.True(session.TryCollect(
            placement.ResourceNodeId,
            resource.ItemDefinitionId,
            resource.GetDeterministicYield(),
            out _));
        SaveGameSnapshot snapshot = StarterRepairSnapshotFactory.Create(
            "save_1", 1, session, 0.0, 0.0, 0.0);
        StarterRepairSession restored =
            StarterRepairSession.FromSnapshotWithDynamicResources(
                snapshot,
                new Dictionary<string, ResourceNodeBinding>(StringComparer.Ordinal),
                repair,
                static _ => true,
                (nodeId, itemDefinitionId) =>
                    nodeId.StartsWith("surface_resource.", StringComparison.Ordinal) &&
                    string.Equals(itemDefinitionId, resource.ItemDefinitionId, StringComparison.Ordinal)
                        ? new ResourceNodeBinding(
                            nodeId,
                            itemDefinitionId,
                            resource.GetDeterministicYield())
                        : null);

        Assert.Contains(placement.ResourceNodeId, restored.CollectedNodeIds);
        StarterRepairSession untouched = new(repair, static _ => true);
        SaveGameSnapshot untouchedSnapshot = StarterRepairSnapshotFactory.Create(
            "save_1", 1, untouched, 0.0, 0.0, 0.0);
        Assert.DoesNotContain(
            untouchedSnapshot.Inventory,
            item => item.ItemId.StartsWith("item.surface_resource.", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanetSurfaceFrame_RebaseKeepsLocalCoordinatesBoundedAndLogicalPositionContinuous()
    {
        PlanetSurfaceFrameRuntime frame = new();
        frame.Reset("planet.test");
        double logicalEast = 73_125.25;
        double logicalNorth = -51_876.75;
        (double localEast, double localNorth) = frame.ToLocal(
            logicalEast,
            logicalNorth);
        PlanetSurfaceFrameRebase rebase = frame.PlanRebase(localEast, localNorth);

        Assert.True(rebase.Required);
        frame.Apply(rebase);
        (localEast, localNorth) = frame.ToLocal(logicalEast, logicalNorth);
        PlanetSurfaceLogicalPosition roundTrip = frame.ToLogical(
            localEast,
            12.5,
            localNorth);

        Assert.InRange(
            Math.Abs(localEast),
            0.0,
            PlanetSurfaceFrameRuntime.LocalCoordinateToleranceMeters);
        Assert.InRange(
            Math.Abs(localNorth),
            0.0,
            PlanetSurfaceFrameRuntime.LocalCoordinateToleranceMeters);
        Assert.Equal(logicalEast, roundTrip.EastMeters, 6);
        Assert.Equal(logicalNorth, roundTrip.NorthMeters, 6);
    }

    [Fact]
    public void PlanetSurfaceFrame_ColdRestorePreservesChunkIdentity()
    {
        const double east = 128_333.75;
        const double north = -94_201.5;
        PlanetSurfaceChunkCoordinate expected =
            PlanetSurfaceStreamingRuntime.WorldToChunk(east, north);
        PlanetSurfaceFrameRuntime frame = new();
        frame.RestoreAtLogicalPosition("planet.restore", east, north);
        (double localEast, double localNorth) = frame.ToLocal(east, north);
        PlanetSurfaceLogicalPosition roundTrip = frame.ToLogical(
            localEast,
            0.0,
            localNorth);

        Assert.Equal(
            expected,
            PlanetSurfaceStreamingRuntime.WorldToChunk(
                roundTrip.EastMeters,
                roundTrip.NorthMeters));
        Assert.Equal(0.0, localEast, 6);
        Assert.Equal(0.0, localNorth, 6);
    }

    [Fact]
    public void PlanetSurfaceFrame_AcceptanceCoversLongTraversalRestoreAndPlanetReset()
    {
        PlanetSurfaceFrameAcceptanceReport report =
            PlanetSurfaceFrameAcceptanceRunner.Run();

        Assert.True(report.Passed, report.BuildOutputLine());
        Assert.True(report.RebaseCount >= 8);
        Assert.True(report.LogicalContinuity);
        Assert.True(report.ChunkIdentityStable);
        Assert.True(report.ColdRestoreStable);
        Assert.True(report.PlanetResetStable);
        Assert.True(report.GeodesicStable);
    }

}
