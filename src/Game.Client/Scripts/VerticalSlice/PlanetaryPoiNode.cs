using System;
using Godot;

public partial class PlanetaryPoiNode : StaticBody3D, IInteractable
{
    private PlanetaryPoiDefinition? _definition;
    private MeshInstance3D? _meshInstance;
    private StandardMaterial3D? _material;
    private CollisionShape3D? _collisionShape;

    public string InstanceId { get; private set; } = string.Empty;

    public string PoiTypeId { get; private set; } = string.Empty;

    public double ScanRange => _definition?.ScanRange ?? 0.0;

    public void Configure(
        PlanetaryPoiDefinition definition,
        PlanetaryPoiPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(placement);
        if (!string.Equals(
            definition.PoiTypeId,
            placement.PoiTypeId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "POI definition does not match placement type.");
        }

        _definition = definition;
        InstanceId = placement.InstanceId;
        PoiTypeId = placement.PoiTypeId;
        Name = placement.InstanceId.Replace('.', '_');
        Position = new Vector3(
            (float)placement.PositionX,
            (float)placement.PositionY,
            (float)placement.PositionZ);
        Rotation = new Vector3(
            0.0f,
            Mathf.DegToRad((float)placement.RotationDegrees),
            0.0f);
        CollisionLayer = 1u;
        CollisionMask = 0u;
        AddToGroup("interactable");
        AddToGroup("planetary_poi");

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                (float)definition.Color.R * 0.34f,
                (float)definition.Color.G * 0.34f,
                (float)definition.Color.B * 0.34f),
            EmissionEnabled = true,
            Emission = new Color(
                (float)definition.Color.R * 0.08f,
                (float)definition.Color.G * 0.08f,
                (float)definition.Color.B * 0.08f),
            EmissionEnergyMultiplier = 0.6f,
            Metallic = 0.22f,
            Roughness = 0.72f
        };
        MeshInstance3D meshInstance = new()
        {
            Name = "Mesh"
        };
        _material = material;
        _meshInstance = meshInstance;
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
                TopRadius = radius * 0.88f,
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

        _collisionShape = collisionShape;
        AddChild(meshInstance);
        AddChild(collisionShape);
        OmniLight3D marker = new()
        {
            Name = "DiscoveryMarker",
            Position = new Vector3(
                0.0f,
                (float)(definition.Size.Y * 0.55 + 0.5),
                0.0f),
            LightColor = new Color(
                (float)definition.Color.R,
                (float)definition.Color.G,
                (float)definition.Color.B),
            LightEnergy = 0.35f,
            OmniRange = 3.0f,
            ShadowEnabled = false
        };
        AddChild(marker);
    }

    public void ApplyState(bool discovered, bool resolved)
    {
        if (_definition is not { } definition ||
            _material is not { } material)
        {
            return;
        }

        float multiplier = resolved ? 0.48f : discovered ? 0.92f : 0.34f;
        material.AlbedoColor = new Color(
            (float)definition.Color.R * multiplier,
            (float)definition.Color.G * multiplier,
            (float)definition.Color.B * multiplier,
            resolved ? 0.72f : 1.0f);
        material.Emission = new Color(
            (float)definition.Color.R * (discovered ? 0.32f : 0.08f),
            (float)definition.Color.G * (discovered ? 0.32f : 0.08f),
            (float)definition.Color.B * (discovered ? 0.32f : 0.08f));
        material.EmissionEnergyMultiplier = resolved
            ? 0.35f
            : discovered ? 1.45f : 0.6f;
        material.Transparency = resolved
            ? BaseMaterial3D.TransparencyEnum.Alpha
            : BaseMaterial3D.TransparencyEnum.Disabled;
        if (GetNodeOrNull<OmniLight3D>("DiscoveryMarker") is { } marker)
        {
            marker.LightEnergy = resolved ? 0.1f : discovered ? 0.85f : 0.35f;
        }
    }

    public void SetRuntimeResident(bool resident)
    {
        Visible = resident;
        CollisionLayer = resident ? 1u : 0u;
        _collisionShape?.SetDeferred("disabled", !resident);
    }

    public void Interact(Node3D interactor)
    {
        if (GetTree().CurrentScene is not SalvageRepairSlice slice)
        {
            GD.PushError($"Planetary POI {InstanceId} has no controller.");
            return;
        }

        slice.TryInteractPlanetaryPoi(this, interactor);
    }
}
