using System;
using Godot;

public partial class EcologyFloraSpecimenNode : StaticBody3D, IInteractable, IHitscanTarget
{
    private EcologyFloraDefinition? _definition;
    private int _hits;

    public string InstanceId { get; private set; } = string.Empty;

    public string FloraId => _definition?.FloraId ?? string.Empty;

    public event Action<EcologyFloraSpecimenNode, Node3D>? HarvestRequested;
    public event Action<EcologyFloraSpecimenNode, Node3D>? Damaged;

    public void Configure(
        EcologyFloraDefinition definition,
        EcologyFloraPlacement placement,
        bool renderMesh = true)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(placement);
        _definition = definition;
        InstanceId = placement.InstanceId;
        Name = placement.InstanceId.Replace('.', '_');
        Position = new Vector3(
            (float)placement.PositionX,
            (float)placement.PositionY + 0.55f,
            (float)placement.PositionZ);
        Rotation = new Vector3(
            0.0f,
            Mathf.DegToRad((float)placement.RotationDegrees),
            0.0f);
        float scale = (float)placement.Scale;
        CollisionLayer = 1u;
        CollisionMask = 0u;
        AddToGroup("interactable");
        AddToGroup("ecology_flora");

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                (float)definition.ColorR,
                (float)definition.ColorG,
                (float)definition.ColorB,
                1.0f),
            Roughness = 0.88f
        };
        if (renderMesh)
        {
            MeshInstance3D mesh = new()
            {
                Name = "SpecimenMesh",
                Mesh = CreateMesh(definition, material),
                Scale = Vector3.One * scale
            };
            AddChild(mesh);
        }
        AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new CylinderShape3D
            {
                Radius = Math.Clamp(0.32f * scale, 0.22f, 0.72f),
                Height = Math.Clamp(1.1f * scale, 0.7f, 2.8f)
            }
        });
    }

    public void Interact(Node3D interactor)
    {
        HarvestRequested?.Invoke(this, interactor);
    }

    public void ReceiveHit(
        Node3D source,
        Vector3 position,
        Vector3 normal)
    {
        _hits++;
        Damaged?.Invoke(this, source);
        if (_hits >= 2)
        {
            HarvestRequested?.Invoke(this, source);
        }
        else
        {
            GD.Print(
                "TASK-116 flora damage PASS: " +
                $"instance={InstanceId}; species={FloraId}; hits={_hits}/2.");
        }
    }

    public static PrimitiveMesh CreateMesh(
        EcologyFloraDefinition definition,
        Material material) => CreateLodMesh(definition, material, lod: 0);

    public static PrimitiveMesh CreateLodMesh(
        EcologyFloraDefinition definition,
        Material material,
        int lod)
    {
        bool simplified = lod > 0;
        PrimitiveMesh mesh = definition.Shape switch
        {
            "Canopy" => new SphereMesh
            {
                Radius = 0.55f,
                Height = 1.10f,
                RadialSegments = simplified ? 6 : 10,
                Rings = simplified ? 3 : 5
            },
            "Pad" => new CylinderMesh
            {
                TopRadius = 0.52f,
                BottomRadius = 0.44f,
                Height = 0.22f,
                RadialSegments = simplified ? 7 : 14,
                Rings = simplified ? 1 : 2
            },
            "Fungus" => new CylinderMesh
            {
                TopRadius = 0.50f,
                BottomRadius = 0.16f,
                Height = 0.72f,
                RadialSegments = simplified ? 6 : 12,
                Rings = simplified ? 1 : 3
            },
            "Tuft" => new CylinderMesh
            {
                TopRadius = 0.12f,
                BottomRadius = 0.42f,
                Height = 1.15f,
                RadialSegments = simplified ? 4 : 7,
                Rings = 1
            },
            "Frond" => new CylinderMesh
            {
                TopRadius = 0.34f,
                BottomRadius = 0.16f,
                Height = 1.35f,
                RadialSegments = simplified ? 4 : 6,
                Rings = 1
            },
            _ => new CylinderMesh
            {
                TopRadius = 0.10f,
                BottomRadius = 0.30f,
                Height = 1.55f,
                RadialSegments = simplified ? 4 : 7,
                Rings = 1
            }
        };
        mesh.Material = material;
        return mesh;
    }
}
