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

    private PlanetSurfaceCurvedPatchDescriptor? CurrentPlanetSurfaceCurvedPatch
    {
        get
        {
            if (_planetSurfaceContentProfile is null || _planetSurfaceFrame is null)
            {
                return null;
            }
            return new PlanetSurfaceCurvedPatchDescriptor(
                Math.Max(
                    PlanetSurfaceTopologyRuntime.MinimumRadiusMeters,
                    PlanetSurfaceContentProfile.Environment.RadiusKm * 1000.0),
                PlanetSurfaceFrame.OriginEastMeters,
                PlanetSurfaceFrame.OriginNorthMeters);
        }
    }

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
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is not null)
        {
            Vector3 logical = gameplay.ToLocal(_player.GlobalPosition);
            double logicalHeight = logical.Y +
                (CurrentPlanetSurfaceCurvedPatch?.TangentSagMeters(
                    logical.X,
                    logical.Z) ?? 0.0);
            return new PlanetSurfaceLogicalPosition(
                logical.X,
                logicalHeight,
                logical.Z);
        }
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
            Vector3 logical = gameplay.ToLocal(worldPosition);
            logical.Y += (float)(CurrentPlanetSurfaceCurvedPatch?.TangentSagMeters(
                logical.X,
                logical.Z) ?? 0.0);
            return logical;
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
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is not null)
        {
            double physicalHeight = heightMeters -
                (CurrentPlanetSurfaceCurvedPatch?.TangentSagMeters(
                    eastMeters,
                    northMeters) ?? 0.0);
            return gameplay.ToGlobal(new Vector3(
                (float)eastMeters,
                (float)physicalHeight,
                (float)northMeters));
        }
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
        PlanetSurfaceLogicalPosition currentLogical =
            GetPlanetSurfaceLogicalPlayerPosition();
        (double localEast, double localNorth) = PlanetSurfaceFrame.ToLocal(
            currentLogical.EastMeters,
            currentLogical.NorthMeters);
        PlanetSurfaceFrameRebase rebase = PlanetSurfaceFrame.PlanRebase(
            localEast,
            localNorth);
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
        ApplyPlanetSurfaceFrameTransforms();

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
            $"local={PlanetSurfaceFrame.ToLocal(logicalAfter.EastMeters, logicalAfter.NorthMeters).EastMeters:0.0}/" +
            $"{PlanetSurfaceFrame.ToLocal(logicalAfter.EastMeters, logicalAfter.NorthMeters).NorthMeters:0.0}m; " +
            $"logical={logicalAfter.EastMeters:0.0}/{logicalAfter.NorthMeters:0.0}m; " +
            $"continuityError={continuityError:0.000000}m; " +
            $"rebases={PlanetSurfaceFrame.RebaseCount}.");
    }

    private void ApplyPlanetSurfaceFrameTransforms()
    {
        double east = _planetSurfaceFrame?.OriginEastMeters ?? 0.0;
        double north = _planetSurfaceFrame?.OriginNorthMeters ?? 0.0;
        ApplyPlanetSurfacePhysicalTransforms(east, north);
    }

    private string BuildPlanetSurfaceFrameHudLine()
    {
        if (_player is null || _planetSurfaceFrame is null)
        {
            return "surface frame: unavailable";
        }

        PlanetSurfaceLogicalPosition logical =
            GetPlanetSurfaceLogicalPlayerPosition();
        (double localEast, double localNorth) = PlanetSurfaceFrame.ToLocal(
            logical.EastMeters,
            logical.NorthMeters);
        return
            "surface frame: " +
            $"logical={logical.EastMeters.ToString("0.0", CultureInfo.InvariantCulture)}/" +
            $"{logical.NorthMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"local={localEast.ToString("0.0", CultureInfo.InvariantCulture)}/" +
            $"{localNorth.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
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
