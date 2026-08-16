using System;
using Godot;

public partial class SalvageRepairSlice
{
    private string _surfaceFlightSafetyAcceptanceHud = "READY";
    private bool? _surfaceFlightSafetyAcceptancePassed;

    private void PrintSurfaceFlightSafetyReady()
    {
        GD.Print(
            "TASK-178.7 surface/brake/handoff READY: " +
            $"surfaceHardFloor={PilotedShipMinimumTerrainClearanceMeters:0.0}m; " +
            $"surfaceActivation={PlanetRuntimeActivationAltitudeMeters:0}m; " +
            $"atmosphereDynamics={OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters:0}..{OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters:0}m; " +
            $"surfaceHandoff={PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters:0}m; " +
            "brake=monotonic-zero-clamp; reverseOnBrake=0; terrainSweep=1; F5=acceptance.");
    }

    private void RunSurfaceFlightSafetyAcceptance()
    {
        SurfaceFlightSafetyAcceptanceReport report =
            SurfaceFlightSafetyAcceptanceRunner.Run();
        float liveAtmosphereStart = _voyageShip?.AtmosphereFadeStart ?? 0.0f;
        float liveAtmosphereHeight = _voyageShip?.AtmosphereHeight ?? 0.0f;
        bool liveAtmosphere = _voyageShip is not null &&
            Math.Abs(
                liveAtmosphereStart -
                OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters) <= 0.5 &&
            Math.Abs(
                liveAtmosphereHeight -
                OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters) <= 0.5;
        bool liveStreamer = _planetSurfaceStreamer is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceStreamer);
        bool hardFloor = PilotedShipMinimumTerrainClearanceMeters >= 3.0;
        bool passed = report.Passed && liveAtmosphere && liveStreamer && hardFloor;
        _surfaceFlightSafetyAcceptancePassed = passed;
        _surfaceFlightSafetyAcceptanceHud = passed
            ? $"PASS brake=1 atmosphere={liveAtmosphereStart:0}..{liveAtmosphereHeight:0}m floor={PilotedShipMinimumTerrainClearanceMeters:0.0}m"
            : $"FAIL model={(report.Passed ? 1 : 0)} atmosphere={(liveAtmosphere ? 1 : 0)} streamer={(liveStreamer ? 1 : 0)} floor={(hardFloor ? 1 : 0)}";

        string output = report.BuildOutputLine().Replace(
            $"acceptance {(report.Passed ? "PASS" : "FAIL")}:",
            $"acceptance {(passed ? "PASS" : "FAIL")}:") +
            $" liveAtmosphere={(liveAtmosphere ? 1 : 0)}; liveStreamer={(liveStreamer ? 1 : 0)}; " +
            $"hardFloor={(hardFloor ? 1 : 0)}; recoveries={_pilotedShipSurfaceRecoveryCount}; " +
            $"sweepBlocks={_pilotedShipSurfaceSweepBlockCount}; sweepSamples={_pilotedShipSurfaceSweepSamples}; " +
            $"minObservedClearance={(_pilotedShipMinimumObservedTerrainClearance < double.PositiveInfinity ? _pilotedShipMinimumObservedTerrainClearance : 0.0):0.00}m.";
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
