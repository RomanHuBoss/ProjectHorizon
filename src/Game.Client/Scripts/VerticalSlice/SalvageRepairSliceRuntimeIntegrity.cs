using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _runtimeIntegrityAcceptanceHud = "READY";
    private bool? _runtimeIntegrityAcceptancePassed;

    private void PrintRuntimeIntegrityReady()
    {
        GD.Print(
            "TASK-180.1 runtime integrity READY: planet=opaque-core+two-sided-cube-sphere; " +
            $"stationShapes>={RuntimeIntegrityAcceptanceRunner.MinimumStationCollisionShapes}; " +
            "stationSweep=continuous-compound; terrainObserver=no-int-min-sentinel; " +
            "orbitShadows=bounded-disabled; surfaceContact=debounced; F5=acceptance.");
    }

    private void RunRuntimeIntegrityAcceptance()
    {
        _runtimeIntegrityAcceptanceHud = "RUNNING";
        _runtimeIntegrityAcceptancePassed = null;

        DetailedPlanetGlobeDiagnostics globe = _starSystemSimulationNode?
            .CreateDetailedGlobeDiagnostics() ?? new DetailedPlanetGlobeDiagnostics(
                string.Empty, 0, 0, 0, 0, 0, 0.0f, 0.0f,
                false, false, false, false, 0.0f);
        int stationShapes = _orbitalStation is null
            ? 0
            : _orbitalStation.GetChildren().Count(child =>
                child is CollisionShape3D shape &&
                !shape.Disabled &&
                shape.Shape is not null);

        bool stationSweepGuard =
            OrbitalStationCollisionRuntime.TrySweepExpandedAabb(
                new Vector3(0.0f, 0.0f, 80.0f),
                new Vector3(0.0f, 0.0f, -80.0f),
                new Vector3(27.0f, 8.0f, 19.0f),
                OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters,
                out OrbitalStationCollisionHit stationHit) &&
            stationHit.SegmentFraction is > 0.0f and < 1.0f;

        Vector2I terrainChunk = _planetSurfaceStreamer?.CurrentChunk ??
            new Vector2I(int.MinValue, int.MinValue);
        bool terrainObserverResolved =
            terrainChunk.X != int.MinValue && terrainChunk.Y != int.MinValue;

        RuntimeIntegrityAcceptanceReport report =
            RuntimeIntegrityAcceptanceRunner.Evaluate(
                globe.OpaqueCoreShell,
                globe.FaceCount,
                stationShapes,
                stationSweepGuard,
                terrainObserverResolved);
        _runtimeIntegrityAcceptancePassed = report.Passed;
        _runtimeIntegrityAcceptanceHud = report.Passed
            ? $"PASS planet={report.PlanetFaces}/6 station={report.StationCollisionShapes} sweep=1 terrain=1"
            : $"FAIL {report.Result}";
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
