using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private static readonly string[] ProductionGlbResources =
    {
        "res://Assets/Models/Ships/SHP_Explorer_01_LOD0.glb",
        "res://Assets/Models/Ships/SHP_Explorer_01_LOD1.glb",
        "res://Assets/Models/Ships/SHP_Explorer_01_LOD2.glb",
        "res://Assets/Models/Ships/SHP_Interceptor_01_LOD0.glb",
        "res://Assets/Models/Ships/SHP_Interceptor_01_LOD1.glb",
        "res://Assets/Models/Ships/SHP_Interceptor_01_LOD2.glb",
        "res://Assets/Models/Stations/STN_Orbital_01_LOD0.glb",
        "res://Assets/Models/Stations/STN_Orbital_01_LOD1.glb",
        "res://Assets/Models/Stations/STN_Orbital_01_LOD2.glb"
    };

    private string _productionAssetPipelineAcceptanceHud = "READY";
    private bool? _productionAssetPipelineAcceptancePassed;

    private void PrintProductionAssetPipelineReady()
    {
        GD.Print(
            "TASK-184 production asset pipeline READY: " +
            "families=3; glb=9; lod=LOD0/LOD1/LOD2; units=meters; format=glTF2/GLB; " +
            "mountMarkers>=14; collision=separate; legacyFallback=hidden; F5=acceptance.");
    }

    private void RunProductionAssetPipelineAcceptance()
    {
        _productionAssetPipelineAcceptanceHud = "RUNNING";
        _productionAssetPipelineAcceptancePassed = null;

        Node3D? playerModel = _voyageShip?.GetNodeOrNull<Node3D>("Visuals/ProductionExterior");
        Node3D? stationModel = GetNodeOrNull<Node3D>("Gameplay/OrbitalStation/ProductionModel");
        bool npcAssetsLoaded = _npcShipNavigationNodes.Count > 0 &&
            _npcShipNavigationNodes.All(ship => ship.ProductionAssetLoaded);

        int glbAssets = ProductionGlbResources.Count(path => ResourceLoader.Exists(path));
        int lodChains = CountLodChains(playerModel) + CountLodChains(stationModel);
        if (_npcShipNavigationNodes.Count > 0 &&
            _npcShipNavigationNodes[0].GetNodeOrNull<Node3D>("ProductionModel") is Node3D npcModel)
        {
            lodChains += CountLodChains(npcModel);
        }

        int mountMarkers = CountMountMarkers(playerModel) + CountMountMarkers(stationModel);
        Node3D? firstNpcModel = _npcShipNavigationNodes.Count > 0
            ? _npcShipNavigationNodes[0].GetNodeOrNull<Node3D>("ProductionModel")
            : null;
        mountMarkers += CountMountMarkers(firstNpcModel);

        bool productionNodesCollisionFree =
            !ContainsCollisionShape(playerModel) &&
            !ContainsCollisionShape(stationModel) &&
            !ContainsCollisionShape(firstNpcModel);
        bool gameplayCollisionPresent =
            _voyageShip?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape is not null &&
            GetNodeOrNull<Node3D>("Gameplay/OrbitalStation")?
                .GetChildren().Count(child => child is CollisionShape3D) >= 20 &&
            _npcShipNavigationNodes.All(ship =>
                ship.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape is not null);

        bool legacyFallbackHidden =
            _voyageShip?.GetNodeOrNull<MeshInstance3D>("Visuals/Hull")?.Visible == false &&
            GetNodeOrNull<MeshInstance3D>("Gameplay/OrbitalStation/MeshInstance3D")?.Visible == false &&
            _npcShipNavigationNodes.All(ship =>
                ship.GetNodeOrNull<Node3D>("LegacyProceduralFallback")?.Visible == false);

        bool lodControllerPresent =
            playerModel is ProductionModelLodController &&
            stationModel is ProductionModelLodController &&
            firstNpcModel is ProductionModelLodController;

        ProductionAssetPipelineAcceptanceReport report =
            ProductionAssetPipelineAcceptanceRunner.Evaluate(
                assetFamilies: 3,
                glbAssets: glbAssets,
                lodChains: lodChains,
                mountMarkers: mountMarkers,
                playerAssetLoaded: playerModel is not null,
                stationAssetLoaded: stationModel is not null,
                npcAssetsLoaded: npcAssetsLoaded,
                collisionSeparated: productionNodesCollisionFree && gameplayCollisionPresent,
                legacyFallbackHidden: legacyFallbackHidden,
                lodControllerPresent: lodControllerPresent);

        _productionAssetPipelineAcceptancePassed = report.Passed;
        _productionAssetPipelineAcceptanceHud = report.Passed
            ? $"PASS glb={report.GlbAssets}/9 lod={report.LodChains}/3 markers={report.MountMarkers}"
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

    private static int CountLodChains(Node? root) => root is not null &&
        root.GetNodeOrNull<Node3D>("LOD0") is not null &&
        root.GetNodeOrNull<Node3D>("LOD1") is not null &&
        root.GetNodeOrNull<Node3D>("LOD2") is not null
            ? 1
            : 0;

    private static int CountMountMarkers(Node? root)
    {
        if (root is null)
        {
            return 0;
        }

        int count = root.Name.ToString().StartsWith("MNT_", System.StringComparison.Ordinal)
            ? 1
            : 0;
        foreach (Node child in root.GetChildren())
        {
            count += CountMountMarkers(child);
        }
        return count;
    }

    private static bool ContainsCollisionShape(Node? root)
    {
        if (root is null)
        {
            return false;
        }
        if (root is CollisionShape3D)
        {
            return true;
        }
        foreach (Node child in root.GetChildren())
        {
            if (ContainsCollisionShape(child))
            {
                return true;
            }
        }
        return false;
    }
}
