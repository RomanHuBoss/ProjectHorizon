using System;
using System.Linq;

public sealed record StarSystemSimulationAcceptanceReport(
    bool Passed,
    bool DeterministicGeneration,
    bool BodyCoverage,
    bool MoonBounds,
    bool AnalyticOrbits,
    bool RepresentationLevels,
    bool SingleDetailedPlanet,
    bool SystemTransition,
    bool VisualProjection,
    bool RuntimeSamples,
    bool SurfaceActivation,
    bool ActivationPipeline,
    int Bodies,
    int Planets,
    int Moons,
    int Stations,
    int ShipContacts,
    int VisualNodes,
    int Rebuilds,
    string Result);

public static class StarSystemSimulationAcceptanceRunner
{
    public static StarSystemSimulationAcceptanceReport Run(
        GalaxyNavigationRuntime navigation,
        StarSystemSimulationNode liveNode,
        bool expectedSurfaceActivation,
        bool activationPipelineComplete)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(liveNode);
        try
        {
            GalaxySystemDefinition current = navigation.CurrentSystem;
            StarSystemSimulationRuntime left = new(current, 0.0);
            StarSystemSimulationRuntime right = new(current, 0.0);
            bool deterministicGeneration = left.Definitions.SequenceEqual(
                right.Definitions);
            bool bodyCoverage =
                left.PlanetCount == current.Planets.Count &&
                left.PlanetCount is >= 1 and <= 8 &&
                left.StationCount is >= 1 and <= 3 &&
                left.ShipContactCount is >= 4 and <=
                    StarSystemSimulationRuntime.MaximumShipContacts;
            bool moonBounds = current.Planets.All(planet =>
                planet.MoonCount is >= 0 and <= 4) &&
                left.MoonCount == current.Planets.Sum(planet => planet.MoonCount);

            GalaxyPlanetDefinition firstPlanet = current.Planets[0];
            StarSystemBodyDefinition planetDefinition = left.Definitions.First(
                definition => string.Equals(
                    definition.BodyId,
                    firstPlanet.PlanetId,
                    StringComparison.Ordinal));
            SystemDouble3 positionA = left.EvaluateBodyPosition(
                firstPlanet.PlanetId,
                0.0);
            SystemDouble3 positionB = left.EvaluateBodyPosition(
                firstPlanet.PlanetId,
                Math.Max(30.0, planetDefinition.OrbitPeriodSeconds * 0.25));
            double radiusA = positionA.Length();
            double radiusB = positionB.Length();
            bool analyticOrbits =
                (positionB - positionA).Length() > 0.5 &&
                Math.Abs(radiusA - planetDefinition.OrbitRadius) < 0.001 &&
                Math.Abs(radiusB - planetDefinition.OrbitRadius) < 0.001;

            GalaxySystemDefinition coverageSystem = FindCoverageSystem(navigation);
            StarSystemSimulationRuntime coverage = new(coverageSystem, 0.0);
            string coverageFocus = coverageSystem.Planets[0].PlanetId;
            string coverageStar = $"{coverageSystem.SystemId}.star";
            StarSystemSimulationSnapshot coverageSnapshot = coverage.CreateSnapshot(
                coverageStar,
                coverageFocus,
                detailedPlanetRequested: false);
            bool representationLevels =
                coverageSnapshot.ProxyCount > 0 &&
                coverageSnapshot.MarkerCount > 0 &&
                coverageSnapshot.StatisticalCount > 0;
            StarSystemSimulationSnapshot detailSnapshot = coverage.CreateSnapshot(
                coverageFocus,
                coverageFocus,
                detailedPlanetRequested: true);
            bool singleDetailedPlanet =
                detailSnapshot.DetailedPlanetCount == 1 &&
                detailSnapshot.Bodies.Count(body =>
                    body.Representation == StarSystemRepresentation.DetailedPlanet &&
                    body.Definition.Kind != StarSystemBodyKind.Planet) == 0;

            GalaxySystemDefinition destination = navigation.GenerateSystem(
                current.SectorX + 1,
                current.SectorY,
                current.SectorZ);
            StarSystemSimulationRuntime destinationRuntime = new(destination, 0.0);
            bool systemTransition =
                !string.Equals(
                    destinationRuntime.SystemId,
                    left.SystemId,
                    StringComparison.Ordinal) &&
                destinationRuntime.Definitions.All(definition =>
                    !left.Definitions.Any(original => string.Equals(
                        original.BodyId,
                        definition.BodyId,
                        StringComparison.Ordinal)));

            StarSystemSimulationDiagnostics diagnostics = liveNode.CreateDiagnostics();
            bool visualProjection =
                string.Equals(
                    diagnostics.SystemId,
                    current.SystemId,
                    StringComparison.Ordinal) &&
                diagnostics.GeneratedBodies == diagnostics.VisualNodes &&
                diagnostics.PlanetBodies == current.Planets.Count;
            bool runtimeSamples = diagnostics.RuntimeSamples > 0;
            bool surfaceActivation =
                diagnostics.SurfaceRuntimeActive == expectedSurfaceActivation &&
                string.Equals(
                    diagnostics.FocusPlanetId,
                    navigation.CurrentPlanetId,
                    StringComparison.Ordinal);

            bool passed = deterministicGeneration &&
                bodyCoverage &&
                moonBounds &&
                analyticOrbits &&
                representationLevels &&
                singleDetailedPlanet &&
                systemTransition &&
                visualProjection &&
                runtimeSamples &&
                surfaceActivation &&
                activationPipelineComplete;
            string result = passed
                ? "deterministic star-system hierarchy, analytic orbits, representation LOD and single-planet activation verified"
                : "one or more star-system simulation invariants failed";
            return new StarSystemSimulationAcceptanceReport(
                passed,
                deterministicGeneration,
                bodyCoverage,
                moonBounds,
                analyticOrbits,
                representationLevels,
                singleDetailedPlanet,
                systemTransition,
                visualProjection,
                runtimeSamples,
                surfaceActivation,
                activationPipelineComplete,
                diagnostics.GeneratedBodies,
                diagnostics.PlanetBodies,
                diagnostics.MoonBodies,
                diagnostics.StationBodies,
                diagnostics.ShipContacts,
                diagnostics.VisualNodes,
                diagnostics.Rebuilds,
                result);
        }
        catch (Exception exception)
        {
            return new StarSystemSimulationAcceptanceReport(
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                exception.Message);
        }
    }

    private static GalaxySystemDefinition FindCoverageSystem(
        GalaxyNavigationRuntime navigation)
    {
        GalaxySystemDefinition current = navigation.CurrentSystem;
        GalaxySystemDefinition best = current;
        for (int x = -4; x <= 4; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                for (int z = -4; z <= 4; z++)
                {
                    GalaxySystemDefinition candidate = navigation.GenerateSystem(
                        current.SectorX + x,
                        current.SectorY + y,
                        current.SectorZ + z);
                    if (candidate.Planets.Count > best.Planets.Count)
                    {
                        best = candidate;
                    }
                    if (candidate.Planets.Count >= 7)
                    {
                        return candidate;
                    }
                }
            }
        }
        return best;
    }
}
