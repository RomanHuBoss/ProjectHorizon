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
    private bool _runtimeSuppressed;

    public bool IsCollected => _collected;

    public bool RuntimeSuppressed => _runtimeSuppressed;

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
        if (_definition is not null)
        {
            ApplyDefinitionMaterial(_definition);
        }
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

        if (_mesh is not null)
        {
            ApplyDefinitionMaterial(definition);
        }
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

    public void SetRuntimeSuppressed(bool suppressed)
    {
        _runtimeSuppressed = suppressed;
        ApplyCollectedState();
    }

    private void ApplyDefinitionMaterial(GameResourceDefinition definition)
    {
        if (_mesh is null)
        {
            return;
        }

        ResourceVisualDefinition visual = definition.Visual;
        ApplyMaterialRecursive(_mesh, visual, 1.0f);
    }

    private static void ApplyMaterialRecursive(
        MeshInstance3D mesh,
        ResourceVisualDefinition visual,
        float brightness)
    {
        mesh.MaterialOverride =
            ProceduralSurfaceVisualFactory.BuildResourceMaterial(
                visual,
                brightness);
        int detailIndex = 0;
        foreach (Node child in mesh.GetChildren())
        {
            if (child is not MeshInstance3D detail)
            {
                continue;
            }
            float detailBrightness = detailIndex % 2 == 0 ? 1.12f : 0.84f;
            ApplyMaterialRecursive(detail, visual, detailBrightness);
            detailIndex++;
        }
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
        bool unavailable = _collected || _runtimeSuppressed;
        if (_mesh is not null)
        {
            _mesh.Visible = !unavailable;
        }

        CollisionLayer = unavailable ? 0u : 1u;
        CollisionMask = unavailable ? 0u : 1u;
        _collisionShape?.SetDeferred("disabled", unavailable);
    }
}
