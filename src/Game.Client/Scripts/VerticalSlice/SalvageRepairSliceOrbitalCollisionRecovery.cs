using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private bool _orbitalCollisionPreviousValid;
    private Vector3 _orbitalCollisionPreviousShipPosition;
    private StageOneVoyageLocation _orbitalCollisionPreviousLocation;
    private WorldSceneKind _orbitalCollisionPreviousWorldKind;
    private int _orbitalSweptCollisionCount;
    private int _freeFlightPlanetEntryCount;
    private string _orbitalCollisionLastBody = string.Empty;
    private bool _spaceflightCollisionRecoveryReadyPrinted;

    private void UpdateOrbitalCollisionRecovery()
    {
        TryPrintSpaceflightCollisionRecoveryReady();
        if (_voyageShip is null || _starSystemSimulationNode is null ||
            _stageOneVoyageRuntime is null || _worldSceneCoordinatorRuntime is null ||
            !StageOneVoyage.Piloted ||
            StageOneVoyage.Location is not (
                StageOneVoyageLocation.OutboundFlight or
                StageOneVoyageLocation.InboundFlight) ||
            WorldScenes.Current.Kind != WorldSceneKind.Orbit ||
            _interplanetaryTravelRuntime?.IsCruising == true)
        {
            ResetOrbitalCollisionSweep();
            return;
        }

        Vector3 current = _voyageShip.GlobalPosition;
        if (!_orbitalCollisionPreviousValid ||
            _orbitalCollisionPreviousLocation != StageOneVoyage.Location ||
            _orbitalCollisionPreviousWorldKind != WorldScenes.Current.Kind)
        {
            ArmOrbitalCollisionSweep(current);
            return;
        }

        Vector3 previous = _orbitalCollisionPreviousShipPosition;
        if (!previous.IsFinite() || !current.IsFinite() ||
            previous.DistanceTo(current) > 350.0f)
        {
            // World handoffs and persistence restores can legitimately teleport
            // the ship. Never interpret those authoritative state changes as a
            // physical sweep through every body between the two coordinates.
            ArmOrbitalCollisionSweep(current);
            return;
        }

        if (TryCaptureFreeFlightPlanetEntry(previous, current))
        {
            ArmOrbitalCollisionSweep(_voyageShip.GlobalPosition);
            return;
        }

        if (_starSystemSimulationNode.TryGetFirstSolidBodyHit(
                previous,
                current,
                OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters,
                out OrbitalBodyCollisionHit hit))
        {
            HandleOrbitalBodyImpact(hit);
            ArmOrbitalCollisionSweep(_voyageShip.GlobalPosition);
            return;
        }

        ArmOrbitalCollisionSweep(current);
    }

    private bool TryCaptureFreeFlightPlanetEntry(Vector3 previous, Vector3 current)
    {
        if (_voyageShip is null || _starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null ||
            StageOneVoyage.Location is not (
                StageOneVoyageLocation.OutboundFlight or
                StageOneVoyageLocation.InboundFlight) ||
            StageOneVoyage.IsPlanetarySurfaceApproach ||
            !_starSystemSimulationNode.TryGetFirstPlanetEntryShellHit(
                previous,
                current,
                OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters,
                PlanetaryApproachRuntime.OrbitalEntryClearanceMeters,
                out OrbitalBodyCollisionHit shellHit))
        {
            return false;
        }

        if (_voyageShip.Speed > PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed)
        {
            // Safe-entry shell is deliberately ignored at excessive speed. The
            // inner swept solid-body collision below will then stop/death the
            // ship instead of silently teleporting through the planet.
            return false;
        }

        GalaxyPlanetDefinition? targetPlanet = ResolveLandablePlanet(shellHit.BodyId);
        if (targetPlanet is null)
        {
            return false;
        }

        double speed = _voyageShip.Speed;
        if (!string.Equals(
                targetPlanet.PlanetId,
                GalaxyNavigation.CurrentPlanetId,
                StringComparison.Ordinal))
        {
            return TryCommitManualCrossPlanetEntry(
                targetPlanet,
                shellHit,
                speed);
        }

        _voyageShip.GlobalPosition = shellHit.ShipCenterAtImpact;
        if (!TryCommitPlanetaryEntryHandoff(automatic: false))
        {
            return false;
        }

        _freeFlightPlanetEntryCount++;
        GD.Print(
            "TASK-178.5 free-flight planetary entry PASS: " +
            $"planet={targetPlanet.PlanetId}; mode=manual-flight; swept=1; " +
            $"speed={speed.ToString("0.0", CultureInfo.InvariantCulture)}m/s; " +
            $"displayRadius={shellHit.DisplayRadius.ToString("0", CultureInfo.InvariantCulture)}m; " +
            $"entryRadius={shellHit.CollisionRadius.ToString("0", CultureInfo.InvariantCulture)}m; " +
            "surfaceHandoff=1.");
        return true;
    }

    private void HandleOrbitalBodyImpact(OrbitalBodyCollisionHit hit)
    {
        if (_voyageShip is null)
        {
            return;
        }

        double impactSpeed = _voyageShip.Speed;
        _voyageShip.GlobalPosition = hit.ShipCenterAtImpact;
        _voyageShip.Velocity = Vector3.Zero;
        _voyageShip.ClearExternalCommand();
        _orbitalSweptCollisionCount++;
        _orbitalCollisionLastBody = hit.BodyId;

        string reasonKey = hit.Kind == StarSystemBodyKind.Star
            ? "ui.death.star_impact"
            : "ui.death.planet_impact";
        GD.Print(
            "TASK-178.5 orbital body collision PASS: " +
            $"body={hit.BodyId}; kind={hit.Kind}; swept=1; blocked=1; " +
            $"speed={impactSpeed.ToString("0.0", CultureInfo.InvariantCulture)}m/s; " +
            $"displayRadius={hit.DisplayRadius.ToString("0", CultureInfo.InvariantCulture)}m; " +
            $"fraction={hit.SegmentFraction.ToString("0.000", CultureInfo.InvariantCulture)}; death=1.");
        ShowApplicationDeathScreen(reasonKey);
    }

    private void TryPrintSpaceflightCollisionRecoveryReady()
    {
        if (_spaceflightCollisionRecoveryReadyPrinted || _voyageShip is null ||
            _starSystemSimulationNode is null || _galaxyNavigationRuntime is null ||
            !_starSystemSimulationNode.TryGetBodyDisplaySphere(
                GalaxyNavigation.CurrentPlanetId,
                out StarSystemBodyDefinition definition,
                out _,
                out float radius))
        {
            return;
        }

        _spaceflightCollisionRecoveryReadyPrinted = true;
        GD.Print(
            "TASK-178.5 spaceflight kinematics/collision READY: " +
            $"flightAssist=heading-coupled; alignment={_voyageShip.VelocityAlignmentRate:0.0}/s; " +
            $"planet={definition.BodyId}; radius={radius:0}m; " +
            $"shipSweepRadius={OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters:0.0}m; " +
            "collision=continuous-swept-sphere; manualEntry=physical-shell; F5=acceptance.");
    }

    private void ResetOrbitalCollisionSweep()
    {
        _orbitalCollisionPreviousValid = false;
    }

    private void ArmOrbitalCollisionSweep(Vector3 position)
    {
        _orbitalCollisionPreviousValid = true;
        _orbitalCollisionPreviousShipPosition = position;
        _orbitalCollisionPreviousLocation = StageOneVoyage.Location;
        _orbitalCollisionPreviousWorldKind = WorldScenes.Current.Kind;
    }
}
