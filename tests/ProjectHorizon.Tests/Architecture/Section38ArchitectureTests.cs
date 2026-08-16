using System;
using Godot;
using Xunit;

namespace ProjectHorizon.Tests.Architecture;

public sealed class Section38ArchitectureTests
{
    [Fact]
    public void DomainEventBusDispatchesTypedEventsAndUnsubscribes()
    {
        DomainEventBus bus = new();
        int itemEvents = 0;
        using IDisposable subscription = bus.Subscribe<ItemAdded>(evt =>
        {
            Assert.Equal("item.test", evt.DefinitionId);
            itemEvents++;
        });

        bus.Publish(new ItemAdded(
            "item.test",
            2,
            "unit-test",
            DateTimeOffset.UtcNow));
        Assert.Equal(1, itemEvents);
        Assert.Equal(1, bus.SubscriptionCount);

        subscription.Dispose();
        bus.Publish(new ItemAdded(
            "item.test",
            1,
            "unit-test",
            DateTimeOffset.UtcNow));
        Assert.Equal(1, itemEvents);
        Assert.Equal(0, bus.SubscriptionCount);
    }

    [Fact]
    public void FrequencyGateMatchesNearbyAndDistantAiBudgets()
    {
        SystemFrequencyGate nearby = new(SystemFrequencyPolicy.NearbyAiHz);
        SystemFrequencyGate distant = new(SystemFrequencyPolicy.DistantAiHz);
        int nearbyTicks = 0;
        int distantTicks = 0;
        const double delta = 1.0 / SystemFrequencyPolicy.PhysicsHz;

        for (int frame = 0; frame < 600; frame++)
        {
            if (nearby.Consume(delta)) nearbyTicks++;
            if (distant.Consume(delta)) distantTicks++;
        }

        Assert.InRange(nearbyTicks, 99, 101);
        Assert.InRange(distantTicks, 19, 21);
    }

    [Fact]
    public void EcologyUsesNormativeAiTiers()
    {
        Assert.Equal(SystemFrequencyPolicy.NearbyAiHz,
            EcologyRuntime.GetUpdateFrequencyHz(8.0), 6);
        Assert.Equal(SystemFrequencyPolicy.DistantAiHz,
            EcologyRuntime.GetUpdateFrequencyHz(35.0), 6);
        Assert.Equal(0.0, EcologyRuntime.GetUpdateFrequencyHz(80.0), 6);
    }

    [Fact]
    public void AerialAcceptanceHasDistanceIndependentFlyingFaunaProbe()
    {
        Assert.Equal(0.0, EcologyRuntime.GetUpdateFrequencyHz(160.0), 6);
        Assert.NotNull(typeof(EcologyFaunaNode).GetMethod(
            nameof(EcologyFaunaNode.StepAerialForAcceptance)));
    }

    [Fact]
    public void AerialAltitudeAcceptanceIgnoresDeadFaunaButStillExercisesController()
    {
        Assert.NotNull(typeof(EcologyFaunaNode).GetProperty(
            nameof(EcologyFaunaNode.IsActiveFlyingNavigationParticipant)));

        AerialSteeringRuntime steering = new();
        AerialSteeringSnapshot before = steering.CreateSnapshot();
        Vector3 correction = steering.ApplyAltitudeEnvelope(
            Vector3.Zero,
            0.0f,
            1.6f,
            3.4f,
            7.2f,
            1.65f,
            3.0f);
        AerialSteeringSnapshot after = steering.CreateSnapshot();

        Assert.True(correction.Y > 0.01f);
        Assert.True(after.AltitudeCorrections > before.AltitudeCorrections);
    }

