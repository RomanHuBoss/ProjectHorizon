using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private static readonly string[] Task216ResourceScenes =
    {
        "res://Assets/Models/Resources/RES_Ore_01.tscn",
        "res://Assets/Models/Resources/RES_Salvage_01.tscn",
        "res://Assets/Models/Resources/RES_Crystal_01.tscn",
        "res://Assets/Models/Resources/RES_Fiber_01.tscn",
        "res://Assets/Models/Resources/RES_Organic_01.tscn"
    };

    private static readonly string[] Task216ResourceGlbs =
        Task216ResourceScenes
            .SelectMany(path => Enumerable.Range(0, 3).Select(lod =>
                path.Replace(".tscn", $"_LOD{lod}.glb", StringComparison.Ordinal)))
            .ToArray();

    private string _productionModelArtAcceptanceHud = "READY";
    private bool? _productionModelArtAcceptancePassed;

    private void PrintProductionModelArtReady()
    {
        GD.Print(
            "TASK-216 production model art READY: " +
            "ships=explorer+interceptor-high-detail; station=industrial-detail-pass; " +
            "resources=ore+salvage+crystal+fiber+organic-GLB; lod=3-each; " +
            "resourceProcedural=fallback-only; collision=unchanged; " +
            "visualAcceptance=owner-required; F5=acceptance.");
    }

    private void RunProductionModelArtAcceptance()
    {
        _productionModelArtAcceptanceHud = "RUNNING";
        _productionModelArtAcceptancePassed = null;

        Node3D? playerModel = _voyageShip?.GetNodeOrNull<Node3D>("Visuals/ProductionExterior");
        Node3D? stationModel = GetNodeOrNull<Node3D>("Gameplay/OrbitalStation/ProductionModel");
        Node3D? npcModel = _npcShipNavigationNodes.Count > 0
            ? _npcShipNavigationNodes[0].GetNodeOrNull<Node3D>("ProductionModel")
            : null;

        Node? playerLod0 = playerModel?.GetNodeOrNull<Node>("LOD0");
        Node? stationLod0 = stationModel?.GetNodeOrNull<Node>("LOD0");
        Node? npcLod0 = npcModel?.GetNodeOrNull<Node>("LOD0");

        bool shipDetail =
            HasDescendantNamed(playerLod0, "SensorSpine") &&
            HasDescendantNamed(playerLod0, "GearDoorPort") &&
            HasDescendantNamed(playerLod0, "VentralRadiator_0") &&
            HasDescendantNamed(playerLod0, "AuthoredServiceModule") &&
            HasDescendantNamed(npcLod0, "DorsalSensor") &&
            HasDescendantNamed(npcLod0, "WeaponDoorPort") &&
            HasDescendantNamed(npcLod0, "AuthoredAvionicsModule");
        bool stationDetail =
            HasDescendantNamed(stationLod0, "RingService_00") &&
            HasDescendantNamed(stationLod0, "RadiatorRib_0_0") &&
            HasDescendantNamed(stationLod0, "CargoTank_00") &&
            HasDescendantNamed(stationLod0, "SpindleArmor_-27") &&
            HasDescendantNamed(stationLod0, "AuthoredCommsDish") &&
            HasDescendantNamed(stationLod0, "AuthoredServiceTrussA") &&
            HasDescendantNamed(stationLod0, "AuthoredServiceTrussB");

        SalvageResourceNode[] resources = GetTree()
            .GetNodesInGroup("vertical_slice_resource")
            .Concat(GetTree().GetNodesInGroup("planet_surface_resource"))
            .Concat(GetTree().GetNodesInGroup("cave_resource_deposit"))
            .OfType<SalvageResourceNode>()
            .Distinct()
            .ToArray();
        int productionResources = 0;
        int fallbackResources = 0;
        bool resourceLodReady = true;
        foreach (SalvageResourceNode resource in resources)
        {
            MeshInstance3D? visual = resource.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
            bool production = visual is not null &&
                visual.HasMeta("production_resource_visual") &&
                visual.GetMeta("production_resource_visual").AsBool();
            if (production && visual is not null)
            {
                productionResources++;
                resourceLodReady &= visual.GetNodeOrNull<ProductionModelLodController>("LodController") is not null;
            }
            else
            {
                fallbackResources++;
            }
        }

        int resourceGlbAssets = Task216ResourceGlbs.Count(path => ResourceLoader.Exists(path));
        int resourceFamilies = Task216ResourceScenes.Count(path => ResourceLoader.Exists(path));
        bool collisionSeparated =
            !ContainsCollisionShape(playerModel) &&
            !ContainsCollisionShape(stationModel) &&
            !ContainsCollisionShape(npcModel) &&
            resources.All(resource =>
                resource.GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is not Node visual ||
                !ContainsCollisionShape(visual));

        ProductionModelArtAcceptanceReport report =
            ProductionModelArtAcceptanceRunner.Evaluate(
                CountMeshInstances(playerLod0),
                CountMeshInstances(npcLod0),
                CountMeshInstances(stationLod0),
                resourceFamilies,
                resourceGlbAssets,
                productionResources,
                fallbackResources,
                shipDetail,
                stationDetail,
                resourceLodReady,
                collisionSeparated);

        _productionModelArtAcceptancePassed = report.Passed;
        _productionModelArtAcceptanceHud = report.Passed
            ? $"PASS art={report.PlayerMeshParts}/{report.NpcMeshParts}/{report.StationMeshParts} res={report.LiveProductionResources}"
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
