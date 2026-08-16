using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private int _manualCrossPlanetEntryCount;
    private string _lastManualCrossPlanetTarget = string.Empty;

    private bool TryCommitManualCrossPlanetEntry(
        GalaxyPlanetDefinition targetPlanet,
        OrbitalBodyCollisionHit shellHit,
        double entrySpeed)
    {
        if (_voyageShip is null || _worldSceneCoordinatorNode is null ||
            _worldSceneCoordinatorRuntime is null ||
            _galaxyNavigationRuntime is null ||
            _interplanetaryTravelRuntime is null ||
            WorldScenes.Current.Kind != WorldSceneKind.Orbit ||
            StageOneVoyage.Location is not (
                StageOneVoyageLocation.OutboundFlight or
                StageOneVoyageLocation.InboundFlight))
        {
            return false;
        }

        string sourcePlanetId = GalaxyNavigation.CurrentPlanetId;
        if (string.Equals(
            sourcePlanetId,
            targetPlanet.PlanetId,
            StringComparison.Ordinal))
        {
            return false;
        }

        PlanetEnvironmentProfile targetEnvironment = PlanetEnvironment.BuildProfile(
            targetPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        if (!targetEnvironment.Landable)
        {
            return false;
        }

        double transferDistance = shellHit.Center.Length();
        if (_starSystemSimulationNode is not null &&
            _starSystemSimulationNode.TryGetBodyDisplaySphere(
                sourcePlanetId,
                out _,
                out Vector3 sourceCenter,
                out _))
        {
            transferDistance = sourceCenter.DistanceTo(shellHit.Center);
        }
        transferDistance = Math.Max(1.0, transferDistance);

        WorldSceneCoordinatorNodeSnapshot sceneSnapshot =
            _worldSceneCoordinatorNode.CaptureSnapshot();
        WorldSceneContext transit = WorldSceneContext.Create(
            WorldSceneKind.InterplanetaryTransit,
            GalaxyNavigation.CurrentSystem.SystemId,
            sourcePlanetId);
        WorldSceneTransitionResult begin =
            _worldSceneCoordinatorNode.TryTransition(transit, out string beginResult);
        if (begin != WorldSceneTransitionResult.Applied)
        {
            GD.PushError(
                "TASK-178.6 manual planet transit FAIL: " +
                $"phase=begin; source={sourcePlanetId}; target={targetPlanet.PlanetId}; " +
                $"result={beginResult}");
            return false;
        }

        _worldResidencyTransitions++;
        ApplyWorldResidencyPolicy(force: true);
        CaptureCurrentPlanetSurfaceState();

        bool selected = GalaxyNavigation.TrySelectPlanetDestination(
            targetPlanet.PlanetId,
            out string selectionResult);
        string transferResult = selectionResult;
        bool transferred = selected && GalaxyNavigation.TryCompletePlanetTransfer(
            targetPlanet.PlanetId,
            transferDistance,
            out transferResult);
        if (!transferred)
        {
            GalaxyNavigation.ClearPlanetDestination();
            InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);
            _worldSceneCoordinatorNode.RestoreSnapshot(sceneSnapshot);
            ApplyWorldResidencyPolicy(force: true);
            GD.PushError(
                "TASK-178.6 manual planet transit FAIL: " +
                $"phase=commit; source={sourcePlanetId}; target={targetPlanet.PlanetId}; " +
                $"selection={selectionResult}; result={(selected ? transferResult : selectionResult)}");
            return false;
        }

        InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);
        StageOneVoyage.ArriveAtPlanetaryApproach();
        _voyageNavigationAssist = false;
        _voyageShip.ClearExternalCommand();

        // This activation is deliberately synchronous with the planet-identity
        // transaction. Terrain/ecology/POI/resource plans therefore belong to
        // the destination before the player can enter its 220 m surface approach.
        ActivateCurrentPlanetSurfaceContent();
        ApplyStageOneVoyageToScene();
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);

        _manualCrossPlanetEntryCount++;
        _lastManualCrossPlanetTarget = targetPlanet.PlanetId;
        int flora = _ecologyPlan?.Flora.Count ?? 0;
        int faunaActive = _ecologyPlan?.ActiveFauna.Count ?? 0;
        int faunaSimplified = _ecologyPlan?.SimplifiedFauna.Count ?? 0;
        int pois = _planetaryPoiPlacements.Count;
        int resources = _streamedSurfaceResources.Count;
        GD.Print(
            "TASK-178.6 manual planet transfer PASS: " +
            $"source={sourcePlanetId}; target={targetPlanet.PlanetId}; " +
            $"distance={transferDistance.ToString("0", CultureInfo.InvariantCulture)}m; " +
            $"entrySpeed={entrySpeed.ToString("0.0", CultureInfo.InvariantCulture)}m/s; " +
            "world=Orbit->InterplanetaryTransit->Orbit; " +
            $"flora={flora}; fauna={faunaActive}/{faunaSimplified}; " +
            $"pois={pois}; resources={resources}; surfaceHandoff=1.");
        return true;
    }

    private GalaxyPlanetDefinition? ResolveLandablePlanet(string planetId)
    {
        if (_galaxyNavigationRuntime is null || string.IsNullOrWhiteSpace(planetId))
        {
            return null;
        }

        GalaxyPlanetDefinition? planet = GalaxyNavigation.CurrentSystem.Planets
            .FirstOrDefault(candidate => string.Equals(
                candidate.PlanetId,
                planetId,
                StringComparison.Ordinal));
        if (planet is null)
        {
            return null;
        }

        PlanetEnvironmentProfile environment = PlanetEnvironment.BuildProfile(
            planet,
            GalaxyNavigation.CurrentSystem.StarType);
        return environment.Landable ? planet : null;
    }
}
