using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetSurfaceFrameRuntime? _planetSurfaceFrame;
    private string _planetSurfaceFrameAcceptanceHud = "READY";
    private bool _planetSurfaceFrameReadyPrinted;

    private PlanetSurfaceFrameRuntime PlanetSurfaceFrame =>
        _planetSurfaceFrame ??= new PlanetSurfaceFrameRuntime();

    private void EnsurePlanetSurfaceFrameForCurrentPlanet()
    {
        string planetId = GalaxyNavigation.CurrentPlanetId;
        if (_planetSurfaceFrame is null)
        {
            _planetSurfaceFrame = new PlanetSurfaceFrameRuntime();
            _planetSurfaceFrame.Reset(planetId);
            ApplyPlanetSurfaceFrameTransforms();
            return;
        }

        if (!string.Equals(
                _planetSurfaceFrame.PlanetId,
                planetId,
                StringComparison.Ordinal))
        {
            _planetSurfaceFrame.Reset(planetId);
            _planetSurfaceFrameReadyPrinted = false;
            ApplyPlanetSurfaceFrameTransforms();
        }
    }

    private PlanetSurfaceLogicalPosition GetPlanetSurfaceLogicalPlayerPosition()
    {
        if (_player is null)
        {
            return new PlanetSurfaceLogicalPosition(0.0, 0.0, 0.0);
        }

        EnsurePlanetSurfaceFrameForCurrentPlanet();
        return PlanetSurfaceFrame.ToLogical(
            _player.GlobalPosition.X,
            _player.GlobalPosition.Y,
            _player.GlobalPosition.Z);
    }

    private Vector3 WorldToPlanetSurfaceLogicalPosition(Vector3 worldPosition)
    {
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is not null)
        {
            return gameplay.ToLocal(worldPosition);
        }

        EnsurePlanetSurfaceFrameForCurrentPlanet();
        return new Vector3(
            worldPosition.X + (float)PlanetSurfaceFrame.OriginEastMeters,
            worldPosition.Y,
            worldPosition.Z + (float)PlanetSurfaceFrame.OriginNorthMeters);
    }

    private Vector3 SurfaceLogicalToLocalPosition(
        double eastMeters,
        double heightMeters,
        double northMeters)
    {
        EnsurePlanetSurfaceFrameForCurrentPlanet();
        (double east, double north) = PlanetSurfaceFrame.ToLocal(
            eastMeters,
            northMeters);
        return new Vector3((float)east, (float)heightMeters, (float)north);
    }

    private void RestorePlanetSurfaceFrameAtLogicalPosition(
        double eastMeters,
        double northMeters)
    {
        _planetSurfaceFrame ??= new PlanetSurfaceFrameRuntime();
        _planetSurfaceFrame.RestoreAtLogicalPosition(
            GalaxyNavigation.CurrentPlanetId,
            eastMeters,
            northMeters);
        _planetSurfaceFrameReadyPrinted = false;
        ApplyPlanetSurfaceFrameTransforms();
    }

    private void ResetPlanetSurfaceFrameForCurrentPlanet()
    {
        _planetSurfaceFrame ??= new PlanetSurfaceFrameRuntime();
        _planetSurfaceFrame.Reset(GalaxyNavigation.CurrentPlanetId);
        _planetSurfaceFrameReadyPrinted = false;
        ApplyPlanetSurfaceFrameTransforms();
    }

    private void UpdatePlanetSurfaceFrame()
    {
        if (!_surfaceRuntimeActive || _player is null || StageOneVoyage.Piloted)
        {
            return;
        }

        EnsurePlanetSurfaceFrameForCurrentPlanet();
        PlanetSurfaceFrameRebase rebase = PlanetSurfaceFrame.PlanRebase(
            _player.GlobalPosition.X,
            _player.GlobalPosition.Z);
        if (!rebase.Required)
        {
            if (!_planetSurfaceFrameReadyPrinted)
            {
                PlanetSurfaceLogicalPosition logical =
                    GetPlanetSurfaceLogicalPlayerPosition();
                GD.Print(
                    "TASK-162 planet-global surface frame READY: " +
                    $"planet={PlanetSurfaceFrame.PlanetId}; " +
                    $"cell={PlanetSurfaceFrameRuntime.RebaseCellSizeMeters:0}m; " +
                    $"threshold={PlanetSurfaceFrameRuntime.RebaseThresholdMeters:0}m; " +
                    $"origin={PlanetSurfaceFrame.OriginEastMeters:0}/{PlanetSurfaceFrame.OriginNorthMeters:0}; " +
                    $"logical={logical.EastMeters:0.0}/{logical.NorthMeters:0.0}; " +
                    "streamer=logical-chunk; surfaceRoots=rebased; persistence=logical-xz.");
                _planetSurfaceFrameReadyPrinted = true;
            }
            return;
        }

        PlanetSurfaceLogicalPosition logicalBefore =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceFrame.Apply(rebase);
        Vector3 worldShift = new(
            (float)rebase.ShiftEastMeters,
            0.0f,
            (float)rebase.ShiftNorthMeters);
        _player.GlobalPosition -= worldShift;
        ApplyPlanetSurfaceFrameTransforms();
        ApplyPlanetSurfaceOriginShiftToRuntimeCaches(worldShift);

        PlanetSurfaceLogicalPosition logicalAfter =
            GetPlanetSurfaceLogicalPlayerPosition();
        double continuityError = Math.Max(
            Math.Abs(logicalAfter.EastMeters - logicalBefore.EastMeters),
            Math.Abs(logicalAfter.NorthMeters - logicalBefore.NorthMeters));
        GD.Print(
            "TASK-162 planet surface REBASE: " +
            $"planet={PlanetSurfaceFrame.PlanetId}; " +
            $"shift={rebase.ShiftEastMeters:0}/{rebase.ShiftNorthMeters:0}m; " +
            $"origin={PlanetSurfaceFrame.OriginEastMeters:0}/{PlanetSurfaceFrame.OriginNorthMeters:0}m; " +
            $"local={_player.GlobalPosition.X:0.0}/{_player.GlobalPosition.Z:0.0}m; " +
            $"logical={logicalAfter.EastMeters:0.0}/{logicalAfter.NorthMeters:0.0}m; " +
            $"continuityError={continuityError:0.000000}m; " +
            $"rebases={PlanetSurfaceFrame.RebaseCount}.");
    }

    private void ApplyPlanetSurfaceOriginShiftToRuntimeCaches(Vector3 worldShift)
    {
        foreach (NpcFactionAgentNode agent in
                 GetTree().GetNodesInGroup("npc_faction_agent"))
        {
            agent.ApplyWorldOriginShift(worldShift);
        }
        foreach (NpcShipNavigationNode ship in _npcShipNavigationNodes)
        {
            if (GodotObject.IsInstanceValid(ship))
            {
                ship.ApplyWorldOriginShift(worldShift);
            }
        }
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes)
        {
            if (GodotObject.IsInstanceValid(fauna))
            {
                fauna.ApplyWorldOriginShift();
            }
        }
        if (_aerialSteeringRuntime is not null)
        {
            RefreshAerialNavigationEnvironment();
        }
    }

    private void ApplyPlanetSurfaceFrameTransforms()
    {
        double east = _planetSurfaceFrame?.OriginEastMeters ?? 0.0;
        double north = _planetSurfaceFrame?.OriginNorthMeters ?? 0.0;

        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is not null)
        {
            gameplay.Position = new Vector3((float)-east, 0.0f, (float)-north);
        }

        Node3D? ground = GetNodeOrNull<Node3D>("GroundBody");
        if (ground is not null)
        {
            // GroundBody is a short-lived fallback patch generated around the
            // current logical frame origin; keep its vertices in local space.
            ground.Position = Vector3.Zero;
        }

        _planetSurfaceStreamer?.SetLogicalSurfaceOrigin(east, north);
    }

    private string BuildPlanetSurfaceFrameHudLine()
    {
        if (_player is null || _planetSurfaceFrame is null)
        {
            return "surface frame: unavailable";
        }

        PlanetSurfaceLogicalPosition logical =
            GetPlanetSurfaceLogicalPlayerPosition();
        return
            "surface frame: " +
            $"logical={logical.EastMeters.ToString("0.0", CultureInfo.InvariantCulture)}/" +
            $"{logical.NorthMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"local={_player.GlobalPosition.X.ToString("0.0", CultureInfo.InvariantCulture)}/" +
            $"{_player.GlobalPosition.Z.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"origin={PlanetSurfaceFrame.OriginEastMeters.ToString("0", CultureInfo.InvariantCulture)}/" +
            $"{PlanetSurfaceFrame.OriginNorthMeters.ToString("0", CultureInfo.InvariantCulture)}m; " +
            $"rebases={PlanetSurfaceFrame.RebaseCount}";
    }

    private void RunPlanetSurfaceFrameAcceptance()
    {
        PlanetSurfaceFrameAcceptanceReport report =
            PlanetSurfaceFrameAcceptanceRunner.Run();
        _planetSurfaceFrameAcceptanceHud = report.BuildHudLine();
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
