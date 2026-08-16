using System;
using Godot;

public partial class BaseConstructionModuleNode : StaticBody3D
{
    public string InstanceId { get; private set; } = string.Empty;

    public string ModuleId { get; private set; } = string.Empty;

    public int GridX { get; private set; }

    public int GridZ { get; private set; }

    public void Configure(
        BaseModuleDefinition definition,
        BaseModulePlacement placement,
        double gridSizeMeters,
        double surfaceHeight = 0.11)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(placement);
        if (gridSizeMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSizeMeters));
        }

        InstanceId = placement.InstanceId;
        ModuleId = placement.ModuleId;
        GridX = placement.GridX;
        GridZ = placement.GridZ;
        Name = ToNodeName(placement.InstanceId);
        Position = new Vector3(
            (float)(placement.GridX * gridSizeMeters),
            (float)(definition.Size.Y * 0.5 + surfaceHeight),
            (float)(placement.GridZ * gridSizeMeters));
        Rotation = new Vector3(
            0.0f,
            placement.RotationQuarterTurns * Mathf.Pi * 0.5f,
            0.0f);
        CollisionLayer = 1u;
        CollisionMask = 0u;
        AddToGroup("base_construction_module");

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                (float)definition.Color.R,
                (float)definition.Color.G,
                (float)definition.Color.B,
                placement.Enabled ? 1.0f : 0.58f),
            Metallic = 0.25f,
            Roughness = 0.62f,
            Transparency = placement.Enabled
                ? BaseMaterial3D.TransparencyEnum.Disabled
                : BaseMaterial3D.TransparencyEnum.Alpha
        };
        MeshInstance3D meshInstance = new()
        {
            Name = "Mesh"
        };
        CollisionShape3D collisionShape = new()
        {
            Name = "Collision"
        };
        if (string.Equals(definition.Shape, "Cylinder", StringComparison.Ordinal))
        {
            float radius = (float)(Math.Max(
                definition.Size.X,
                definition.Size.Z) * 0.5);
            meshInstance.Mesh = new CylinderMesh
            {
                Material = material,
                TopRadius = radius * 0.92f,
                BottomRadius = radius,
                Height = (float)definition.Size.Y,
                RadialSegments = 16
            };
            collisionShape.Shape = new CylinderShape3D
            {
                Radius = radius,
                Height = (float)definition.Size.Y
            };
        }
        else
        {
            Vector3 size = new(
                (float)definition.Size.X,
                (float)definition.Size.Y,
                (float)definition.Size.Z);
            meshInstance.Mesh = new BoxMesh
            {
                Material = material,
                Size = size
            };
            collisionShape.Shape = new BoxShape3D
            {
                Size = size
            };
        }

        AddChild(meshInstance);
        AddChild(collisionShape);
        for (int index = 0; index < definition.DynamicLights; index++)
        {
            OmniLight3D light = new()
            {
                Name = $"Light{index + 1}",
                Position = new Vector3(
                    definition.DynamicLights == 1
                        ? 0.0f
                        : (index == 0 ? -0.35f : 0.35f),
                    (float)(definition.Size.Y * 0.42),
                    0.0f),
                LightColor = new Color(
                    Math.Min(1.0f, (float)definition.Color.R + 0.28f),
                    Math.Min(1.0f, (float)definition.Color.G + 0.28f),
                    Math.Min(1.0f, (float)definition.Color.B + 0.28f)),
                LightEnergy = placement.Enabled ? 0.72f : 0.0f,
                OmniRange = 4.0f,
                ShadowEnabled = false
            };
            AddChild(light);
        }
    }

    private static string ToNodeName(string instanceId)
    {
        return instanceId.Replace('.', '_');
    }
}
