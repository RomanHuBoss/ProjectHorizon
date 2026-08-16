public sealed record HardSurfaceVisualRedesignAcceptanceReport(
    int PlayerMeshParts,
    int NpcMeshParts,
    int StationMeshParts,
    bool PlayerSignaturePresent,
    bool NpcSignaturePresent,
    bool StationSignaturePresent,
    bool LegacyFallbackHidden,
    bool CollisionSeparated,
    bool LodReady)
{
    public bool Passed =>
        PlayerMeshParts >= 10 &&
        NpcMeshParts >= 8 &&
        StationMeshParts >= 28 &&
        PlayerSignaturePresent &&
        NpcSignaturePresent &&
        StationSignaturePresent &&
        LegacyFallbackHidden &&
        CollisionSeparated &&
        LodReady;

    public string Result => Passed
        ? "hard-surface-visual-redesign-runtime"
        : "hard-surface-visual-redesign-incomplete";

    public string BuildOutputLine() =>
        $"TASK-186 hard-surface visual redesign acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"playerParts={PlayerMeshParts}; npcParts={NpcMeshParts}; stationParts={StationMeshParts}; " +
        $"playerSignature={Bool(PlayerSignaturePresent)}; npcSignature={Bool(NpcSignaturePresent)}; " +
        $"stationSignature={Bool(StationSignaturePresent)}; fallbackHidden={Bool(LegacyFallbackHidden)}; " +
        $"collisionSeparate={Bool(CollisionSeparated)}; lodReady={Bool(LodReady)}; result={Result}.";

    private static int Bool(bool value) => value ? 1 : 0;
}

public static class HardSurfaceVisualRedesignAcceptanceRunner
{
    public static HardSurfaceVisualRedesignAcceptanceReport Evaluate(
        int playerMeshParts,
        int npcMeshParts,
        int stationMeshParts,
        bool playerSignaturePresent,
        bool npcSignaturePresent,
        bool stationSignaturePresent,
        bool legacyFallbackHidden,
        bool collisionSeparated,
        bool lodReady) =>
        new(
            playerMeshParts,
            npcMeshParts,
            stationMeshParts,
            playerSignaturePresent,
            npcSignaturePresent,
            stationSignaturePresent,
            legacyFallbackHidden,
            collisionSeparated,
            lodReady);
}
