public sealed record ProductionSurfaceArtAcceptanceReport(
    int TextureAtlasMaps,
    bool TextureAtlasDimensionCompliant,
    int HardSurfaceLodAssets,
    int ResourceFamilies,
    int ResourceGlbAssets,
    int LiveProductionResources,
    int LiveResourceFallbacks,
    bool CollisionSeparated)
{
    public bool Passed =>
        TextureAtlasMaps >= 4 &&
        TextureAtlasDimensionCompliant &&
        HardSurfaceLodAssets >= 9 &&
        ResourceFamilies >= 10 &&
        ResourceGlbAssets >= 30 &&
        LiveProductionResources >= 3 &&
        LiveResourceFallbacks == 0 &&
        CollisionSeparated;

    public string Result => Passed
        ? "production-surface-art-runtime"
        : "production-surface-art-incomplete";

    public string BuildOutputLine() =>
        $"TASK-218 production surface art acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"atlasMaps={TextureAtlasMaps}; atlasDimension={(TextureAtlasDimensionCompliant ? 1 : 0)}; " +
        $"hardSurfaceLod={HardSurfaceLodAssets}; resourceFamilies={ResourceFamilies}; " +
        $"resourceGlb={ResourceGlbAssets}; liveResources={LiveProductionResources}; " +
        $"fallbacks={LiveResourceFallbacks}; collisionSeparate={(CollisionSeparated ? 1 : 0)}; result={Result}.";
}

public static class ProductionSurfaceArtAcceptanceRunner
{
    public static ProductionSurfaceArtAcceptanceReport Evaluate(
        int textureAtlasMaps,
        bool textureAtlasDimensionCompliant,
        int hardSurfaceLodAssets,
        int resourceFamilies,
        int resourceGlbAssets,
        int liveProductionResources,
        int liveResourceFallbacks,
        bool collisionSeparated) =>
        new(
            textureAtlasMaps,
            textureAtlasDimensionCompliant,
            hardSurfaceLodAssets,
            resourceFamilies,
            resourceGlbAssets,
            liveProductionResources,
            liveResourceFallbacks,
            collisionSeparated);
}
