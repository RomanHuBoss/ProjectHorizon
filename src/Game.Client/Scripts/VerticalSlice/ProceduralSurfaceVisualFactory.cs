using System;
using System.Linq;
using Godot;

/// <summary>
/// TASK-164 main-thread-only procedural visual language for live surface props.
/// It intentionally creates render geometry only; gameplay collision, identity,
/// persistence and interaction remain owned by the existing runtime nodes.
/// </summary>
public static class ProceduralSurfaceVisualFactory
{
    public static MeshInstance3D CreateResourceVisual(GameResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string family = ResolveResourceFamily(definition);
        string assetKey = ResolveResourceAssetKey(definition, family);
        string scenePath = $"res://Assets/Models/Resources/RES_{assetKey}_01.tscn";
        PackedScene? packed = ResourceLoader.Load<PackedScene>(scenePath);
        if (packed is not null)
        {
            MeshInstance3D? production = packed.Instantiate<MeshInstance3D>();
            if (production is not null)
            {
                production.Name = "MeshInstance3D";
                production.SetMeta("surface_visual_family", family);
                production.SetMeta("surface_visual_asset", assetKey);
                production.SetMeta("surface_visual_parts", 3);
                production.SetMeta("production_resource_visual", true);
                return production;
            }
        }

        MeshInstance3D fallback = CreateProceduralResourceFallback(definition, family);
        fallback.SetMeta("production_resource_visual", false);
        return fallback;
    }

    public static string ResolveResourceAssetKey(
        GameResourceDefinition definition,
        string? family = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.Equals(definition.ResourceId, "resource.salvage_alloy", StringComparison.Ordinal) ||
            definition.Tags.Contains("salvage", StringComparer.Ordinal))
        {
            return "Salvage";
        }

