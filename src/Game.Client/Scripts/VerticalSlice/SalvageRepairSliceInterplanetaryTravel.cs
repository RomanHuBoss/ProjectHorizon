using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private InterplanetaryTravelRuntime? _interplanetaryTravelRuntime;
    private InterplanetaryTravelAcceptanceReport? _interplanetaryTravelAcceptanceReport;
    private string _interplanetaryTravelAcceptanceHud = "READY";

    private InterplanetaryTravelRuntime InterplanetaryTravel =>
        _interplanetaryTravelRuntime ??
        throw new InvalidOperationException(
            "Interplanetary travel runtime is unavailable.");

    private void InitializeInterplanetaryTravelRuntime()
    {
        _interplanetaryTravelRuntime = new InterplanetaryTravelRuntime();
        _interplanetaryTravelRuntime.SynchronizeSelection(GalaxyNavigation);
        GD.Print(
            "TASK-152 interplanetary travel READY: " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"current={GalaxyNavigation.CurrentPlanetId}; " +
            $"target={GalaxyNavigation.SelectedPlanetId}; " +
            $"planets={GalaxyNavigation.CurrentSystem.Planets.Count}; " +
            "selection=M/System+Enter; cruise=K navigation-assist; " +
            "handoff=Orbit->InterplanetaryTransit->Orbit; persistence=galaxy-navigation; F5=acceptance.");
    }

    private bool SelectInterplanetaryPlanetTarget(
        GalaxyPlanetDefinition planet,
        out string result)
    {
        if (_interplanetaryTravelRuntime is null)
        {
            result = L("ui.interplanetary.unavailable");
            return false;
        }

        if (InterplanetaryTravel.IsCruising)
        {
            result = L("ui.interplanetary.selection_locked");
            return false;
        }

        bool selected = GalaxyNavigation.TrySelectPlanetDestination(
            planet.PlanetId,
            out _);
        InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);
        if (!selected)
        {
            result = string.Equals(
                    planet.Archetype,
                    "gas_giant",
                    StringComparison.Ordinal)
                ? L("ui.interplanetary.gas_giant")
                : LF("ui.interplanetary.target_rejected", ("planet", planet.PlanetId));
            return false;
        }

        if (string.IsNullOrWhiteSpace(GalaxyNavigation.SelectedPlanetId))
        {
            result = L("ui.interplanetary.target_cleared");
            QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
            return true;
        }

        result = LF(
            "ui.interplanetary.target_selected",
            ("planet", planet.PlanetId),
            ("archetype", LocalizeGalaxyPlanetArchetype(planet.Archetype)));
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        return true;
    }

    private bool TryApplyInterplanetaryNavigationAssist()
    {
        if (_interplanetaryTravelRuntime is null ||
            _starSystemSimulationNode is null ||
            _voyageShip is null ||
            _stageOneVoyageRuntime is null ||
            _galaxyNavigationRuntime is null ||
            string.IsNullOrWhiteSpace(GalaxyNavigation.SelectedPlanetId))
        {
            return false;
        }

        InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);
        string targetPlanetId = GalaxyNavigation.SelectedPlanetId;
        if (!_starSystemSimulationNode.TryGetBodyApproachPoint(
                targetPlanetId,
                _voyageShip.GlobalPosition,
                PlanetaryApproachRuntime.OrbitalEntryClearanceMeters,
                out Vector3 target,
                out Vector3 targetCenter,
                out float targetDisplayRadius))
        {
            _status = LF(
                "ui.interplanetary.target_proxy_unavailable",
                ("planet", targetPlanetId));
            _voyageShip.SetExternalCommand(new ShipControlCommand(
                0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, false, true));
            return true;
        }

        Vector3 offset = target - _voyageShip.GlobalPosition;
        double distance = offset.Length();
        if (!InterplanetaryTravel.IsCruising)
        {
            InterplanetaryTravelActionResult begin =
                InterplanetaryTravel.TryBeginCruise(
                    GalaxyNavigation,
                    StageOneVoyage,
                    ShipSystems,
                    distance,
                    out string beginResult);
            if (begin != InterplanetaryTravelActionResult.Applied)
            {
                _status = beginResult;
                _voyageNavigationAssist = false;
                _voyageShip.ClearExternalCommand();
                return true;
            }

            QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
            _status = LF(
                "ui.interplanetary.cruise_started",
                ("planet", targetPlanetId),
                ("fuel", InterplanetaryTravel.FuelCost.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)));
            GD.Print(
                "TASK-152 interplanetary cruise BEGIN: " +
                $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
                $"source={InterplanetaryTravel.SourcePlanetId}; " +
                $"target={InterplanetaryTravel.TargetPlanetId}; " +
                $"distance={InterplanetaryTravel.PlannedDistanceMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
                $"planetRadius={targetDisplayRadius.ToString("0", CultureInfo.InvariantCulture)}m; " +
                $"entryClearance={PlanetaryApproachRuntime.OrbitalEntryClearanceMeters.ToString("0", CultureInfo.InvariantCulture)}m; " +
                $"fuelCost={InterplanetaryTravel.FuelCost.ToString("0.00", CultureInfo.InvariantCulture)}; " +
                $"fuelRemaining={ShipSystems.Fuel.ToString("0.00", CultureInfo.InvariantCulture)}.");
        }

        if (distance > 0.25)
        {
            _voyageShip.LookAt(target, SurfaceLocalDirectionToWorld(Vector3.Up).Normalized());
        }

        InterplanetaryGuidance guidance = InterplanetaryTravel.BuildGuidance(
            distance,
            _voyageShip.Speed);
        _voyageShip.SetExternalCommand(new ShipControlCommand(
            guidance.Forward,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            guidance.Boost,
            guidance.Brake));

        if (!guidance.ArrivalReady)
        {
            return true;
        }

        string sourcePlanetId = InterplanetaryTravel.SourcePlanetId;
        string destinationPlanetId = InterplanetaryTravel.TargetPlanetId;
        double plannedDistance = InterplanetaryTravel.PlannedDistanceMeters;
        CaptureCurrentPlanetSurfaceState();
        if (!InterplanetaryTravel.TryCompleteArrival(
                GalaxyNavigation,
                distance,
                out string arrivalResult))
        {
            _status = arrivalResult;
            InterplanetaryTravel.Cancel(
                keepSelectedTarget: true,
                GalaxyNavigation);
            _voyageNavigationAssist = false;
            _voyageShip.ClearExternalCommand();
            return true;
        }

        StageOneVoyage.ArriveAtPlanetaryApproach();
        _voyageNavigationAssist = true;
        ActivateCurrentPlanetSurfaceContent();
        ApplyStageOneVoyageToScene();
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        _lastDomainEvent = "InterplanetaryPlanetApproach";
        _status = LF(
            "ui.interplanetary.arrival",
            ("planet", GalaxyNavigation.CurrentPlanetId),
            ("transfers", GalaxyNavigation.InterplanetaryTransferCount));
        GD.Print(
            "TASK-152 interplanetary transfer PASS: " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"source={sourcePlanetId}; target={destinationPlanetId}; " +
            $"current={GalaxyNavigation.CurrentPlanetId}; " +
            $"plannedDistance={plannedDistance.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"arrivalDistance={distance.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"planetCenter=({targetCenter.X.ToString("0", CultureInfo.InvariantCulture)},{targetCenter.Y.ToString("0", CultureInfo.InvariantCulture)},{targetCenter.Z.ToString("0", CultureInfo.InvariantCulture)}); " +
            $"transfers={GalaxyNavigation.InterplanetaryTransferCount}; " +
            $"totalDistance={GalaxyNavigation.TotalInterplanetaryDistanceMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"checkpoint={StageOneVoyage.LastCheckpoint}; navAssist=1.");
        return true;
    }

    private void CancelInterplanetaryCruiseForManualControl()
    {
        if (_interplanetaryTravelRuntime?.IsCruising != true ||
            _galaxyNavigationRuntime is null)
        {
            return;
        }

        InterplanetaryTravel.Cancel(
            keepSelectedTarget: true,
            GalaxyNavigation);
        _status = L("ui.interplanetary.cruise_cancelled");
        GD.Print(
            "TASK-152 interplanetary cruise CANCEL: " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"current={GalaxyNavigation.CurrentPlanetId}; " +
            $"target={GalaxyNavigation.SelectedPlanetId}; fuelRefund=0.");
    }

    private string BuildInterplanetaryTravelHudLine()
    {
        if (_interplanetaryTravelRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            return L("ui.hud.interplanetary.unavailable");
        }

        string target = string.IsNullOrWhiteSpace(GalaxyNavigation.SelectedPlanetId)
            ? L("ui.common.none")
            : GalaxyNavigation.SelectedPlanetId;
        return LF(
            "ui.hud.interplanetary.summary",
            ("current", GalaxyNavigation.CurrentPlanetId),
            ("target", target),
            ("phase", InterplanetaryTravel.Phase),
            ("transfers", GalaxyNavigation.InterplanetaryTransferCount),
            ("distance", GalaxyNavigation.TotalInterplanetaryDistanceMeters.ToString(
                "0",
                CultureInfo.InvariantCulture)));
    }

    private void RunInterplanetaryTravelAcceptance()
    {
        InterplanetaryTravelAcceptanceReport report =
            InterplanetaryTravelAcceptanceRunner.Run(ShipSystemsCatalog);
        _interplanetaryTravelAcceptanceReport = report;
        _interplanetaryTravelAcceptanceHud = report.BuildHudLine();
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
