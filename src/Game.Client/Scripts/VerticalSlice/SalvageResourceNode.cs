using System;
using Godot;

public partial class SalvageResourceNode : StaticBody3D, IInteractable
{
    [Export]
    public string ResourceNodeId { get; set; } = "salvage.unassigned";

    [Export(PropertyHint.Range, "1,10,1")]
    public int Quantity { get; set; } = 1;

    private MeshInstance3D? _mesh;
    private CollisionShape3D? _collisionShape;
    private bool _collected;

    public bool IsCollected => _collected;

    public override void _Ready()
    {
        if (string.IsNullOrWhiteSpace(ResourceNodeId) ||
            string.Equals(
                ResourceNodeId,
                "salvage.unassigned",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Resource node {Name} has no serialized ResourceNodeId. " +
                "The C# export name must be written exactly as " +
                "ResourceNodeId in the .tscn file.");
        }

        _mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        _collisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (_mesh is null || _collisionShape is null)
        {
            throw new InvalidOperationException(
                $"Resource node {Name} is missing mesh or collision.");
        }

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(0.12f, 0.82f, 0.86f),
            EmissionEnabled = true,
            Emission = new Color(0.04f, 0.38f, 0.42f),
            EmissionEnergyMultiplier = 1.8f,
            Metallic = 0.55f,
            Roughness = 0.28f
        };
        _mesh.MaterialOverride = material;
        ApplyCollectedState();
    }

    public void Interact(Node3D interactor)
    {
        if (_collected)
        {
            return;
        }

        if (GetTree().CurrentScene is not SalvageRepairSlice slice)
        {
            GD.PushError(
                $"Vertical slice resource {ResourceNodeId} has no controller.");
            return;
        }

        if (slice.TryCollectResource(
            this,
            ResourceNodeId,
            Quantity,
            interactor))
        {
            SetCollected(true);
        }
    }

    public void SetCollected(bool collected)
    {
        _collected = collected;
        ApplyCollectedState();
    }

    private void ApplyCollectedState()
    {
        if (_mesh is not null)
        {
            _mesh.Visible = !_collected;
        }

        CollisionLayer = _collected ? 0u : 1u;
        CollisionMask = _collected ? 0u : 1u;
        _collisionShape?.SetDeferred("disabled", _collected);
    }
}
