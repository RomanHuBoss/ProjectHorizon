using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed record OrbitalScaleMouseSurfaceAcceptanceReport(
    bool Passed,
    bool PlanetScale,
    bool PlanetSpacing,
    bool MoonClearance,
    bool MouseSteering,
    bool MouseRetention,
    bool PlayableCruise,
    bool PhysicalTransferGraph,
    bool LandableSurfaceCoverage,
    int LandablePlanets,
    int ContentReadyPlanets,
    double MinimumPlanetRadius,
    double MinimumPlanetGap,
    double MinimumMoonClearance,
    int MinimumFlora,
    int MinimumFauna,
    int MinimumPois,
    int MinimumResources,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS scale={MinimumPlanetRadius:0}m gap={MinimumPlanetGap / 1000.0:0}km " +
          $"mouse=1 content={ContentReadyPlanets}/{LandablePlanets}"
        : $"FAIL scale={(PlanetScale ? 1 : 0)} mouse={(MouseSteering ? 1 : 0)} " +
          $"content={ContentReadyPlanets}/{LandablePlanets}";

    public string BuildOutputLine() =>
        $"TASK-178.6 orbital scale/mouse/multi-planet acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"planetScale={(PlanetScale ? 1 : 0)}; planetSpacing={(PlanetSpacing ? 1 : 0)}; " +
        $"moonClearance={(MoonClearance ? 1 : 0)}; mouseSteering={(MouseSteering ? 1 : 0)}; " +
        $"mouseRetention={(MouseRetention ? 1 : 0)}; playableCruise={(PlayableCruise ? 1 : 0)}; " +
        $"physicalTransfer={(PhysicalTransferGraph ? 1 : 0)}; " +
        $"landableContent={(LandableSurfaceCoverage ? 1 : 0)}; " +
        $"planets={ContentReadyPlanets}/{LandablePlanets}; " +
        $"planetRadiusMin={MinimumPlanetRadius:0}m; planetGapMin={MinimumPlanetGap:0}m; " +
        $"moonClearanceMin={MinimumMoonClearance:0}m; floraMin={MinimumFlora}; faunaMin={MinimumFauna}; " +
        $"poisMin={MinimumPois}; resourcesMin={MinimumResources}; result={Result}";
}

public static class OrbitalScaleMouseSurfaceAcceptanceRunner
{
    public static OrbitalScaleMouseSurfaceAcceptanceReport Run(
        GalaxySystemDefinition system,
        PlanetEnvironmentRuntime environment,
        EcologyCatalog ecologyCatalog,
        PlanetaryPoiCatalog poiCatalog,
        IReadOnlyDictionary<string, GameResourceDefinition> resources)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(ecologyCatalog);
        ArgumentNullException.ThrowIfNull(poiCatalog);
        ArgumentNullException.ThrowIfNull(resources);

