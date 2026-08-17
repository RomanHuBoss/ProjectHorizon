public sealed record ProductionModelArtAcceptanceReport(
    int PlayerMeshParts,
    int NpcMeshParts,
    int StationMeshParts,
    int ResourceFamilies,
    int ResourceGlbAssets,
    int LiveProductionResources,
    int LiveResourceFallbacks,
    bool DetailedShipSignature,
    bool DetailedStationSignature,
    bool ResourceLodReady,
    bool CollisionSeparated)
{
    public bool Passed =>
        PlayerMeshParts >= 35 &&
        NpcMeshParts >= 18 &&
        StationMeshParts >= 100 &&
        ResourceFamilies >= 5 &&
        ResourceGlbAssets >= 15 &&
        LiveProductionResources >= 3 &&
        LiveResourceFallbacks == 0 &&
        DetailedShipSignature &&
        DetailedStationSignature &&
        ResourceLodReady &&
        CollisionSeparated;

    public string Result => Passed
        ? "production-art-model-overhaul-runtime"
        : "production-art-model-overhaul-incomplete";

    public string BuildOutputLine() =>
        $"TASK-216 production model art acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"playerParts={PlayerMeshParts}; npcParts={NpcMeshParts}; stationParts={StationMeshParts}; " +
        $"resourceFamilies={ResourceFamilies}; resourceGlb={ResourceGlbAssets}; " +
        $"liveResources={LiveProductionResources}; fallbacks={LiveResourceFallbacks}; " +
        $"shipDetail={Bool(DetailedShipSignature)}; stationDetail={Bool(DetailedStationSignature)}; " +
        $"resourceLod={Bool(ResourceLodReady)}; collisionSeparate={Bool(CollisionSeparated)}; result={Result}.";

    private static int Bool(bool value) => value ? 1 : 0;
}

public static class ProductionModelArtAcceptanceRunner
{
    public static ProductionModelArtAcceptanceReport Evaluate(
        int playerMeshParts,
        int npcMeshParts,
        int stationMeshParts,
        int resourceFamilies,
        int resourceGlbAssets,
        int liveProductionResources,
        int liveResourceFallbacks,
        bool detailedShipSignature,
        bool detailedStationSignature,
        bool resourceLodReady,
        bool collisionSeparated) =>
        new(
            playerMeshParts,
            npcMeshParts,
            stationMeshParts,
            resourceFamilies,
            resourceGlbAssets,
            liveProductionResources,
            liveResourceFallbacks,
            detailedShipSignature,
            detailedStationSignature,
            resourceLodReady,
            collisionSeparated);
}