    [Fact]
    public void AerialSpeedLimiterPreservesVerticalAuthorityUnderHeavyHorizontalSteering()
    {
        Vector3 desiredLocal = new(18.0f, 2.75f, -24.0f);
        Vector3 limited = AerialSteeringRuntime.ClampHorizontalAndVerticalSpeed(
            desiredLocal,
            5.0f,
            EcologyFaunaNode.FlyingMaximumVerticalSpeed);

        Vector3 horizontal = new(limited.X, 0.0f, limited.Z);
        Assert.InRange(horizontal.Length(), 4.999f, 5.001f);
        Assert.Equal(2.75f, limited.Y, 3);

        Vector3 verticalClamped = AerialSteeringRuntime.ClampHorizontalAndVerticalSpeed(
            new Vector3(100.0f, 8.0f, 100.0f),
            7.0f,
            3.0f);
        Assert.Equal(3.0f, verticalClamped.Y, 3);
    }

    [Fact]
    public void InterplanetarySelectionConsistencyRejectsStaleCrossSystemTarget()
    {
        GalaxyNavigationRuntime galaxy = new();
        InterplanetaryTravelRuntime travel = new();
        Assert.True(travel.IsSelectionConsistentWith(galaxy));

        GalaxyPlanetDefinition target = galaxy.CurrentSystem.Planets[1];
        Assert.True(galaxy.TrySelectPlanetDestination(target.PlanetId, out _));
        Assert.False(travel.IsSelectionConsistentWith(galaxy));

        travel.SynchronizeSelection(galaxy);
        Assert.True(travel.IsSelectionConsistentWith(galaxy));

        galaxy.ClearPlanetDestination();
        Assert.False(travel.IsSelectionConsistentWith(galaxy));

        travel.SynchronizeSelection(galaxy);
        Assert.True(travel.IsSelectionConsistentWith(galaxy));
        Assert.Equal(InterplanetaryTravelPhase.Idle, travel.Phase);
        Assert.Empty(travel.TargetPlanetId);
    }

    [Fact]
    public void SpaceflightNavigationClosureExposesSixNormativeContracts()
    {
        Assert.Equal(6,
            SpaceflightNavigationSubsystemAcceptanceRunner.ExpectedContractCount);
        Assert.NotNull(typeof(InterplanetaryTravelRuntime).GetMethod(
            nameof(InterplanetaryTravelRuntime.IsSelectionConsistentWith)));
    }

