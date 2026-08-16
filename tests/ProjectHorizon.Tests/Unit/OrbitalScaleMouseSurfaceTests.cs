using Godot;
using ProjectHorizon.Tests.Support;

namespace ProjectHorizon.Tests.Unit;

public sealed class OrbitalScaleMouseSurfaceTests
{
    [Fact]
    public void MouseSteering_SurvivesOnePhysicsTickAndThenSettles()
    {
        Vector2 impulse = ArcadeFlightAssistRuntime.AccumulateMouseSteering(
            Vector2.Zero,
            new Vector2(64.0f, -48.0f),
            0.0035f,
            2.25f,
            invertPitch: false,
            invertYaw: false);
        Vector2 afterOneTick = ArcadeFlightAssistRuntime.DecayMouseSteering(
            impulse,
            7.5f,
            1.0f / 60.0f);

        Assert.True(impulse.Length() > 0.4f);
        Assert.True(afterOneTick.Length() >= impulse.Length() * 0.80f);

        Vector2 settled = impulse;
        for (int i = 0; i < 90; i++)
        {
            settled = ArcadeFlightAssistRuntime.DecayMouseSteering(
                settled,
                7.5f,
                1.0f / 60.0f);
        }
        Assert.True(settled.Length() <= 0.01f);
    }

    [Fact]
    public void ExpandedSystem_UsesPlayableScaleAwareCruiseSpeed()
    {
        double far = InterplanetaryTravelRuntime.CalculateSafeCruiseSpeed(120000.0);
        double approach = InterplanetaryTravelRuntime.CalculateSafeCruiseSpeed(1200.0);
        double near = InterplanetaryTravelRuntime.CalculateSafeCruiseSpeed(16.0);

        Assert.True(InterplanetaryTravelRuntime.CruiseSpeedMetersPerSecond >= 500.0);
        Assert.True(far >= 500.0);
        Assert.InRange(approach, 150.0, 280.0);
        Assert.Equal(InterplanetaryTravelRuntime.MaximumArrivalSpeed, near, 3);
    }

    [Fact]
    public void StarterSystem_UsesLargeBodiesAndWideOrbitalSeparation()
    {
        GalaxyNavigationRuntime galaxy = new();
        PlanetEnvironmentRuntime environments = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);
        StarSystemSimulationRuntime simulation = new(
            galaxy.CurrentSystem,
            0.0,
            planet =>
            {
                PlanetEnvironmentProfile profile = environments.BuildProfile(
                    planet,
                    galaxy.CurrentSystem.StarType);
                return Math.Clamp(profile.RadiusKm * 360.0, 9000.0, 28000.0);
            });

        StarSystemBodyDefinition[] planets = simulation.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Planet)
            .OrderBy(body => body.OrbitRadius)
            .ToArray();
        StarSystemBodyDefinition[] moons = simulation.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Moon)
            .ToArray();

        Assert.True(planets.Min(body => body.VisualRadius) >= 9000.0);
        Assert.True(planets.Min(body => body.OrbitRadius) >= 110000.0);
        Assert.True(planets.Zip(planets.Skip(1), (a, b) => b.OrbitRadius - a.OrbitRadius)
            .Min() >= 90000.0);
        foreach (StarSystemBodyDefinition moon in moons)
        {
            StarSystemBodyDefinition parent = planets.Single(planet =>
                planet.BodyId == moon.ParentBodyId);
            Assert.True(
                moon.OrbitRadius - parent.VisualRadius - moon.VisualRadius >= 25000.0);
        }
    }

    [Fact]
    public void EveryLandableStarterPlanet_HasEcologyPoisAndResources()
    {
        GalaxyNavigationRuntime galaxy = new();
        PlanetEnvironmentRuntime environments = new(
            RepositoryFixture.PlanetEnvironments,
            RepositoryFixture.Ecology);

        OrbitalScaleMouseSurfaceAcceptanceReport report =
            OrbitalScaleMouseSurfaceAcceptanceRunner.Run(
                galaxy.CurrentSystem,
                environments,
                RepositoryFixture.Ecology,
                RepositoryFixture.Pois,
                RepositoryFixture.Content.Resources);

        Assert.True(report.Passed, report.Result);
        Assert.True(report.LandablePlanets >= 3);
        Assert.Equal(report.LandablePlanets, report.ContentReadyPlanets);
        Assert.True(report.MinimumFlora >= 180);
        Assert.True(report.MinimumFauna > 0);
        Assert.True(report.MinimumPois >= 20);
        Assert.True(report.MinimumResources > 0);
    }

    [Fact]
    public void CrossPlanetSurfaceActivation_KeepsStrictTransitGraph()
    {
        WorldSceneCoordinatorRuntime coordinator = new(
            WorldSceneContext.Create(WorldSceneKind.Orbit, "system.x", "planet.a"));

        Assert.Equal(
            WorldSceneTransitionResult.Applied,
            coordinator.TryTransition(
                WorldSceneContext.Create(
                    WorldSceneKind.InterplanetaryTransit,
                    "system.x",
                    "planet.a"),
                out _));
        Assert.Equal(
            WorldSceneTransitionResult.Applied,
            coordinator.TryTransition(
                WorldSceneContext.Create(WorldSceneKind.Orbit, "system.x", "planet.b"),
                out _));
        Assert.Equal("planet.b", coordinator.Current.PlanetId);
    }
}
