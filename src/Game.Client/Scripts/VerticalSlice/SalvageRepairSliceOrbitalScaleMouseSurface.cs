using Godot;

public partial class SalvageRepairSlice
{
    private string _orbitalScaleMouseSurfaceAcceptanceHud = "READY";
    private bool? _orbitalScaleMouseSurfaceAcceptancePassed;

    private void PrintOrbitalScaleMouseSurfaceReady()
    {
        float radius = 0.0f;
        if (_starSystemSimulationNode is not null && _galaxyNavigationRuntime is not null)
        {
            _starSystemSimulationNode.TryGetBodyDisplaySphere(
                GalaxyNavigation.CurrentPlanetId,
                out _,
                out _,
                out radius);
        }

        GD.Print(
            "TASK-178.6 orbital scale/mouse/multi-planet READY: " +
            $"planet={GalaxyNavigation.CurrentPlanetId}; radius={radius:0}m; " +
            $"planetOrbitMin={StarSystemSimulationRuntime.MinimumPlanetOrbitRadius / 1000.0:0}km; " +
            $"planetGap={StarSystemSimulationRuntime.PlanetOrbitSpacing / 1000.0:0}km; " +
            $"moonClearance={StarSystemSimulationRuntime.MinimumMoonSurfaceClearance / 1000.0:0}km; " +
            $"mouseGain={(_voyageShip?.MouseFlightGain ?? 0.0f):0.00}; mousePath=_Input; " +
            "surfaceContent=planet-transactional; F5=acceptance.");
    }

    private void RunOrbitalScaleMouseSurfaceAcceptance()
    {
        if (_galaxyNavigationRuntime is null || _planetEnvironmentRuntime is null ||
            _ecologyCatalog is null || _planetaryPoiCatalog is null ||
            _contentCatalog is null)
        {
            _orbitalScaleMouseSurfaceAcceptancePassed = false;
            _orbitalScaleMouseSurfaceAcceptanceHud = "FAIL unavailable";
            return;
        }

        OrbitalScaleMouseSurfaceAcceptanceReport report =
            OrbitalScaleMouseSurfaceAcceptanceRunner.Run(
                GalaxyNavigation.CurrentSystem,
                PlanetEnvironment,
                EcologyCatalog,
                PlanetaryPoiCatalog,
                ContentCatalog.Resources);

        bool liveMouse = _voyageShip is not null &&
            ArcadeShipController.StatefulVirtualFlightStickEnabled &&
            ArcadeShipController.SpringCenteredVirtualFlightStickEnabled &&
            _voyageShip.MouseFlightGain >= 1.0f &&
            _voyageShip.MouseVirtualStickAutoCenterDelaySeconds > 0.0f &&
            _voyageShip.MouseVirtualStickAutoCenterDelaySeconds <= 0.25f &&
            _voyageShip.MouseVirtualStickAutoCenterRate >= 4.0f;
        bool liveMouseEvidence = (_voyageShip?.MouseSteeringSampleCount ?? 0) > 0;
        StarSystemBodyDefinition definition = null!;
        float radius = 0.0f;
        bool liveScale = _starSystemSimulationNode is not null &&
            _starSystemSimulationNode.DisplayAnchor.Z >= 15000.0f &&
            _starSystemSimulationNode.TryGetBodyDisplaySphere(
                GalaxyNavigation.CurrentPlanetId,
                out definition,
                out _,
                out radius) &&
            definition.Kind == StarSystemBodyKind.Planet && radius >= 9000.0f;
        bool passed = report.Passed && liveMouse && liveScale;
        _orbitalScaleMouseSurfaceAcceptancePassed = passed;
        _orbitalScaleMouseSurfaceAcceptanceHud = passed
            ? $"PASS planet={radius:0}m mouse=1 content={report.ContentReadyPlanets}/{report.LandablePlanets}"
            : $"FAIL model={(report.Passed ? 1 : 0)} mouse={(liveMouse ? 1 : 0)} scale={(liveScale ? 1 : 0)}";

        string output = report.BuildOutputLine().Replace(
            $"acceptance {(report.Passed ? "PASS" : "FAIL")}:",
            $"acceptance {(passed ? "PASS" : "FAIL")}:") +
            $" liveMouse={(liveMouse ? 1 : 0)}; liveMouseEvidence={(liveMouseEvidence ? 1 : 0)}; liveScale={(liveScale ? 1 : 0)}; " +
            $"liveRadius={radius:0}m; mouseSamples={_voyageShip?.MouseSteeringSampleCount ?? 0}; manualTransfers={_manualCrossPlanetEntryCount}; " +
            $"lastManualTarget={(_lastManualCrossPlanetTarget.Length == 0 ? "none" : _lastManualCrossPlanetTarget)}.";
        if (passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }
}