    [Fact]
    public void SpaceflightNavigationClosureRequiresEveryCrossContractChain()
    {
        ShipSystemsAcceptanceReport ship = new(
            Passed: true, Result: "ok", ShipClasses: 6, Systems: 7, Modules: 18,
            CatalogCoverage: true, ClassStats: true, InstallAll: true,
            SlotLimits: true, DuplicateRejected: true, DerivedStats: true,
            DamageLifecycle: true, RepairLifecycle: true, ModuleDisable: true,
            FlightReadiness: true, HyperspaceReadiness: true, FuelLifecycle: true,
            InventoryConservation: true, PreRepairBlocked: true,
            PreRepairFlightReady: true, CommissionTransition: true,
            PostRepairFlightReady: true, ResetCommissioned: true, ColdRestore: true,
            LegacyFallback: true, ExactRoundTrip: true, LogWritten: true,
            Diagnostics: null!, ElapsedMilliseconds: 1.0);
        StageOneVoyageAcceptanceReport voyage = new(
            Passed: true, Result: "ok", DerivedStatsApplied: true,
            PreRepairBlocked: true, Takeoff: true, FuelDebited: true, Docking: true,
            StationVisited: true, Undock: true, Landing: true, LoopCompleted: true,
            ReadinessRejected: true, ColdRestore: true, LegacyFallback: true,
            ExactRoundTrip: true, LogWritten: true, Diagnostics: null!,
            ElapsedMilliseconds: 1.0);
        GalaxyNavigationAcceptanceReport galaxy = new(
            Passed: true, Result: "ok", DeterministicGeneration: true,
            CoordinateHierarchy: true, StarCoverage: true, PlanetBounds: true,
            RoutePlanning: true, Preconditions: true, HyperspaceJump: true,
            FuelDebited: true, VisitedPersistence: true, Stress100: true,
            ColdRestore: true, LegacyFallback: true, ExactRoundTrip: true,
            LogWritten: true, Diagnostics: null!, ElapsedMilliseconds: 1.0);
        StarSystemSimulationAcceptanceReport star = new(
            Passed: true, DeterministicGeneration: true, BodyCoverage: true,
            MoonBounds: true, AnalyticOrbits: true, RepresentationLevels: true,
            SingleDetailedPlanet: true, SystemTransition: true, VisualProjection: true,
            RuntimeSamples: true, SurfaceActivation: true, ActivationPipeline: true,
            Bodies: 21, Planets: 4, Moons: 8, Stations: 1, ShipContacts: 7,
            VisualNodes: 21, Rebuilds: 2, Result: "ok");
        InterplanetaryTravelAcceptanceReport interplanetary = new(
            Passed: true, StarterPlanetCoverage: true, TargetSelection: true,
            TargetPersistence: true, FuelDebited: true, Guidance: true,
            WorldHandoff: true, Arrival: true, TransferPersistence: true,
            SameSystemInvariant: true, PlannedDistanceMeters: 192.0, FuelCost: 2.07,
            SourcePlanetId: "planet.a", TargetPlanetId: "planet.b", Result: "ok");
        WorldSceneCoordinatorAcceptanceReport world = new(
            Passed: true, TransitionGraph: true, IllegalTransitionRejected: true,
            HyperspaceSystemChange: true, ContextValidation: true, PackedScenes: true,
            SingleLiveScene: true, LiveContextMatch: true, ResidencyPolicy: true,
            LiveTransitionPath: true, TransactionalSwap: true, StateRestored: true,
            LiveSteps: 7, MaxHostChildren: 1, TransitionCount: 6, Reloads: 7,
            RejectedTransitions: 1, HyperspaceTransitions: 1, Result: "ok");

        SpaceflightNavigationSubsystemModelAcceptanceReport pass =
            SpaceflightNavigationSubsystemAcceptanceRunner.Run(
                ship, voyage, galaxy, star, interplanetary, world);
        Assert.True(pass.Passed);
        Assert.Equal(6, pass.ContractsPassed);
        Assert.True(pass.ReadinessChain && pass.FuelChain && pass.TransitionChain);
        Assert.True(pass.PersistenceChain && pass.NavigationIdentity && pass.BoundedResidency);

        SpaceflightNavigationSubsystemModelAcceptanceReport brokenFuel =
            SpaceflightNavigationSubsystemAcceptanceRunner.Run(
                ship, voyage with { FuelDebited = false }, galaxy, star,
                interplanetary, world);
        Assert.False(brokenFuel.Passed);
        Assert.False(brokenFuel.FuelChain);
        Assert.Equal(6, brokenFuel.ContractsPassed);
    }

    [Fact]
    public void LayeredAssembliesHaveOneWayDependencies()
    {
        var domainAssembly = typeof(IDomainEvent).Assembly;
        var applicationAssembly = typeof(DomainEventBus).Assembly;
        var clientAssembly = typeof(EcologyRuntime).Assembly;

        Assert.Equal("Game.Domain", domainAssembly.GetName().Name);
        Assert.Equal("Game.Application", applicationAssembly.GetName().Name);
        Assert.Equal("Game.Client", clientAssembly.GetName().Name);
        Assert.DoesNotContain(domainAssembly.GetReferencedAssemblies(), reference =>
            reference.Name is "Game.Application" or "Game.Client" or "GodotSharp" or "Microsoft.Data.Sqlite");
        Assert.Contains(applicationAssembly.GetReferencedAssemblies(), reference =>
            reference.Name == "Game.Domain");
        Assert.DoesNotContain(applicationAssembly.GetReferencedAssemblies(), reference =>
            reference.Name is "Game.Client" or "GodotSharp" or "Microsoft.Data.Sqlite");
    }

}
