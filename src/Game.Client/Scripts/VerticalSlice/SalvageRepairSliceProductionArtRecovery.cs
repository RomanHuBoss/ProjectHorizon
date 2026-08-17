using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private const string Task220CrystalLod0 =
        "res://Assets/Models/Resources/RES_Crystal_01_LOD0.glb";
    private const string Task220IceLod0 =
        "res://Assets/Models/Resources/RES_Ice_01_LOD0.glb";

    private string _productionArtRecoveryAcceptanceHud = "READY";
    private bool? _productionArtRecoveryAcceptancePassed;

    private void PrintProductionArtRecoveryReady()
    {
        GD.Print(
            "TASK-220 production art recovery READY: ownerRejected=TASK-216+TASK-218; " +
            "shipPalette=light-industrial-alloy; darkMass=canopy+recess-only; " +
            "crystalAxis=Y-up; crystalVerticality>=1.25; iceVerticality>=1.20; " +
            "resourceMaterial=semantic-role-contrast; collision=unchanged; manualVisual=owner-required; F5=acceptance.");
    }

    private void RunProductionArtRecoveryAcceptance()
    {
        _productionArtRecoveryAcceptanceHud = "RUNNING";
        _productionArtRecoveryAcceptancePassed = null;

        int hardSurfaceLodAssets = Task218HardSurfaceGlbs.Count(path => ResourceLoader.Exists(path));
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

        float hullLuminance = ReadPrimaryHullLuminance();
        float crystalVerticality = ReadPackedSceneVerticality(Task220CrystalLod0);
        float iceVerticality = ReadPackedSceneVerticality(Task220IceLod0);

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

        ProductionArtRecoveryAcceptanceReport report =
            ProductionArtRecoveryAcceptanceRunner.Evaluate(
                hardSurfaceLodAssets,
                resourceGlbAssets,
                productionResources,
                fallbackResources,
                hullLuminance,
                crystalVerticality,
                iceVerticality,
                collisionSeparated);

        _productionArtRecoveryAcceptancePassed = report.Passed;
        _productionArtRecoveryAcceptanceHud = report.Passed
            ? $"PASS hull={report.PrimaryHullLuminance:F2} crystal={report.CrystalVerticality:F2} ice={report.IceVerticality:F2}"
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

    private static float ReadPrimaryHullLuminance()
    {
        Texture2D? texture = ResourceLoader.Load<Texture2D>(Task218TextureAtlasMaps[0]);
        Image? image = texture?.GetImage();
        if (image is null || image.GetWidth() < 256 || image.GetHeight() < 256)
        {
            return 0.0f;
        }

        // Cell (0,0) is the legacy MAT_Hull_Graphite semantic slot. TASK-220
        // deliberately turns it into a light primary alloy while preserving the stable ID.
        Color color = image.GetPixel(96, 96);
        return color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
    }

    private static float ReadPackedSceneVerticality(string path)
    {
        PackedScene? packed = ResourceLoader.Load<PackedScene>(path);
        Node3D? root = packed?.Instantiate<Node3D>();
        if (root is null)
        {
            return 0.0f;
        }

        bool hasBounds = false;
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        AccumulateMeshBounds(root, Transform3D.Identity, ref hasBounds, ref minimum, ref maximum);
        root.Free();
        if (!hasBounds)
        {
            return 0.0f;
        }

        Vector3 size = maximum - minimum;
        float footprint = Math.Max(size.X, size.Z);
        return footprint > 0.0001f ? size.Y / footprint : 0.0f;
    }

    private static void AccumulateMeshBounds(
        Node node,
        Transform3D parentTransform,
        ref bool hasBounds,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        Transform3D transform = parentTransform;
        if (node is Node3D spatial)
        {
            transform = parentTransform * spatial.Transform;
        }

        if (node is MeshInstance3D meshInstance && meshInstance.Mesh is not null)
        {
            Aabb bounds = meshInstance.Mesh.GetAabb();
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 local = bounds.Position + new Vector3(
                            bounds.Size.X * x,
                            bounds.Size.Y * y,
                            bounds.Size.Z * z);
                        Vector3 point = transform * local;
                        minimum = new Vector3(
                            Math.Min(minimum.X, point.X),
                            Math.Min(minimum.Y, point.Y),
                            Math.Min(minimum.Z, point.Z));
                        maximum = new Vector3(
                            Math.Max(maximum.X, point.X),
                            Math.Max(maximum.Y, point.Y),
                            Math.Max(maximum.Z, point.Z));
                        hasBounds = true;
                    }
                }
            }
        }

        foreach (Node child in node.GetChildren())
        {
            AccumulateMeshBounds(child, transform, ref hasBounds, ref minimum, ref maximum);
        }
    }
}
