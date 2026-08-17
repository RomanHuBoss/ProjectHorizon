using System;
using Godot;

public partial class SalvageRepairSlice
{
    private WorldStreamingCoordinatorNode? _worldStreamingCoordinator;
    private string _worldStreamingAcceptanceHud = "READY";
    private bool? _worldStreamingAcceptancePassed;
    private bool _worldStreamingReadyPrinted;

    private void InitializeWorldStreamingRuntime()
    {
        if (_worldStreamingCoordinator is not null &&
            GodotObject.IsInstanceValid(_worldStreamingCoordinator))
        {
            return;
        }
        _worldStreamingCoordinator = new WorldStreamingCoordinatorNode
        {
            Name = "WorldStreamingCoordinator"
        };
        AddChild(_worldStreamingCoordinator);
        PrintWorldStreamingReady();
    }

    private void PrintWorldStreamingReady()
    {
        if (_worldStreamingReadyPrinted)
        {
            return;
        }
        _worldStreamingReadyPrinted = true;
        GD.Print(
            "TASK-194 world streaming READY: " +
            "activeZone=onFoot:2km/ground:5km/atmospheric:15km; " +
            "macroRegion=1km; simplified=outer-ring; " +
            "priorities=player>movement>collision>visible>far>preload; " +
            "workers=max(1,min(4,cpu-2)); background=data-only; " +
            "mainThreadBudget=2ms/5ms-forced/10ms-loading; cancellation=revision-token; " +
            "microTerrain=25-chunk-collision-safe; F5=acceptance.");
    }

    private void UpdateWorldStreamingRuntime(double delta)
    {
        if (_worldStreamingCoordinator is null ||
            !GodotObject.IsInstanceValid(_worldStreamingCoordinator))
        {
            return;
        }
        if (!_surfaceRuntimeActive)
        {
            _worldStreamingCoordinator.Suspend();
            return;
        }

        Node3D? observerNode = StageOneVoyage.Piloted && _voyageShip is not null
            ? _voyageShip
            : _player;
        if (observerNode is null || !GodotObject.IsInstanceValid(observerNode))
        {
            return;
        }

        Vector3 logical;
        if (IsPlayerInsidePlanetaryCave && _planetaryCaveReturnLogical is { } outside)
        {
            logical = new Vector3(
                (float)outside.EastMeters,
                (float)outside.HeightMeters,
                (float)outside.NorthMeters);
        }
        else
        {
            logical = WorldToPlanetSurfaceLogicalPosition(observerNode.GlobalPosition);
        }

        Vector3 velocity = observerNode is CharacterBody3D body
            ? body.Velocity
            : Vector3.Zero;
        WorldStreamingTravelMode mode = StageOneVoyage.Piloted
            ? WorldStreamingTravelMode.AtmosphericFlight
            : WorldStreamingTravelMode.OnFoot;
        _worldStreamingCoordinator.Tick(
            delta,
            new WorldStreamingObserverSample(
                logical.X,
                logical.Z,
                velocity.X,
                velocity.Z,
                mode));
    }

    private void RunWorldStreamingAcceptance()
    {
        if (_worldStreamingCoordinator is null ||
            !GodotObject.IsInstanceValid(_worldStreamingCoordinator) ||
            _planetSurfaceStreamer is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceStreamer))
        {
            _worldStreamingAcceptancePassed = false;
            _worldStreamingAcceptanceHud = "FAIL runtime unavailable";
            GD.PushError(
                "TASK-194 world streaming acceptance FAIL: live coordinator or terrain streamer unavailable.");
            return;
        }
        WorldStreamingAcceptanceReport report =
            WorldStreamingAcceptanceRunner.Evaluate(
                _worldStreamingCoordinator.CreateDiagnostics(),
                _planetSurfaceStreamer.CaptureProfilerSnapshot());
        _worldStreamingAcceptancePassed = report.Passed;
        _worldStreamingAcceptanceHud = report.Passed
            ? $"PASS full={report.LiveFullRegions} simp={report.LiveSimplifiedRegions} pre={report.LivePreloadRegions} pri=6 budget=2ms"
            : "FAIL world streaming contract";
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
