using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _productionVisualLanguageAcceptanceHud = "READY";
    private bool? _productionVisualLanguageAcceptancePassed;

    private void PrintProductionVisualLanguageReady()
    {
        GD.Print(
            "TASK-180 production visual language READY: " +
            $"playerExterior>={ProductionVisualLanguageAcceptanceRunner.MinimumPlayerExteriorParts}; " +
            $"cockpit>={ProductionVisualLanguageAcceptanceRunner.MinimumCockpitDetailParts}; " +
            $"stationDetails>={ProductionVisualLanguageAcceptanceRunner.MinimumStationDetailParts}; " +
            $"npcShip={NpcShipNavigationNode.ProductionVisualPartCount}; " +
            $"planetMaterialVariants={DetailedPlanetGlobeNode.ProductionTerrainMaterialVariants}; " +
            "materials=semantic-PBR; playerPrimaryCollision=unchanged; " +
            "stationPhysicalEnvelope=TASK-180.1; externalArtAssets=0; F5=acceptance.");
    }

    private void RunProductionVisualLanguageAcceptance()
    {
        _productionVisualLanguageAcceptanceHud = "RUNNING";
        _productionVisualLanguageAcceptancePassed = null;
        Node3D? shipVisuals = _voyageShip?.GetNodeOrNull<Node3D>("Visuals");
        Node3D? cockpitInterior = _voyageShip?
            .GetNodeOrNull<Node3D>("Visuals/CockpitInterior");
        Node3D? stationDetail = GetNodeOrNull<Node3D>(
            "Gameplay/OrbitalStation/VisualDetail");
        CollisionShape3D? shipCollision = _voyageShip?
            .GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        CollisionShape3D? stationCollision = GetNodeOrNull<CollisionShape3D>(
            "Gameplay/OrbitalStation/CollisionShape3D");

        int playerExteriorParts = CountDirectMeshes(shipVisuals);
        int cockpitDetailParts = CountDirectMeshes(cockpitInterior);
        int stationDetailParts = CountDirectMeshes(stationDetail);
        int npcShipDetailParts = _npcShipNavigationNodes.Count == 0
            ? 0
            : _npcShipNavigationNodes.Min(ship => CountDirectMeshes(ship));
        int planetMaterialVariants = _starSystemSimulationNode?
            .DetailedPlanetTerrainMaterialVariants ?? 0;
        int semanticMaterialProfiles = _starSystemSimulationNode?
            .ProductionVisualProfileCount ?? 0;
        bool visualOnlyDetails = stationDetail is not null &&
            stationDetail.GetChildren().All(child => child is MeshInstance3D) &&
            cockpitInterior is not null &&
            cockpitInterior.GetChildren().All(child =>
                child is MeshInstance3D or OmniLight3D);

        ProductionVisualLanguageAcceptanceReport report =
            ProductionVisualLanguageAcceptanceRunner.Evaluate(
                playerExteriorParts,
                cockpitDetailParts,
                stationDetailParts,
                npcShipDetailParts,
                planetMaterialVariants,
                semanticMaterialProfiles,
                shipCollision?.Shape is BoxShape3D,
                stationCollision?.Shape is BoxShape3D,
                visualOnlyDetails);
        _productionVisualLanguageAcceptancePassed = report.Passed;
        _productionVisualLanguageAcceptanceHud = report.Passed
            ? $"PASS ship={report.PlayerExteriorParts} cockpit={report.CockpitDetailParts} station={report.StationDetailParts} materials={report.PlanetMaterialVariants}/6"
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

    private static int CountDirectMeshes(Node? root) => root is null
        ? 0
        : root.GetChildren().Count(child => child is MeshInstance3D);
}
