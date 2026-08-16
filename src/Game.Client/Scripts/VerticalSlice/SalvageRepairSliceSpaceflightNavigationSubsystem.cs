using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _spaceflightNavigationSubsystemAcceptanceHud = "READY";
    private bool _spaceflightNavigationSubsystemAcceptanceRequested;
    private bool? _spaceflightNavigationSubsystemAcceptancePassed;
    private bool _spaceflightNavigationSubsystemReadyPrinted;

    private void UpdateSpaceflightNavigationSubsystemRuntime()
    {
        if (_spaceflightNavigationSubsystemReadyPrinted ||
            _worldSceneCoordinatorNode is null ||
            _starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null ||
            _interplanetaryTravelRuntime is null ||
            _stageOneVoyageRuntime is null ||
            _shipSystemsRuntime is null ||
            _voyageShip is null)
        {
            return;
        }

        if (!TryEvaluateLiveSpaceflightNavigation(
                out SpaceflightNavigationLiveSnapshot live))
        {
            return;
        }

        _spaceflightNavigationSubsystemReadyPrinted = true;
        GD.Print(
            "TASK-178 spaceflight navigation subsystem READY: " +
            $"system={live.SystemId}; planet={live.PlanetId}; world={live.WorldKind}; " +
            $"commissioned={(ShipSystems.Commissioned ? 1 : 0)}; " +
            $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
            $"hyperReady={(ShipSystems.HyperspaceReady ? 1 : 0)}; " +
            $"travelPhase={InterplanetaryTravel.Phase}; selectionSync={(live.SelectionSync ? 1 : 0)}; " +
            $"pilotControl={(live.PilotControl ? 1 : 0)}; residency={(live.Residency ? 1 : 0)}; F5=acceptance.");
    }

    private void RequestSpaceflightNavigationSubsystemAcceptance()
    {
        _spaceflightNavigationSubsystemAcceptanceRequested = true;
        _spaceflightNavigationSubsystemAcceptancePassed = null;
        _spaceflightNavigationSubsystemAcceptanceHud = "RUNNING";

        // These three reports are synchronous in the F5 orchestration. If one
        // is unavailable, do not leave the aggregate acceptance waiting forever.
        if (_starSystemSimulationAcceptanceReport is null ||
            _interplanetaryTravelAcceptanceReport is null ||
            _worldSceneCoordinatorAcceptanceReport is null)
        {
            FailSpaceflightNavigationSubsystemAcceptance(
                "one or more synchronous TASK-128/148/152 reports are unavailable");
        }
    }

    private void UpdateSpaceflightNavigationSubsystemAcceptance()
    {
        if (!_spaceflightNavigationSubsystemAcceptanceRequested)
        {
            return;
        }

        bool asynchronousReportsReady =
            _shipSystemsAcceptanceReport is not null &&
            _stageOneVoyageAcceptanceReport is not null &&
            _galaxyNavigationAcceptanceReport is not null;
        if (!asynchronousReportsReady)
        {
            bool stillRunning =
                _shipSystemsAcceptanceTask is not null ||
                _stageOneVoyageAcceptanceTask is not null ||
                _galaxyNavigationAcceptanceTask is not null;
            if (!stillRunning)
            {
                FailSpaceflightNavigationSubsystemAcceptance(
                    "one or more asynchronous TASK-110/112/114 reports did not complete");
            }
            return;
        }

        if (_starSystemSimulationAcceptanceReport is null ||
            _interplanetaryTravelAcceptanceReport is null ||
            _worldSceneCoordinatorAcceptanceReport is null)
        {
            FailSpaceflightNavigationSubsystemAcceptance(
                "one or more TASK-128/148/152 reports are unavailable");
            return;
        }

        SpaceflightNavigationSubsystemModelAcceptanceReport model =
            SpaceflightNavigationSubsystemAcceptanceRunner.Run(
                _shipSystemsAcceptanceReport,
                _stageOneVoyageAcceptanceReport,
                _galaxyNavigationAcceptanceReport,
                _starSystemSimulationAcceptanceReport,
                _interplanetaryTravelAcceptanceReport,
                _worldSceneCoordinatorAcceptanceReport);

        bool liveAvailable = TryEvaluateLiveSpaceflightNavigation(
            out SpaceflightNavigationLiveSnapshot live);
        bool passed = model.Passed && liveAvailable && live.AllPassed;

        _spaceflightNavigationSubsystemAcceptanceHud = passed
            ? $"PASS contracts={model.ContractsPassed}/{model.ContractsTotal} live=8/8"
            : $"FAIL contracts={model.ContractsPassed}/{model.ContractsTotal} " +
              $"selection={(live.SelectionSync ? 1 : 0)} world={(live.WorldContext ? 1 : 0)} " +
              $"star={(live.StarSystemSync ? 1 : 0)} voyage={(live.ShipVoyageSync ? 1 : 0)} " +
              $"pilotControl={(live.PilotControl ? 1 : 0)}";

        string output =
            $"TASK-178 spaceflight navigation subsystem acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"contracts={model.ContractsPassed}/{model.ContractsTotal}; " +
            $"ship={(model.ShipSystemsContract ? 1 : 0)}; voyage={(model.VoyageContract ? 1 : 0)}; " +
            $"galaxy={(model.GalaxyContract ? 1 : 0)}; starSystem={(model.StarSystemContract ? 1 : 0)}; " +
            $"interplanetary={(model.InterplanetaryContract ? 1 : 0)}; worldScene={(model.WorldSceneContract ? 1 : 0)}; " +
            $"readinessChain={(model.ReadinessChain ? 1 : 0)}; fuelChain={(model.FuelChain ? 1 : 0)}; " +
            $"transitionChain={(model.TransitionChain ? 1 : 0)}; persistenceChain={(model.PersistenceChain ? 1 : 0)}; " +
            $"navigationIdentity={(model.NavigationIdentity ? 1 : 0)}; boundedResidency={(model.BoundedResidency ? 1 : 0)}; " +
            $"selectionSync={(live.SelectionSync ? 1 : 0)}; worldContext={(live.WorldContext ? 1 : 0)}; " +
            $"starSystemSync={(live.StarSystemSync ? 1 : 0)}; shipVoyageSync={(live.ShipVoyageSync ? 1 : 0)}; " +
            $"currentPlanetScope={(live.CurrentPlanetScope ? 1 : 0)}; targetScope={(live.TargetScope ? 1 : 0)}; " +
            $"pilotControl={(live.PilotControl ? 1 : 0)}; liveResidency={(live.Residency ? 1 : 0)}; " +
            $"world={live.WorldKind}; system={live.SystemId}; planet={live.PlanetId}; " +
            $"result={(passed ? "spaceflight and navigation stack closed as one coherent runtime subsystem" : model.Result)}";

        if (passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }

        _spaceflightNavigationSubsystemAcceptancePassed = passed;
        _spaceflightNavigationSubsystemAcceptanceRequested = false;
        UpdateCombinedCatalogAndShipAcceptanceState();
    }

    private void FailSpaceflightNavigationSubsystemAcceptance(string reason)
    {
        _spaceflightNavigationSubsystemAcceptanceHud = $"FAIL {reason}";
        _spaceflightNavigationSubsystemAcceptancePassed = false;
        _spaceflightNavigationSubsystemAcceptanceRequested = false;
        GD.PushError(
            "TASK-178 spaceflight navigation subsystem acceptance FAIL: " +
            $"result={reason}");
        UpdateCombinedCatalogAndShipAcceptanceState();
    }

    private bool TryEvaluateLiveSpaceflightNavigation(
        out SpaceflightNavigationLiveSnapshot live)
    {
        if (_worldSceneCoordinatorNode is null ||
            _starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null ||
            _interplanetaryTravelRuntime is null ||
            _stageOneVoyageRuntime is null ||
            _shipSystemsRuntime is null ||
            _voyageShip is null)
        {
            live = SpaceflightNavigationLiveSnapshot.Unavailable;
            return false;
        }

        WorldSceneContext expected = ResolveWorldSceneContext();
        WorldSceneCoordinatorDiagnostics world =
            _worldSceneCoordinatorNode.CreateDiagnostics();
        StarSystemSimulationDiagnostics star =
            _starSystemSimulationNode.CreateDiagnostics();

        bool selectionSync =
            InterplanetaryTravel.IsSelectionConsistentWith(GalaxyNavigation);
        bool worldContext =
            world.SingleScene &&
            world.ShellMatchesContext &&
            world.HostChildren == 1 &&
            world.Kind == expected.Kind &&
            string.Equals(world.SystemId, expected.SystemId, StringComparison.Ordinal) &&
            string.Equals(world.PlanetId, expected.PlanetId, StringComparison.Ordinal);
        bool starSystemSync =
            string.Equals(
                star.SystemId,
                GalaxyNavigation.CurrentSystem.SystemId,
                StringComparison.Ordinal) &&
            star.PlanetBodies == GalaxyNavigation.CurrentSystem.Planets.Count &&
            string.Equals(
                star.FocusPlanetId,
                GetActiveDeveloperPlanetId(),
                StringComparison.Ordinal);
        bool shipVoyageSync =
            !StageOneVoyage.Piloted ||
            (ShipSystems.Commissioned && ShipSystems.FlightReady);
        bool currentPlanetScope = GalaxyNavigation.CurrentSystem.Planets.Any(
            planet => string.Equals(
                planet.PlanetId,
                GalaxyNavigation.CurrentPlanetId,
                StringComparison.Ordinal));
        bool targetScope = string.IsNullOrWhiteSpace(
                GalaxyNavigation.SelectedPlanetId) ||
            GalaxyNavigation.CurrentSystem.Planets.Any(planet => string.Equals(
                planet.PlanetId,
                GalaxyNavigation.SelectedPlanetId,
                StringComparison.Ordinal));
        bool parked = StageOneVoyage.Piloted && StageOneVoyage.Location is
            StageOneVoyageLocation.PlanetSurface or
            StageOneVoyageLocation.OrbitalStation;
        bool pilotControl = !StageOneVoyage.Piloted
            ? !_voyageShip.PilotEnabled
            : parked
                ? _voyageShip.PilotEnabled &&
                  _voyageShip.ParkedControlLocked &&
                  !_voyageShip.IsPhysicsProcessing() &&
                  !_voyageShip.ExternalControlActive
                : _voyageNavigationAssist
                    ? _voyageShip.PilotEnabled &&
                      !_voyageShip.ParkedControlLocked &&
                      _voyageShip.ExternalControlActive
                    : _voyageShip.ManualInputOwnershipActive &&
                      _voyageShip.IsPhysicsProcessing();
        bool residency = WorldResidencyPolicyMatches();

        live = new SpaceflightNavigationLiveSnapshot(
            selectionSync,
            worldContext,
            starSystemSync,
            shipVoyageSync,
            currentPlanetScope,
            targetScope,
            pilotControl,
            residency,
            world.Kind,
            world.SystemId,
            world.PlanetId);
        return selectionSync && worldContext && starSystemSync &&
            shipVoyageSync && currentPlanetScope && targetScope &&
            pilotControl && residency;
    }

    private readonly record struct SpaceflightNavigationLiveSnapshot(
        bool SelectionSync,
        bool WorldContext,
        bool StarSystemSync,
        bool ShipVoyageSync,
        bool CurrentPlanetScope,
        bool TargetScope,
        bool PilotControl,
        bool Residency,
        WorldSceneKind WorldKind,
        string SystemId,
        string PlanetId)
    {
        public static SpaceflightNavigationLiveSnapshot Unavailable => new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            WorldSceneKind.Surface,
            string.Empty,
            string.Empty);

        public bool AllPassed =>
            SelectionSync &&
            WorldContext &&
            StarSystemSync &&
            ShipVoyageSync &&
            CurrentPlanetScope &&
            TargetScope &&
            PilotControl &&
            Residency;
    }
}