        try
        {
            StarSystemSimulationRuntime simulation = new(
                system,
                0.0,
                planet =>
                {
                    PlanetEnvironmentProfile profile = environment.BuildProfile(
                        planet,
                        system.StarType);
                    return Math.Clamp(profile.RadiusKm * 360.0, 9000.0, 28000.0);
                });
            StarSystemBodyDefinition[] planets = simulation.Definitions
                .Where(body => body.Kind == StarSystemBodyKind.Planet)
                .OrderBy(body => body.OrbitRadius)
                .ToArray();
            StarSystemBodyDefinition[] moons = simulation.Definitions
                .Where(body => body.Kind == StarSystemBodyKind.Moon)
                .ToArray();

            double minPlanetRadius = planets.Min(body => body.VisualRadius);
            double minPlanetGap = planets.Length <= 1
                ? double.PositiveInfinity
                : planets.Zip(planets.Skip(1), (left, right) =>
                    right.OrbitRadius - left.OrbitRadius).Min();
            bool planetScale = minPlanetRadius >= 9000.0 &&
                planets.Max(body => body.VisualRadius) >= 14000.0;
            bool planetSpacing = minPlanetGap >= 90000.0 &&
                planets.Min(body => body.OrbitRadius) >= 110000.0;

            double minMoonClearance = double.PositiveInfinity;
            bool moonClearance = true;
            foreach (StarSystemBodyDefinition moon in moons)
            {
                StarSystemBodyDefinition parent = planets.Single(planet =>
                    string.Equals(
                        planet.BodyId,
                        moon.ParentBodyId,
                        StringComparison.Ordinal));
                double clearance = moon.OrbitRadius -
                    parent.VisualRadius - moon.VisualRadius;
                minMoonClearance = Math.Min(minMoonClearance, clearance);
                moonClearance &= clearance >= 25000.0;
            }
            if (moons.Length == 0)
            {
                minMoonClearance = double.PositiveInfinity;
            }

            Vector2 mouseImpulse = ArcadeFlightAssistRuntime.AccumulateMouseSteering(
                Vector2.Zero,
                new Vector2(64.0f, -48.0f),
                0.0035f,
                2.25f,
                invertPitch: false,
                invertYaw: false);
            bool mouseSteering = Math.Abs(mouseImpulse.X) >= 0.30f &&
                Math.Abs(mouseImpulse.Y) >= 0.35f;
            Vector2 oneTick = ArcadeFlightAssistRuntime.DecayMouseSteering(
                mouseImpulse,
                7.5f,
                1.0f / 60.0f);
            Vector2 settled = mouseImpulse;
            for (int index = 0; index < 90; index++)
            {
                settled = ArcadeFlightAssistRuntime.DecayMouseSteering(
                    settled,
                    7.5f,
                    1.0f / 60.0f);
            }
            bool mouseRetention = oneTick.Length() >= mouseImpulse.Length() * 0.80f &&
                settled.Length() <= 0.01f;

            double farCruiseSpeed =
                InterplanetaryTravelRuntime.CalculateSafeCruiseSpeed(120000.0);
            double approachCruiseSpeed =
                InterplanetaryTravelRuntime.CalculateSafeCruiseSpeed(1200.0);
            bool playableCruise =
                InterplanetaryTravelRuntime.CruiseSpeedMetersPerSecond >= 500.0 &&
                farCruiseSpeed >= 500.0 &&
                approachCruiseSpeed < 280.0 &&
                InterplanetaryTravelRuntime.AssumedBrakeDecelerationMetersPerSecondSquared >= 30.0;

            string sourcePlanet = system.Planets.First().PlanetId;
            string targetPlanet = system.Planets.Skip(1).First().PlanetId;
            WorldSceneCoordinatorRuntime coordinator = new(
                WorldSceneContext.Create(
                    WorldSceneKind.Orbit,
                    system.SystemId,
                    sourcePlanet));
            bool physicalTransferGraph =
                coordinator.TryTransition(
                    WorldSceneContext.Create(
                        WorldSceneKind.InterplanetaryTransit,
                        system.SystemId,
                        sourcePlanet),
                    out _) == WorldSceneTransitionResult.Applied &&
                coordinator.TryTransition(
                    WorldSceneContext.Create(
                        WorldSceneKind.Orbit,
                        system.SystemId,
                        targetPlanet),
                    out _) == WorldSceneTransitionResult.Applied;

            PlanetSurfaceContentRuntime surface = new(
                environment,
                ecologyCatalog,
                poiCatalog);
            int landable = 0;
            int contentReady = 0;
            int minFlora = int.MaxValue;
            int minFauna = int.MaxValue;
            int minPois = int.MaxValue;
            int minResources = int.MaxValue;
            foreach (GalaxyPlanetDefinition planet in system.Planets)
            {
                PlanetEnvironmentProfile preview = environment.BuildProfile(
                    planet,
                    system.StarType);
                if (!preview.Landable)
                {
                    continue;
                }

                landable++;
                PlanetSurfaceContentProfile profile = surface.BuildProfile(
                    planet,
                    system.StarType);
                EcologyPlan ecology = surface.BuildEcologyPlan(profile);
                IReadOnlyList<PlanetaryPoiPlacement> pois = surface.BuildPoiPlan(profile);
                IReadOnlyList<PlanetSurfaceResourcePlacement> resourceWindow =
                    PlanetSurfaceWorldCompositionRuntime.BuildResourceWindow(
                        profile,
                        resources,
                        new PlanetSurfaceChunkCoordinate(0, 0));
                int fauna = ecology.ActiveFauna.Count + ecology.SimplifiedFauna.Count;
                minFlora = Math.Min(minFlora, ecology.Flora.Count);
                minFauna = Math.Min(minFauna, fauna);
                minPois = Math.Min(minPois, pois.Count);
                minResources = Math.Min(minResources, resourceWindow.Count);
                if (ecology.Flora.Count > 0 && fauna > 0 &&
                    pois.Count > 0 && resourceWindow.Count > 0)
                {
                    contentReady++;
                }
            }

            if (landable == 0)
            {
                minFlora = minFauna = minPois = minResources = 0;
            }
            bool landableSurfaceCoverage = landable >= 3 &&
                contentReady == landable && minFlora >= 180 &&
                minFauna > 0 && minPois >= 20 && minResources > 0;

            bool passed = planetScale && planetSpacing && moonClearance &&
                mouseSteering && mouseRetention && playableCruise &&
                physicalTransferGraph && landableSurfaceCoverage;
            return new OrbitalScaleMouseSurfaceAcceptanceReport(
                passed,
                planetScale,
                planetSpacing,
                moonClearance,
                mouseSteering,
                mouseRetention,
                playableCruise,
                physicalTransferGraph,
                landableSurfaceCoverage,
                landable,
                contentReady,
                minPlanetRadius,
                minPlanetGap,
                minMoonClearance,
                minFlora,
                minFauna,
                minPois,
                minResources,
                passed
                    ? "large readable orbital scale, pre-UI mouse steering, playable 600m/s scale-aware cruise and deterministic content on every landable system planet verified"
                    : "one or more TASK-178.6 orbital scale/mouse/multi-planet invariants failed");
        }
        catch (Exception exception)
        {
            return new OrbitalScaleMouseSurfaceAcceptanceReport(
                false, false, false, false, false, false, false, false, false,
                0, 0, 0.0, 0.0, 0.0, 0, 0, 0, 0,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
