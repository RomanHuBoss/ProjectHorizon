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
            UpgradeProductionVisual(definition);
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
        int detailIndex = 0;
        ApplyMaterialRecursive(_mesh, visual, ref detailIndex);
    }

    private static void ApplyMaterialRecursive(
        Node node,
        ResourceVisualDefinition visual,
        ref int detailIndex)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is not null)
        {
            string role = mesh.Name.ToString().ToLowerInvariant();
            int brightnessBand = detailIndex % 3;
            float brightness = brightnessBand switch
            {
                0 => 1.00f,
                1 => 1.08f,
                _ => 0.92f
            };
            float metallicScale = 1.0f;
            float roughnessOffset = 0.0f;
            float emissionScale = 0.55f;

            if (role.Contains("core", StringComparison.Ordinal) ||
                role.Contains("vein", StringComparison.Ordinal) ||
                role.Contains("throat", StringComparison.Ordinal) ||
                role.Contains("accent", StringComparison.Ordinal))
            {
                brightness *= 1.28f;
                metallicScale = 1.10f;
                roughnessOffset = -0.14f;
                emissionScale = 1.25f;
            }
            else if (role.Contains("matrix", StringComparison.Ordinal) ||
                     role.Contains("bed", StringComparison.Ordinal) ||
                     role.Contains("shelf", StringComparison.Ordinal) ||
                     role.Contains("mass", StringComparison.Ordinal))
            {
                brightness *= 0.62f;
                metallicScale = 0.60f;
                roughnessOffset = 0.20f;
                emissionScale = 0.08f;
            }
            else if (role.Contains("crystal", StringComparison.Ordinal) ||
                     role.Contains("spire", StringComparison.Ordinal) ||
                     role.Contains("blade", StringComparison.Ordinal) ||
                     role.Contains("shard", StringComparison.Ordinal))
            {
                brightness *= 1.12f;
                metallicScale = 0.72f;
                roughnessOffset = -0.10f;
                emissionScale = 0.90f;
            }
            else if (role.Contains("scrap", StringComparison.Ordinal) ||
                     role.Contains("coupler", StringComparison.Ordinal) ||
                     role.Contains("beam", StringComparison.Ordinal))
            {
                metallicScale = 1.35f;
                roughnessOffset = 0.05f;
                emissionScale = 0.18f;
            }

            mesh.MaterialOverride =
                ProceduralSurfaceVisualFactory.BuildResourceMaterial(
                    visual,
                    brightness,
                    metallicScale,
                    roughnessOffset,
                    emissionScale);
            detailIndex++;
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyMaterialRecursive(child, visual, ref detailIndex);
        }
    }

    private void UpgradeProductionVisual(GameResourceDefinition definition)
    {
        if (_mesh is null ||
            _mesh.HasMeta("production_resource_visual") &&
            _mesh.GetMeta("production_resource_visual").AsBool())
        {
            return;
        }

        MeshInstance3D production =
            ProceduralSurfaceVisualFactory.CreateResourceVisual(definition);
        if (!production.HasMeta("production_resource_visual") ||
            !production.GetMeta("production_resource_visual").AsBool())
        {
            return;
        }

        RemoveChild(_mesh);
        _mesh.QueueFree();
        AddChild(production);
        _mesh = production;
        GD.Print(
            $"TASK-216 resource visual upgraded: node={ResourceNodeId}; " +
            $"asset={production.GetMeta("surface_visual_asset")}; lod=3; collision=unchanged.");
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
