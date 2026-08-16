using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetSurfaceRadialFrameRuntime? _planetRadialSurfaceRuntime;
    private PlanetSurfaceRadialFrameState? _planetRadialSurfaceState;
    private string _planetRadialSurfaceAcceptanceHud = "READY";
    private bool _planetRadialSurfaceReadyPrinted;

    private void InitializePlanetRadialSurfaceRuntime()
    {
        if (_planetSurfaceContentProfile is null || _player is null)
        {
            return;
        }

        _planetRadialSurfaceRuntime = new PlanetSurfaceRadialFrameRuntime(
            PlanetSurfaceContentProfile.Environment);
        _planetRadialSurfaceState = null;
        _planetRadialSurfaceReadyPrinted = false;
        RefreshPlanetRadialSurfaceState(forceAnnouncement: true);
    }

    private void UpdatePlanetRadialSurfaceRuntime()
    {
        if (_player is null)
        {
            return;
        }
        if (!_surfaceRuntimeActive || StageOneVoyage.Piloted)
        {
            _player.RestoreDefaultGravity();
            return;
        }
        if (_planetSurfaceContentProfile is null)
        {
            return;
        }
        if (_planetRadialSurfaceRuntime is null ||
            !string.Equals(
                _planetRadialSurfaceRuntime.Environment.PlanetId,
                PlanetSurfaceContentProfile.PlanetId,
                StringComparison.Ordinal))
        {
            InitializePlanetRadialSurfaceRuntime();
            return;
        }

        RefreshPlanetRadialSurfaceState(forceAnnouncement: false);
    }

    private void RefreshPlanetRadialSurfaceState(bool forceAnnouncement)
    {
        if (_planetRadialSurfaceRuntime is null || _player is null)
        {
            return;
        }

        EnsurePlanetSurfaceFrameForCurrentPlanet();
        PlanetSurfaceLogicalPosition logical = PlanetSurfaceFrame.ToLogical(
            _player.GlobalPosition.X,
            _player.GlobalPosition.Y,
            _player.GlobalPosition.Z);
        PlanetSurfaceRadialFrameState next = _planetRadialSurfaceRuntime.Build(
            logical.EastMeters,
            logical.NorthMeters);
        PlanetSurfaceRadialFrameState? previous = _planetRadialSurfaceState;
        _planetRadialSurfaceState = next;
        _player.SetPlanetSurfaceGravity(
            _planetRadialSurfaceRuntime.Environment.SurfaceGravityG);

        if (previous is { } prior &&
            prior.CubeFace.Face != next.CubeFace.Face)
        {
            double upDelta = _planetRadialSurfaceRuntime.MeasureUpDeltaDegrees(
                prior,
                next);
            GD.Print(
                "TASK-170 cube-face transition: " +
                $"{prior.FaceName}->{next.FaceName}; " +
                $"lat={next.Geographic.LatitudeDegrees.ToString("0.0000", CultureInfo.InvariantCulture)}; " +
                $"lon={next.Geographic.LongitudeDegrees.ToString("0.0000", CultureInfo.InvariantCulture)}; " +
                $"u={next.CubeFace.U.ToString("0.000", CultureInfo.InvariantCulture)}; " +
                $"v={next.CubeFace.V.ToString("0.000", CultureInfo.InvariantCulture)}; " +
                $"upDelta={upDelta.ToString("0.0000", CultureInfo.InvariantCulture)}deg; " +
                "physics=moving-tangent-frame.");
        }

        if (!forceAnnouncement && _planetRadialSurfaceReadyPrinted)
        {
            return;
        }

        GD.Print(
            "TASK-170 radial surface frame READY: " +
            $"planet={next.PlanetId}; " +
            $"face={next.FaceName}; " +
            $"uv={next.CubeFace.U.ToString("0.000", CultureInfo.InvariantCulture)}/" +
            $"{next.CubeFace.V.ToString("0.000", CultureInfo.InvariantCulture)}; " +
            $"lat={next.Geographic.LatitudeDegrees.ToString("0.0000", CultureInfo.InvariantCulture)}; " +
            $"lon={next.Geographic.LongitudeDegrees.ToString("0.0000", CultureInfo.InvariantCulture)}; " +
            $"gravity={next.GravityMetersPerSecondSquared.ToString("0.00", CultureInfo.InvariantCulture)}m/s2; " +
            "localUp=+Y; globalUp=planet-radial; streamer=25/9-bounded; " +
            "developer=surface_warp; persistence=logical-xz/no-schema-bump; F5=acceptance.");
        _planetRadialSurfaceReadyPrinted = true;
    }

    private string BuildPlanetRadialSurfaceHudLine()
    {
        if (_planetRadialSurfaceState is not { } state)
        {
            return "radial frame: unavailable";
        }
        return
            "radial frame: " +
            $"face={state.FaceName} " +
            $"uv={state.CubeFace.U.ToString("0.00", CultureInfo.InvariantCulture)}/" +
            $"{state.CubeFace.V.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"lat/lon={state.Geographic.LatitudeDegrees.ToString("0.000", CultureInfo.InvariantCulture)}/" +
            $"{state.Geographic.LongitudeDegrees.ToString("0.000", CultureInfo.InvariantCulture)}°; " +
            $"g={state.GravityMetersPerSecondSquared.ToString("0.00", CultureInfo.InvariantCulture)}m/s²";
    }

    private DeveloperCommandResult DeveloperSurfaceWarp(string[] parts)
    {
        if (parts.Length != 3 ||
            !double.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double latitude) ||
            !double.TryParse(
                parts[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double longitude))
        {
            return new DeveloperCommandResult(
                false,
                "usage: surface_warp <latitudeDeg> <longitudeDeg>");
        }
        if (!_surfaceRuntimeActive || StageOneVoyage.Piloted || _player is null)
        {
            return new DeveloperCommandResult(
                false,
                "surface_warp requires an on-foot active planet surface");
        }
        if (_planetRadialSurfaceRuntime is null || CurrentTerrainProfile is null)
        {
            return new DeveloperCommandResult(
                false,
                "radial surface runtime unavailable");
        }

        PlanetSurfaceCanonicalLogicalAddress target =
            _planetRadialSurfaceRuntime.WarpTarget(latitude, longitude);
        RestorePlanetSurfaceFrameAtLogicalPosition(
            target.EastMeters,
            target.NorthMeters);
        double terrainHeight = PlanetSurfaceTerrainRuntime.SampleHeight(
            CurrentTerrainProfile,
            target.EastMeters,
            target.NorthMeters);
        _player.GlobalPosition = new Vector3(
            0.0f,
            (float)terrainHeight + 1.25f,
            0.0f);
        _player.Velocity = Vector3.Zero;
        _planetSurfaceStreamingReadyPrinted = false;
        _planetSurfaceDistantTerrainCenter = null;
        _lastSurfaceResourceCenter = null;
        EnsurePlanetSurfaceDistantTerrain(CurrentTerrainProfile, force: true);
        RefreshStreamedSurfaceResources(force: true);
        UpdatePlanetaryPoiResidency();
        RefreshPlanetRadialSurfaceState(forceAnnouncement: true);

        PlanetSurfaceRadialFrameState state = _planetRadialSurfaceState!;
        StructuredGameLogger.Log(
            GameLogLevel.Information,
            GameLogCategory.PLAYER,
            "developer surface warp",
            fields: new System.Collections.Generic.Dictionary<string, object?>
            {
                ["latitude"] = state.Geographic.LatitudeDegrees,
                ["longitude"] = state.Geographic.LongitudeDegrees,
                ["face"] = state.FaceName
            });
        return new DeveloperCommandResult(
            true,
            $"surface lat={state.Geographic.LatitudeDegrees:F3}; " +
            $"lon={state.Geographic.LongitudeDegrees:F3}; face={state.FaceName}; " +
            $"logical={target.EastMeters:F1}/{target.NorthMeters:F1}m");
    }

    private void RunPlanetRadialSurfaceAcceptance()
    {
        if (_planetEnvironmentRuntime is null || _galaxyNavigationRuntime is null)
        {
            _planetRadialSurfaceAcceptanceHud = "FAIL — runtime unavailable";
            GD.PushError(
                "TASK-170 radial surface frame acceptance FAIL: runtime unavailable");
            return;
        }

        PlanetEnvironmentProfile[] profiles = GalaxyNavigation.CurrentSystem.Planets
            .Select(planet => PlanetEnvironment.BuildProfile(
                planet,
                GalaxyNavigation.CurrentSystem.StarType))
            .ToArray();
        PlanetSurfaceRadialFrameAcceptanceReport report =
            PlanetSurfaceRadialFrameAcceptanceRunner.Run(profiles);
        _planetRadialSurfaceAcceptanceHud = report.BuildHudLine();
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
