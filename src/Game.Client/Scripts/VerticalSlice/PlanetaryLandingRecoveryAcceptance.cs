using System;
using System.Linq;

public sealed record PlanetaryLandingRecoveryAcceptanceReport(
    bool Passed,
    bool RestoreSafe,
    bool PlanetScale,
    bool MoonClearance,
    bool OrbitalEntry,
    bool SurfaceHandoff,
    bool VoyagePath,
    bool LightingContinuity,
    double MinimumPlanetVisualRadius,
    double MaximumPlanetVisualRadius,
    double MinimumMoonClearance,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planet={MinimumPlanetVisualRadius:0}..{MaximumPlanetVisualRadius:0}m entry=2-stage"
        : $"FAIL restore={(RestoreSafe ? 1 : 0)} scale={(PlanetScale ? 1 : 0)} " +
          $"entry={(OrbitalEntry ? 1 : 0)} handoff={(SurfaceHandoff ? 1 : 0)}";

    public string BuildOutputLine() =>
        $"TASK-178.4 planetary landing/lighting acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"restoreSafe={(RestoreSafe ? 1 : 0)}; planetScale={(PlanetScale ? 1 : 0)}; " +
        $"moonClearance={(MoonClearance ? 1 : 0)}; orbitalEntry={(OrbitalEntry ? 1 : 0)}; " +
        $"surfaceHandoff={(SurfaceHandoff ? 1 : 0)}; voyagePath={(VoyagePath ? 1 : 0)}; " +
        $"lightingContinuity={(LightingContinuity ? 1 : 0)}; " +
        $"planetVisual={MinimumPlanetVisualRadius:0}..{MaximumPlanetVisualRadius:0}m; " +
        $"moonClearanceMin={MinimumMoonClearance:0}m; result={Result}";
}

public static class PlanetaryLandingRecoveryAcceptanceRunner
{
    public static PlanetaryLandingRecoveryAcceptanceReport Run()
    {
        WorldSceneCoordinatorRuntime coordinator = new(
            WorldSceneContext.Create(
                WorldSceneKind.Surface,
                "system.restore",
                "planet.restore"));
        coordinator.Restore(WorldSceneContext.Create(
            WorldSceneKind.StationInterior,
            "system.restore",
            "planet.restore"));
        bool restoreSafe =
            coordinator.Current.Kind == WorldSceneKind.StationInterior &&
            coordinator.RejectedTransitions == 0;

        GalaxyNavigationRuntime galaxy = new();
        StarSystemSimulationRuntime system = new(galaxy.CurrentSystem);
        StarSystemBodyDefinition[] planets = system.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Planet)
            .ToArray();
        StarSystemBodyDefinition[] moons = system.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Moon)
            .ToArray();
        double minPlanet = planets.Min(body => body.VisualRadius);
        double maxPlanet = planets.Max(body => body.VisualRadius);
        bool planetScale =
            minPlanet >= 520.0 &&
            maxPlanet >= 720.0 &&
            planets.Min(body => body.OrbitRadius) >=
                StarSystemSimulationRuntime.MinimumPlanetOrbitRadius;

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
            moonClearance &= clearance >= 250.0;
        }
        if (moons.Length == 0)
        {
            minMoonClearance = double.PositiveInfinity;
        }

        bool orbitalEntry =
            PlanetaryApproachRuntime.IsOrbitalEntryCaptureReady(
                PlanetaryApproachRuntime.OrbitalEntryCaptureRadiusMeters,
                PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed) &&
            !PlanetaryApproachRuntime.IsOrbitalEntryCaptureReady(
                PlanetaryApproachRuntime.OrbitalEntryCaptureRadiusMeters + 0.1,
                0.0) &&
            !PlanetaryApproachRuntime.IsOrbitalEntryCaptureReady(
                0.0,
                PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed + 0.1);
        bool surfaceHandoff =
            PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters >= 150.0 &&
            PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters <=
                SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters &&
            StageOneVoyageRuntime.PlanetApproachPositionY ==
                PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters;

        StageOneVoyageRuntime voyage = new(new StageOneVoyageSaveData(
            StageOneVoyageLocation.InboundFlight,
            Piloted: true,
            StationVisited: true,
            StationVisitedThisLoop: true,
            TakeoffCount: 1,
            DockingCount: 1,
            LandingCount: 0,
            CompletedLoops: 0,
            PositionX: StageOneVoyageRuntime.StationDockPositionX,
            PositionY: StageOneVoyageRuntime.StationDockPositionY,
            PositionZ: StageOneVoyageRuntime.StationUndockPositionZ,
            RotationX: 0.0,
            RotationY: Math.PI,
            RotationZ: 0.0,
            VelocityX: 0.0,
            VelocityY: 0.0,
            VelocityZ: 0.0,
            LastCheckpoint: "flight.inbound"));
        voyage.ArriveAtPlanetaryApproach();
        bool voyagePath = voyage.IsPlanetarySurfaceApproach &&
            Math.Abs(voyage.PositionY -
                PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters) < 0.0001 &&
            string.Equals(voyage.LastCheckpoint, "planet.approach", StringComparison.Ordinal);

        WorldSceneEnvironmentPresentationProfile orbit =
            WorldSceneEnvironmentPresentationRuntime.Resolve(WorldSceneKind.Orbit);
        bool lightingContinuity =
            OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters -
                OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters >= 400.0 &&
            orbit.AmbientEnergy is >= 0.20 and <= 0.32 &&
            orbit.DirectionalEnergy is >= 0.85 and <= 1.0;

        bool passed = restoreSafe && planetScale && moonClearance &&
            orbitalEntry && surfaceHandoff && voyagePath && lightingContinuity;
        return new PlanetaryLandingRecoveryAcceptanceReport(
            passed,
            restoreSafe,
            planetScale,
            moonClearance,
            orbitalEntry,
            surfaceHandoff,
            voyagePath,
            lightingContinuity,
            minPlanet,
            maxPlanet,
            minMoonClearance,
            passed
                ? "restore-safe station context, readable planet scale, two-stage orbital entry and continuous orbital lighting verified"
                : "one or more TASK-178.4 planetary landing/lighting invariants failed");
    }
}
