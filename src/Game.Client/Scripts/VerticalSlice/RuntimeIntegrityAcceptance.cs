public sealed record RuntimeIntegrityAcceptanceReport(
    bool Passed,
    bool PlanetClosed,
    int PlanetFaces,
    int StationCollisionShapes,
    bool StationSweepGuard,
    bool TerrainObserverResolved,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-180.1 runtime integrity acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"planetClosed={(PlanetClosed ? 1 : 0)}; planetFaces={PlanetFaces}; " +
        $"stationShapes={StationCollisionShapes}; stationSweep={(StationSweepGuard ? 1 : 0)}; " +
        $"terrainObserver={(TerrainObserverResolved ? 1 : 0)}; result={Result}";
}

public static class RuntimeIntegrityAcceptanceRunner
{
    public const int RequiredPlanetFaces = 6;
    public const int MinimumStationCollisionShapes =
        OrbitalStationCollisionRuntime.MinimumPhysicalShapeCount;

    public static RuntimeIntegrityAcceptanceReport Evaluate(
        bool planetClosed,
        int planetFaces,
        int stationCollisionShapes,
        bool stationSweepGuard,
        bool terrainObserverResolved)
    {
        bool passed =
            planetClosed &&
            planetFaces == RequiredPlanetFaces &&
            stationCollisionShapes >= MinimumStationCollisionShapes &&
            stationSweepGuard &&
            terrainObserverResolved;
        return new RuntimeIntegrityAcceptanceReport(
            passed,
            planetClosed,
            planetFaces,
            stationCollisionShapes,
            stationSweepGuard,
            terrainObserverResolved,
            passed
                ? "closed orbital planet, compound station collision, swept anti-tunneling and resolved terrain observer verified"
                : "one or more TASK-180.1 runtime integrity invariants failed");
    }
}
