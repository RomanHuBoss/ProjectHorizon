using System;

public sealed record ProductionAssetPipelineAcceptanceReport(
    int AssetFamilies,
    int GlbAssets,
    int LodChains,
    int MountMarkers,
    bool PlayerAssetLoaded,
    bool StationAssetLoaded,
    bool NpcAssetsLoaded,
    bool CollisionSeparated,
    bool LegacyFallbackHidden,
    bool LodControllerPresent)
{
    public bool Passed =>
        AssetFamilies >= 3 &&
        GlbAssets >= 9 &&
        LodChains >= 3 &&
        MountMarkers >= 14 &&
        PlayerAssetLoaded &&
        StationAssetLoaded &&
        NpcAssetsLoaded &&
        CollisionSeparated &&
        LegacyFallbackHidden &&
        LodControllerPresent;

    public string Result => Passed
        ? "section-33-production-asset-pipeline"
        : "production-asset-pipeline-incomplete";

    public string BuildOutputLine() =>
        $"TASK-184 production asset pipeline acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"families={AssetFamilies}; glb={GlbAssets}; lodChains={LodChains}; markers={MountMarkers}; " +
        $"player={Bool(PlayerAssetLoaded)}; station={Bool(StationAssetLoaded)}; npc={Bool(NpcAssetsLoaded)}; " +
        $"collisionSeparate={Bool(CollisionSeparated)}; fallbackHidden={Bool(LegacyFallbackHidden)}; " +
        $"lodController={Bool(LodControllerPresent)}; result={Result}.";

    private static int Bool(bool value) => value ? 1 : 0;
}

public static class ProductionAssetPipelineAcceptanceRunner
{
    public const int RequiredAssetFamilies = 3;
    public const int RequiredGlbAssets = 9;
    public const int RequiredLodChains = 3;
    public const int RequiredMountMarkers = 14;

    public static ProductionAssetPipelineAcceptanceReport Evaluate(
        int assetFamilies,
        int glbAssets,
        int lodChains,
        int mountMarkers,
        bool playerAssetLoaded,
        bool stationAssetLoaded,
        bool npcAssetsLoaded,
        bool collisionSeparated,
        bool legacyFallbackHidden,
        bool lodControllerPresent) =>
        new(
            assetFamilies,
            glbAssets,
            lodChains,
            mountMarkers,
            playerAssetLoaded,
            stationAssetLoaded,
            npcAssetsLoaded,
            collisionSeparated,
            legacyFallbackHidden,
            lodControllerPresent);
}
