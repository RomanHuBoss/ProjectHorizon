using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private const double PilotedShipMinimumTerrainClearanceMeters = 3.2;
    private const double PilotedShipRecoveryPaddingMeters = 0.18;
    private int _pilotedShipSurfaceRecoveryCount;
    private int _pilotedShipSurfaceSweepBlockCount;
    private int _pilotedShipSurfaceSweepSamples;
    private bool _pilotedShipSurfaceSafetyReadyPrinted;
    private bool _pilotedShipSurfaceSweepInitialized;
    private bool _pilotedShipSurfaceContactActive;
    private int _pilotedShipSurfaceContactRecoveries;
    private Vector3 _pilotedShipSurfaceSweepPreviousPosition;
    private double _pilotedShipMinimumObservedTerrainClearance = double.PositiveInfinity;

    private void UpdatePilotedShipSurfaceSafety()
    {
        if (!_surfaceRuntimeActive ||
            _voyageShip is null ||
            CurrentTerrainProfile is null ||
            !StageOneVoyage.Piloted ||
            StageOneVoyage.Location is not (
                StageOneVoyageLocation.OutboundFlight or
                StageOneVoyageLocation.InboundFlight))
        {
            _pilotedShipSurfaceSweepInitialized = false;
            return;
        }

        UpdatePlanetSurfaceStreamingObserver();

        Vector3 current = _voyageShip.GlobalPosition;
        if (!_pilotedShipSurfaceSweepInitialized ||
            !_pilotedShipSurfaceSweepPreviousPosition.IsFinite() ||
            _pilotedShipSurfaceSweepPreviousPosition.DistanceTo(current) > 120.0f)
        {
            _pilotedShipSurfaceSweepInitialized = true;
            _pilotedShipSurfaceSweepPreviousPosition = current;
        }

        Vector3 previous = _pilotedShipSurfaceSweepPreviousPosition;
        float travel = previous.DistanceTo(current);
        int samples = Math.Clamp(
            (int)Math.Ceiling(Math.Max(0.1f, travel) / 1.25f),
            1,
            96);

        Vector3 hitLogical = Vector3.Zero;
        double hitTerrainHeight = 0.0;
        double hitClearance = double.PositiveInfinity;
        bool penetration = false;
        for (int index = 1; index <= samples; index++)
        {
            float t = index / (float)samples;
            Vector3 samplePosition = previous.Lerp(current, t);
            Vector3 logical = WorldToPlanetSurfaceLogicalPosition(samplePosition);
            double terrainHeight = SamplePlanetSurfaceHeight(logical.X, logical.Z);
            double clearance = logical.Y - terrainHeight;
            _pilotedShipSurfaceSweepSamples++;
            if (!double.IsFinite(clearance))
            {
                continue;
            }

            _pilotedShipMinimumObservedTerrainClearance = Math.Min(
                _pilotedShipMinimumObservedTerrainClearance,
                clearance);
            if (clearance < PilotedShipMinimumTerrainClearanceMeters)
            {
                hitLogical = logical;
                hitTerrainHeight = terrainHeight;
                hitClearance = clearance;
                penetration = true;
                break;
            }
        }

        if (!_pilotedShipSurfaceSafetyReadyPrinted)
        {
            _pilotedShipSurfaceSafetyReadyPrinted = true;
            GD.Print(
                "TASK-178.7 piloted surface solidity READY: " +
                $"clearance={PilotedShipMinimumTerrainClearanceMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
                $"activationAltitude={PlanetRuntimeActivationAltitudeMeters.ToString("0", CultureInfo.InvariantCulture)}m; " +
                "streamerObserver=ship-while-piloted; hardFloor=terrain-aware; sweep=1.25m/96.");
        }

        if (!penetration)
        {
            if (_pilotedShipSurfaceContactActive)
            {
                GD.Print(
                    "TASK-180.1 surface contact RECOVERED: " +
                    $"recoveries={_pilotedShipSurfaceContactRecoveries}; " +
                    $"total={_pilotedShipSurfaceRecoveryCount}.");
                _pilotedShipSurfaceContactActive = false;
                _pilotedShipSurfaceContactRecoveries = 0;
            }
            _pilotedShipSurfaceSweepPreviousPosition = current;
            return;
        }

        Vector3 normal = BuildPilotedShipTerrainNormal(
            hitLogical.X,
            hitLogical.Z,
            hitTerrainHeight);
        Vector3 corrected = SurfaceLogicalToLocalPosition(
            hitLogical.X,
            hitTerrainHeight + PilotedShipMinimumTerrainClearanceMeters +
                PilotedShipRecoveryPaddingMeters,
            hitLogical.Z);
        _voyageShip.GlobalPosition = corrected;

        float inward = _voyageShip.Velocity.Dot(normal);
        if (inward < 0.0f)
        {
            _voyageShip.Velocity -= normal * inward;
        }

        _pilotedShipSurfaceRecoveryCount++;
        _pilotedShipSurfaceContactRecoveries++;
        _pilotedShipSurfaceSweepBlockCount++;
        _pilotedShipSurfaceSweepPreviousPosition = corrected;
        if (!_pilotedShipSurfaceContactActive)
        {
            _pilotedShipSurfaceContactActive = true;
            GD.PushWarning(
                "TASK-178.7 surface penetration BLOCKED (TASK-180.1 debounced): " +
                $"clearance={hitClearance.ToString("0.00", CultureInfo.InvariantCulture)}m; " +
                $"terrain={hitTerrainHeight.ToString("0.00", CultureInfo.InvariantCulture)}m; " +
                $"samples={samples}; swept=1; padding={PilotedShipRecoveryPaddingMeters.ToString("0.00", CultureInfo.InvariantCulture)}m; blocked=1.");
        }
    }

    private Vector3 BuildPilotedShipTerrainNormal(
        double eastMeters,
        double northMeters,
        double centerHeight)
    {
        const double sampleStep = 0.75;
        double eastHeight = SamplePlanetSurfaceHeight(
            eastMeters + sampleStep,
            northMeters);
        double northHeight = SamplePlanetSurfaceHeight(
            eastMeters,
            northMeters + sampleStep);

        Vector3 center = SurfaceLogicalToLocalPosition(
            eastMeters,
            centerHeight,
            northMeters);
        Vector3 east = SurfaceLogicalToLocalPosition(
            eastMeters + sampleStep,
            eastHeight,
            northMeters);
        Vector3 north = SurfaceLogicalToLocalPosition(
            eastMeters,
            northHeight,
            northMeters + sampleStep);
        Vector3 normal = (north - center).Cross(east - center);
        Vector3 coarseUp = SurfaceLocalDirectionToWorld(Vector3.Up).Normalized();
        if (normal.LengthSquared() <= 0.000001f)
        {
            return coarseUp;
        }

        normal = normal.Normalized();
        return normal.Dot(coarseUp) >= 0.0f ? normal : -normal;
    }
}