        return (family ?? ResolveResourceFamily(definition)) switch
        {
            "crystal" => "Crystal",
            "fiber" => "Fiber",
            "organic" => "Organic",
            _ => "Ore"
        };
    }

    private static MeshInstance3D CreateProceduralResourceFallback(
        GameResourceDefinition definition,
        string family)
    {
        MeshInstance3D root = new()
        {
            Name = "MeshInstance3D",
            Mesh = family switch
            {
                "crystal" => CrystalShard(0.34f, 1.28f),
                "fiber" => FiberStem(0.20f, 1.12f),
                "organic" => OrganicLobe(0.58f, 0.92f),
                _ => OreRock(0.64f, 1.02f)
            },
            Rotation = new Vector3(0.0f, 0.18f, 0.08f)
        };

        switch (family)
        {
            case "crystal":
                AddShard(root, "ShardA", new Vector3(-0.34f, -0.08f, 0.08f), 0.22f, 0.92f, -0.24f, 0.14f);
                AddShard(root, "ShardB", new Vector3(0.32f, -0.10f, 0.18f), 0.18f, 0.76f, 0.28f, -0.18f);
                AddShard(root, "ShardC", new Vector3(0.04f, -0.14f, -0.34f), 0.16f, 0.62f, -0.18f, -0.31f);
                break;
            case "fiber":
                AddStem(root, "StemA", new Vector3(-0.25f, -0.12f, 0.08f), 0.12f, 0.88f, -0.22f);
                AddStem(root, "StemB", new Vector3(0.26f, -0.14f, -0.10f), 0.10f, 0.78f, 0.26f);
                AddStem(root, "StemC", new Vector3(0.04f, -0.18f, 0.28f), 0.09f, 0.66f, -0.10f);
                break;
            case "organic":
                AddLobe(root, "LobeA", new Vector3(-0.28f, -0.08f, 0.16f), new Vector3(0.72f, 0.48f, 0.86f));
                AddLobe(root, "LobeB", new Vector3(0.30f, -0.12f, -0.12f), new Vector3(0.66f, 0.42f, 0.72f));
                AddLobe(root, "LobeC", new Vector3(0.08f, -0.18f, 0.34f), new Vector3(0.50f, 0.34f, 0.60f));
                break;
            default:
                AddRock(root, "RockA", new Vector3(-0.34f, -0.14f, 0.16f), new Vector3(0.60f, 0.48f, 0.72f), -0.28f);
                AddRock(root, "RockB", new Vector3(0.34f, -0.18f, -0.14f), new Vector3(0.54f, 0.42f, 0.62f), 0.34f);
                AddRock(root, "RockC", new Vector3(0.04f, -0.22f, 0.34f), new Vector3(0.42f, 0.34f, 0.50f), -0.12f);
                break;
        }

        root.SetMeta("surface_visual_family", family);
        root.SetMeta("surface_visual_parts", 4);
        return root;
    }

    public static string ResolveResourceFamily(GameResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Tags.Contains("crystal", StringComparer.Ordinal) ||
            definition.Tags.Contains("exotic", StringComparer.Ordinal))
        {
            return "crystal";
        }
        if (definition.Tags.Contains("fiber", StringComparer.Ordinal) ||
            definition.Tags.Contains("filament", StringComparer.Ordinal) ||
            definition.Tags.Contains("gas", StringComparer.Ordinal))
        {
            return "fiber";
        }
        if (definition.Tags.Contains("bio", StringComparer.Ordinal) ||
            definition.Tags.Contains("gel", StringComparer.Ordinal) ||
            definition.Tags.Contains("resin", StringComparer.Ordinal) ||
            definition.Tags.Contains("sludge", StringComparer.Ordinal) ||
            definition.Tags.Contains("brine", StringComparer.Ordinal) ||
            definition.Tags.Contains("hydrocarbon", StringComparer.Ordinal))
        {
            return "organic";
        }
        return "ore";
    }

    public static StandardMaterial3D BuildResourceMaterial(
        ResourceVisualDefinition visual,
        float brightness = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(visual);
        Color albedo = new(
            (float)Math.Clamp(visual.AlbedoR * brightness, 0.0, 1.0),
            (float)Math.Clamp(visual.AlbedoG * brightness, 0.0, 1.0),
            (float)Math.Clamp(visual.AlbedoB * brightness, 0.0, 1.0));
        return new StandardMaterial3D
        {
            AlbedoColor = albedo,
            EmissionEnabled = visual.EmissionEnergy > 0.001,
            Emission = new Color(
                (float)visual.EmissionR,
                (float)visual.EmissionG,
                (float)visual.EmissionB),
            EmissionEnergyMultiplier = (float)Math.Min(visual.EmissionEnergy, 0.75),
            Metallic = (float)visual.Metallic,
            Roughness = (float)Math.Clamp(visual.Roughness, 0.24, 0.96)
        };
    }

    private static SphereMesh OreRock(float radius, float height) => new()
    {
        Radius = radius,
        Height = height,
        RadialSegments = 8,
        Rings = 4
    };

    private static SphereMesh OrganicLobe(float radius, float height) => new()
    {
        Radius = radius,
        Height = height,
        RadialSegments = 12,
        Rings = 6
    };

    private static CylinderMesh CrystalShard(float radius, float height) => new()
    {
        TopRadius = 0.03f,
        BottomRadius = radius,
        Height = height,
        RadialSegments = 6,
        Rings = 2
    };

    private static CylinderMesh FiberStem(float radius, float height) => new()
    {
        TopRadius = radius * 0.55f,
        BottomRadius = radius,
        Height = height,
        RadialSegments = 7,
        Rings = 2
    };

    private static void AddShard(Node3D root, string name, Vector3 position,
        float radius, float height, float yaw, float pitch) =>
        root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = CrystalShard(radius, height),
            Position = position,
            Rotation = new Vector3(pitch, yaw, 0.0f)
        });

    private static void AddStem(Node3D root, string name, Vector3 position,
        float radius, float height, float tilt) =>
        root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = FiberStem(radius, height),
            Position = position,
            Rotation = new Vector3(tilt, 0.0f, -tilt * 0.6f)
        });

    private static void AddLobe(Node3D root, string name, Vector3 position,
        Vector3 scale) =>
        root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = OrganicLobe(0.52f, 0.82f),
            Position = position,
            Scale = scale
        });

    private static void AddRock(Node3D root, string name, Vector3 position,
        Vector3 scale, float yaw) =>
        root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = OreRock(0.58f, 0.92f),
            Position = position,
            Rotation = new Vector3(0.08f, yaw, -0.06f),
            Scale = scale
        });
}
