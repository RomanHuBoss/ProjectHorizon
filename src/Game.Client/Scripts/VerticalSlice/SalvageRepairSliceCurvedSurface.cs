using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _planetCurvedSurfaceAcceptanceHud = "READY";
    private bool _planetCurvedSurfaceReadyPrinted;

    private void UpdatePlanetCurvedSurfaceRuntime()
    {
        if (_planetCurvedSurfaceReadyPrinted || !_surfaceRuntimeActive ||
            _planetSurfaceContentProfile is null || _planetSurfaceStreamer is null ||
            !_planetSurfaceStreamer.IsStreamingSettled || _npcNavigationSurface is null)
        {
            return;
        }
        TerrainChunkProfilerSnapshot snapshot = _planetSurfaceStreamer.CaptureProfilerSnapshot();
        NpcNavigationSurfaceSnapshot nav = _npcNavigationSurface.CreateSnapshot();
        if (!snapshot.CurvedSurface || !nav.CurvedSurface)
        {
            return;
        }
        GD.Print(
            "TASK-174 curved cube-sphere surface READY: " +
            $"planet={PlanetSurfaceContentProfile.Environment.PlanetId}; " +
            $"radius={PlanetSurfaceContentProfile.Environment.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)}km; " +
            $"collision=curved-trimesh; nav=curved-tiles; face={nav.CenterFace}; " +
            "sky=radial-atmosphere; streamer=25/9; persistence=logical-xz; F5=acceptance.");
        _planetCurvedSurfaceReadyPrinted = true;
    }

    private string BuildPlanetCurvedSurfaceHudLine()
    {
        if (_planetSurfaceStreamer is null || _npcNavigationSurface is null)
        {
            return "curved surface: unavailable";
        }
        TerrainChunkProfilerSnapshot t = _planetSurfaceStreamer.CaptureProfilerSnapshot();
        NpcNavigationSurfaceSnapshot n = _npcNavigationSurface.CreateSnapshot();
        return $"curved surface: collision={(t.CurvedSurface ? 1 : 0)} nav={(n.CurvedSurface ? 1 : 0)} " +
            $"face={n.CenterFace} sag={n.MaximumCurvatureSagMeters.ToString("0.000", CultureInfo.InvariantCulture)}m sky={(_planetSurfaceAtmosphereFrameAligned ? 1 : 0)}";
    }

    private void RunPlanetCurvedSurfaceAcceptance()
    {
        if (_galaxyNavigationRuntime is null || _planetEnvironmentRuntime is null ||
            _planetSurfaceStreamer is null || _npcNavigationSurface is null)
        {
            _planetCurvedSurfaceAcceptanceHud = "FAIL unavailable";
            GD.PushError("TASK-174 curved cube-sphere surface acceptance FAIL: runtime unavailable");
            return;
        }
        PlanetEnvironmentProfile[] profiles = GalaxyNavigation.CurrentSystem.Planets
            .Select(p => PlanetEnvironment.BuildProfile(p, GalaxyNavigation.CurrentSystem.StarType))
            .Where(p => p.Landable)
            .ToArray();
        PlanetSurfaceCurvedCollisionAcceptanceReport report =
            PlanetSurfaceCurvedCollisionAcceptanceRunner.Run(profiles);
        TerrainChunkProfilerSnapshot terrain = _planetSurfaceStreamer.CaptureProfilerSnapshot();
        NpcNavigationSurfaceSnapshot nav = _npcNavigationSurface.CreateSnapshot();
        bool collision = terrain.CurvedSurface && terrain.LoadedChunks == 25 && terrain.Collisions == 9;
        bool navigation = nav.CurvedSurface && nav.ActiveRegions == nav.MaximumRegions;
        Vector3 expectedPlayerUp = GetCurrentPlanetCurvedWorldUp();
        bool sky = _planetSurfaceAtmosphereFrameAligned &&
            _planetSurfaceSkyFrameUp.Dot(expectedPlayerUp) >= 0.9999f;
        bool playerUp = _player is null ||
            _player.ActiveSurfaceUp.Dot(expectedPlayerUp) > 0.999f;
        bool passed = report.Passed && collision && navigation && sky && playerUp;
        _planetCurvedSurfaceAcceptanceHud = passed
            ? $"PASS faces={report.FacesCovered}/6 collision=25/9 nav=1 sky=1"
            : "FAIL curved-surface invariant";
        string output = $"TASK-174 curved cube-sphere surface acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"planets={report.Planets}; curvature={(report.Curvature ? 1 : 0)}; normals={(report.Normals ? 1 : 0)}; " +
            $"rebaseContinuity={(report.RebaseContinuity ? 1 : 0)}; faces={report.FacesCovered}/6; " +
            $"collision={(collision ? 1 : 0)}; navigation={(navigation ? 1 : 0)}; playerUp={(playerUp ? 1 : 0)}; " +
            $"skyRadial={(sky ? 1 : 0)}; bounded25x9={(terrain.LoadedChunks == 25 && terrain.Collisions == 9 ? 1 : 0)}; " +
            $"maxSag={report.MaximumSagMeters.ToString("0.000000", CultureInfo.InvariantCulture)}m; " +
            $"mapErr={report.MaximumRoundTripErrorMeters.ToString("0.000000", CultureInfo.InvariantCulture)}m; " +
            "outcome=" + (passed ? "curved collision/navigation patch and radial atmosphere verified" : "one or more curved surface invariants failed");
        if (passed) GD.Print(output); else GD.PushError(output);
    }
}
