using System;
using Godot;

public partial class PortableCraftingStation : StaticBody3D, IInteractable
{
    [Export]
    public string StationId { get; set; } = "station.unassigned";

    [Export]
    public string RecipeId { get; set; } = "recipe.unassigned";

    private MeshInstance3D? _mesh;
    private bool _crafted;

    public bool IsCrafted => _crafted;

    public override void _Ready()
    {
        if (!GameContentCatalog.IsStableId(StationId) ||
            string.Equals(StationId, "station.unassigned", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Crafting station {Name} has no valid StationId.");
        }

        if (!GameContentCatalog.IsStableId(RecipeId) ||
            string.Equals(RecipeId, "recipe.unassigned", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Crafting station {Name} has no valid RecipeId.");
        }

        _mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (_mesh is null)
        {
            throw new InvalidOperationException(
                $"Crafting station {Name} is missing MeshInstance3D.");
        }

        ApplyState();
    }

    public void Interact(Node3D interactor)
    {
        if (GetTree().CurrentScene is not SalvageRepairSlice slice)
        {
            GD.PushError($"Crafting station {Name} has no vertical-slice controller.");
            return;
        }

        slice.TryCraftAtStation(this, RecipeId, StationId, interactor);
    }

    public void SetCrafted(bool crafted)
    {
        _crafted = crafted;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_mesh is null)
        {
            return;
        }

        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = _crafted
                ? new Color(0.10f, 0.62f, 0.30f)
                : new Color(0.50f, 0.22f, 0.72f),
            EmissionEnabled = true,
            Emission = _crafted
                ? new Color(0.02f, 0.30f, 0.08f)
                : new Color(0.18f, 0.04f, 0.34f),
            EmissionEnergyMultiplier = 1.5f,
            Metallic = 0.48f,
            Roughness = 0.34f
        };
    }
}
