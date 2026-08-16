public sealed record ProductionVisualLanguageAcceptanceReport(
    bool Passed,
    int PlayerExteriorParts,
    int CockpitDetailParts,
    int StationDetailParts,
    int NpcShipDetailParts,
    int PlanetMaterialVariants,
    int SemanticMaterialProfiles,
    bool ShipCollisionPreserved,
    bool StationCollisionPreserved,
    bool VisualOnlyDetails,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-180 production visual language acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"playerExterior={PlayerExteriorParts}; cockpit={CockpitDetailParts}; " +
        $"station={StationDetailParts}; npcShip={NpcShipDetailParts}; " +
        $"planetMaterials={PlanetMaterialVariants}; semanticProfiles={SemanticMaterialProfiles}; " +
        $"shipCollision={(ShipCollisionPreserved ? 1 : 0)}; " +
        $"stationCollision={(StationCollisionPreserved ? 1 : 0)}; " +
        $"visualOnly={(VisualOnlyDetails ? 1 : 0)}; result={Result}";
}

public static class ProductionVisualLanguageAcceptanceRunner
{
    public const int MinimumPlayerExteriorParts = 11;
    public const int MinimumCockpitDetailParts = 9;
    public const int MinimumStationDetailParts = 6;
    public const int MinimumNpcShipDetailParts = 9;
    public const int RequiredPlanetMaterialVariants = 6;

    public static ProductionVisualLanguageAcceptanceReport Evaluate(
        int playerExteriorParts,
        int cockpitDetailParts,
        int stationDetailParts,
        int npcShipDetailParts,
        int planetMaterialVariants,
        int semanticMaterialProfiles,
        bool shipCollisionPreserved,
        bool stationCollisionPreserved,
        bool visualOnlyDetails)
    {
        bool passed =
            playerExteriorParts >= MinimumPlayerExteriorParts &&
            cockpitDetailParts >= MinimumCockpitDetailParts &&
            stationDetailParts >= MinimumStationDetailParts &&
            npcShipDetailParts >= MinimumNpcShipDetailParts &&
            planetMaterialVariants == RequiredPlanetMaterialVariants &&
            semanticMaterialProfiles > 0 &&
            shipCollisionPreserved &&
            stationCollisionPreserved &&
            visualOnlyDetails;
        return new ProductionVisualLanguageAcceptanceReport(
            passed,
            playerExteriorParts,
            cockpitDetailParts,
            stationDetailParts,
            npcShipDetailParts,
            planetMaterialVariants,
            semanticMaterialProfiles,
            shipCollisionPreserved,
            stationCollisionPreserved,
            visualOnlyDetails,
            passed
                ? "procedural production silhouettes, cockpit, station detail and semantic materials verified with primary collision roots preserved; station envelope is extended by TASK-180.1"
                : "one or more TASK-180 production visual language invariants failed");
    }
}
