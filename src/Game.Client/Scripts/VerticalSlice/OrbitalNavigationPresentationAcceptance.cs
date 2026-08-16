using System;
using System.Linq;

public sealed record OrbitalNavigationPresentationAcceptanceReport(
    bool Passed,
    bool RealTimeOrbitClock,
    bool PlanetSpacing,
    bool MoonCadence,
    bool VisualHierarchy,
    bool AssistDockCapture,
    bool LocalProxyPolicy,
    bool SpaceEnvironment,
    bool StationInterior,
    double MinimumPlanetOrbit,
    double MinimumPlanetSpacing,
    double MinimumMoonOrbit,
    double MinimumMoonRealPeriod,
    double MinimumPlanetVisualRadius,
    double MaximumMoonVisualRadius,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planetGap={MinimumPlanetSpacing:0}m moonPeriod={MinimumMoonRealPeriod:0}s"
        : $"FAIL clock={(RealTimeOrbitClock ? 1 : 0)} spacing={(PlanetSpacing ? 1 : 0)} " +
          $"moons={(MoonCadence ? 1 : 0)} dock={(AssistDockCapture ? 1 : 0)}";

    public string BuildOutputLine() =>
        $"TASK-178.2 orbital navigation/presentation acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"orbitClock={(RealTimeOrbitClock ? 1 : 0)}; planetSpacing={(PlanetSpacing ? 1 : 0)}; " +
        $"moonCadence={(MoonCadence ? 1 : 0)}; visualHierarchy={(VisualHierarchy ? 1 : 0)}; " +
        $"assistDock={(AssistDockCapture ? 1 : 0)}; localProxyPolicy={(LocalProxyPolicy ? 1 : 0)}; " +
        $"spaceEnvironment={(SpaceEnvironment ? 1 : 0)}; stationInterior={(StationInterior ? 1 : 0)}; " +
        $"minPlanetOrbit={MinimumPlanetOrbit:0.0}m; " +
        $"minPlanetGap={MinimumPlanetSpacing:0.0}m; minMoonOrbit={MinimumMoonOrbit:0.0}m; " +
        $"minMoonRealPeriod={MinimumMoonRealPeriod:0.0}s; planetVisualMin={MinimumPlanetVisualRadius:0.0}m; " +
        $"moonVisualMax={MaximumMoonVisualRadius:0.0}m; result={Result}";
}

public static class OrbitalNavigationPresentationAcceptanceRunner
{
    public static OrbitalNavigationPresentationAcceptanceReport Run(
        GalaxySystemDefinition system,
        bool stationInteriorReady)
    {
        ArgumentNullException.ThrowIfNull(system);
        StarSystemSimulationRuntime runtime = new(system);
        StarSystemBodyDefinition[] planets = runtime.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Planet)
            .OrderBy(body => body.OrbitRadius)
            .ToArray();
        StarSystemBodyDefinition[] moons = runtime.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Moon)
            .ToArray();

        double minPlanetOrbit = planets.Length == 0
            ? 0.0
            : planets.Min(body => body.OrbitRadius);
        double minPlanetSpacing = planets.Length <= 1
            ? double.PositiveInfinity
            : planets.Zip(planets.Skip(1), (left, right) =>
                right.OrbitRadius - left.OrbitRadius).Min();
        double minMoonOrbit = moons.Length == 0
            ? double.PositiveInfinity
            : moons.Min(body => body.OrbitRadius);
        double minMoonRealPeriod = moons.Length == 0
            ? double.PositiveInfinity
            : moons.Min(body => body.OrbitPeriodSeconds /
                StarSystemSimulationRuntime.OrbitTimeScale);
        double minPlanetVisual = planets.Length == 0
            ? 0.0
            : planets.Min(body => body.VisualRadius);
        double maxMoonVisual = moons.Length == 0
            ? 0.0
            : moons.Max(body => body.VisualRadius);
        double starVisual = runtime.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Star)
            .Select(body => body.VisualRadius)
            .DefaultIfEmpty(0.0)
            .Max();
        double maxPlanetVisual = planets.Length == 0
            ? 0.0
            : planets.Max(body => body.VisualRadius);

        bool realTimeOrbitClock =
            StarSystemSimulationRuntime.OrbitTimeScale <= 2.0;
        bool planetSpacing = planets.Length == system.Planets.Count &&
            minPlanetOrbit >= StarSystemSimulationRuntime.MinimumPlanetOrbitRadius &&
            minPlanetSpacing >= 4400.0;
        bool moonCadence = moons.All(body =>
                body.OrbitRadius >= StarSystemSimulationRuntime.MinimumMoonOrbitRadius &&
                body.OrbitPeriodSeconds / StarSystemSimulationRuntime.OrbitTimeScale >= 1200.0) &&
            minMoonOrbit >= StarSystemSimulationRuntime.MinimumMoonOrbitRadius;
        bool visualHierarchy = planets.All(body => body.VisualRadius >= 520.0) &&
            moons.All(body => body.VisualRadius is >= 150.0 and <= 240.0) &&
            minPlanetVisual >= Math.Max(520.0, maxMoonVisual * 2.8) &&
            starVisual >= Math.Max(2400.0, maxPlanetVisual * 1.35);
        bool assistDockCapture =
            StageOneVoyageRuntime.IsDockingCaptureReady(
                StageOneVoyageRuntime.DockingRangeMeters,
                StageOneVoyageRuntime.MaximumDockingSpeed) &&
            !StageOneVoyageRuntime.IsDockingCaptureReady(
                StageOneVoyageRuntime.DockingRangeMeters + 0.01,
                0.0) &&
            !StageOneVoyageRuntime.IsDockingCaptureReady(
                0.0,
                StageOneVoyageRuntime.MaximumDockingSpeed + 0.01);
        bool localProxyPolicy = StarSystemSimulationNode.LocalTrafficProxiesSuppressed;
        bool spaceEnvironment = new[]
        {
            WorldSceneKind.Orbit,
            WorldSceneKind.InterplanetaryTransit,
            WorldSceneKind.HyperspaceTransit,
            WorldSceneKind.StationInterior
        }.Select(WorldSceneEnvironmentPresentationRuntime.Resolve)
            .All(WorldSceneEnvironmentPresentationRuntime.IsVacuumProfileValid);

        bool passed = realTimeOrbitClock && planetSpacing && moonCadence &&
            visualHierarchy && assistDockCapture && localProxyPolicy &&
            spaceEnvironment && stationInteriorReady;
        return new OrbitalNavigationPresentationAcceptanceReport(
            passed,
            realTimeOrbitClock,
            planetSpacing,
            moonCadence,
            visualHierarchy,
            assistDockCapture,
            localProxyPolicy,
            spaceEnvironment,
            stationInteriorReady,
            minPlanetOrbit,
            minPlanetSpacing,
            minMoonOrbit,
            minMoonRealPeriod,
            minPlanetVisual,
            maxMoonVisual,
            passed
                ? "orbital scale, cadence, vacuum presentation, assist docking and station interior verified"
                : "one or more TASK-178.2 orbital navigation/presentation invariants failed");
    }
}
