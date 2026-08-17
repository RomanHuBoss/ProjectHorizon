public sealed record ProductionArtRecoveryAcceptanceReport(
    int HardSurfaceLodAssets,
    int ResourceGlbAssets,
    int LiveProductionResources,
    int LiveResourceFallbacks,
    float PrimaryHullLuminance,
    float CrystalVerticality,
    float IceVerticality,
    bool CollisionSeparated)
{
    public bool Passed =>
        HardSurfaceLodAssets >= 9 &&
        ResourceGlbAssets >= 30 &&
        LiveProductionResources >= 3 &&
        LiveResourceFallbacks == 0 &&
        PrimaryHullLuminance >= 0.55f &&
        CrystalVerticality >= 1.25f &&
        IceVerticality >= 1.20f &&
        CollisionSeparated;

    public string Result => Passed
        ? "production-art-recovery-runtime"
        : "production-art-recovery-incomplete";

    public string BuildOutputLine() =>
        $"TASK-220 production art recovery acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"hardSurfaceLod={HardSurfaceLodAssets}; resourceGlb={ResourceGlbAssets}; " +
        $"liveResources={LiveProductionResources}; fallbacks={LiveResourceFallbacks}; " +
        $"hullLuma={PrimaryHullLuminance:F3}; crystalVerticality={CrystalVerticality:F3}; " +
        $"iceVerticality={IceVerticality:F3}; collisionSeparate={(CollisionSeparated ? 1 : 0)}; " +
        $"result={Result}.";
}

public static class ProductionArtRecoveryAcceptanceRunner
{
    public static ProductionArtRecoveryAcceptanceReport Evaluate(
        int hardSurfaceLodAssets,
        int resourceGlbAssets,
        int liveProductionResources,
        int liveResourceFallbacks,
        float primaryHullLuminance,
        float crystalVerticality,
        float iceVerticality,
        bool collisionSeparated) =>
        new(
            hardSurfaceLodAssets,
            resourceGlbAssets,
            liveProductionResources,
            liveResourceFallbacks,
            primaryHullLuminance,
            crystalVerticality,
            iceVerticality,
            collisionSeparated);
}
