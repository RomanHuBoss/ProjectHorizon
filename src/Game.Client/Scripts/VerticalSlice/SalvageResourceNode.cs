using System;
using Godot;

public partial class SalvageResourceNode : StaticBody3D, IInteractable
{
    [Export]
    public string ResourceNodeId { get; set; } = "salvage.unassigned";

    [Export]
    public string ResourceDefinitionId { get; set; } = "resource.unassigned";

    private MeshInstance3D? _mesh;
    private CollisionShape3D? _collisionShape;
    private GameResourceDefinition? _definition;
    private bool _collected;

    public bool IsCollected => _collected;

    public int Quantity => _definition?.GetDeterministicYield() ?? 0;

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

        if (!GameContentCatalog.IsStableId(ResourceDefinitionId) ||
            string.Equals(
                ResourceDefinitionId,
                "resource.unassigned",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Resource node {Name} has no valid ResourceDefinitionId.");
        }

        _mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        _collisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (_mesh is null || _collisionShape is null)
        {
            throw new InvalidOperationException(
                $"Resource node {Name} is missing mesh or collision.");
        }

        ApplyFallbackMaterial();
        ApplyCollectedState();
    }

    public void ConfigureDefinition(GameResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(
            ResourceDefinitionId,
            definition.ResourceId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Resource node {Name} requests {ResourceDefinitionId}, " +
                $"but received {definition.ResourceId}.");
        }

        _definition = definition;
        int quantity = definition.GetDeterministicYield();
        if (quantity <= 0)
        {
            throw new InvalidOperationException(
                $"Resource node {Name} resolved invalid quantity {quantity}.");
        }

        if (_mesh is null)
        {
            throw new InvalidOperationException(
                $"Resource node {Name} was configured before _Ready.");
        }

        ResourceVisualDefinition visual = definition.Visual;
        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(
                (float)visual.AlbedoR,
                (float)visual.AlbedoG,
                (float)visual.AlbedoB),
            EmissionEnabled = true,
            Emission = new Color(
                (float)visual.EmissionR,
                (float)visual.EmissionG,
                (float)visual.EmissionB),
            EmissionEnergyMultiplier = (float)visual.EmissionEnergy,
            Metallic = (float)visual.Metallic,
            Roughness = (float)visual.Roughness
        };
    }

    public void Interact(Node3D interactor)
    {
        if (_collected)
        {
            return;
        }

        if (_definition is null)
        {
            GD.PushError(
                $"Vertical slice resource {ResourceNodeId} has no content definition.");
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
            _definition.ItemDefinitionId,
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

    private void ApplyFallbackMaterial()
    {
        if (_mesh is null)
        {
            return;
        }

        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.26f, 0.30f),
            EmissionEnabled = false,
            Metallic = 0.2f,
            Roughness = 0.7f
        };
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
