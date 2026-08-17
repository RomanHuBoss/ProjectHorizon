using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private static readonly string[] Task218TextureAtlasMaps =
    {
        "res://Assets/Textures/Production/TEX_HardSurface_BaseColor.png",
        "res://Assets/Textures/Production/TEX_HardSurface_Normal.png",
        "res://Assets/Textures/Production/TEX_HardSurface_MetallicRoughness.png",
        "res://Assets/Textures/Production/TEX_HardSurface_Emission.png"
    };

    private static readonly string[] Task218HardSurfaceGlbs =
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

    private static readonly string[] Task218ResourceScenes =
    {
        "res://Assets/Models/Resources/RES_Ore_01.tscn",
        "res://Assets/Models/Resources/RES_Salvage_01.tscn",
        "res://Assets/Models/Resources/RES_Crystal_01.tscn",
        "res://Assets/Models/Resources/RES_Fiber_01.tscn",
        "res://Assets/Models/Resources/RES_Organic_01.tscn",
        "res://Assets/Models/Resources/RES_Ice_01.tscn",
        "res://Assets/Models/Resources/RES_Gas_01.tscn",
        "res://Assets/Models/Resources/RES_Salt_01.tscn",
        "res://Assets/Models/Resources/RES_Glass_01.tscn",
        "res://Assets/Models/Resources/RES_Exotic_01.tscn"
    };

    private static readonly string[] Task218ResourceGlbs =
        Task218ResourceScenes
            .SelectMany(path => Enumerable.Range(0, 3).Select(lod =>
                path.Replace(".tscn", $"_LOD{lod}.glb", StringComparison.Ordinal)))
            .ToArray();

    private string _productionSurfaceArtAcceptanceHud = "READY";
    private bool? _productionSurfaceArtAcceptancePassed;

    private void PrintProductionSurfaceArtReady()
    {
        GD.Print(
            "TASK-218 production surface art READY: atlas=4x1024; hardSurfaceMaterial=shared-PBR; " +
            "resourceFamilies=10; resourceGlb=30; catalogRouting=semantic; collision=unchanged; " +
            "manualVisual=owner-required; F5=acceptance.");
    }

    private void RunProductionSurfaceArtAcceptance()
    {
        _productionSurfaceArtAcceptanceHud = "RUNNING";
        _productionSurfaceArtAcceptancePassed = null;

        int atlasMaps = Task218TextureAtlasMaps.Count(path => ResourceLoader.Exists(path));
        bool atlasDimension = true;
        foreach (string path in Task218TextureAtlasMaps)
        {
            Texture2D? texture = ResourceLoader.Load<Texture2D>(path);
            atlasDimension &= texture is not null &&
                texture.GetWidth() == 1024 && texture.GetHeight() == 1024;
        }

        int hardSurfaceLodAssets = Task218HardSurfaceGlbs.Count(path => ResourceLoader.Exists(path));
        int resourceFamilies = Task218ResourceScenes.Count(path => ResourceLoader.Exists(path));
        int resourceGlbAssets = Task218ResourceGlbs.Count(path => ResourceLoader.Exists(path));

        SalvageResourceNode[] resources = GetTree()
            .GetNodesInGroup("vertical_slice_resource")
            .Concat(GetTree().GetNodesInGroup("planet_surface_resource"))
            .Concat(GetTree().GetNodesInGroup("cave_resource_deposit"))
            .OfType<SalvageResourceNode>()
            .Distinct()
            .ToArray();
        int productionResources = 0;
        int fallbackResources = 0;
        foreach (SalvageResourceNode resource in resources)
        {
            MeshInstance3D? visual = resource.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
            bool production = visual is not null &&
                visual.HasMeta("production_resource_visual") &&
                visual.GetMeta("production_resource_visual").AsBool();
            if (production)
            {
                productionResources++;
            }
            else
            {
                fallbackResources++;
            }
        }

        Node3D? playerModel = _voyageShip?.GetNodeOrNull<Node3D>("Visuals/ProductionExterior");
        Node3D? stationModel = GetNodeOrNull<Node3D>("Gameplay/OrbitalStation/ProductionModel");
        Node3D? npcModel = _npcShipNavigationNodes.Count > 0
            ? _npcShipNavigationNodes[0].GetNodeOrNull<Node3D>("ProductionModel")
            : null;
        bool collisionSeparated =
            !ContainsCollisionShape(playerModel) &&
            !ContainsCollisionShape(stationModel) &&
            !ContainsCollisionShape(npcModel) &&
            resources.All(resource =>
                resource.GetNodeOrNull<MeshInstance3D>("MeshInstance3D") is not Node visual ||
                !ContainsCollisionShape(visual));

        ProductionSurfaceArtAcceptanceReport report =
            ProductionSurfaceArtAcceptanceRunner.Evaluate(
                atlasMaps,
                atlasDimension,
                hardSurfaceLodAssets,
                resourceFamilies,
                resourceGlbAssets,
                productionResources,
                fallbackResources,
                collisionSeparated);

        _productionSurfaceArtAcceptancePassed = report.Passed;
        _productionSurfaceArtAcceptanceHud = report.Passed
            ? $"PASS atlas={report.TextureAtlasMaps} res={report.ResourceFamilies}/{report.ResourceGlbAssets} live={report.LiveProductionResources}"
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
