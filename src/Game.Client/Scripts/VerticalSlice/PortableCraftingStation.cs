using System;
using Godot;

public partial class PortableCraftingStation : StaticBody3D, IInteractable
{
    [Export]
    public string StationId { get; set; } = "station.unassigned";

    private MeshInstance3D? _mesh;
    private bool _crafted;
    private bool _crafting;

    public bool IsCrafted => _crafted;

    public bool IsCrafting => _crafting;

    public override void _Ready()
    {
        if (!GameContentCatalog.IsStableId(StationId) ||
            string.Equals(StationId, "station.unassigned", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Crafting station {Name} has no valid StationId.");
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

        slice.OpenRecipeSelector(this, interactor);
    }

    public void SetCrafted(bool crafted)
    {
        _crafted = crafted;
        if (crafted)
        {
            _crafting = false;
        }

        ApplyState();
    }

    public void SetCrafting(bool crafting)
    {
        _crafting = crafting && !_crafted;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_mesh is null)
        {
            return;
        }

        (Color idleAlbedo, Color idleEmission) = GetIdleColors();
        Color albedo = _crafted
            ? new Color(0.10f, 0.62f, 0.30f)
            : _crafting
                ? new Color(0.86f, 0.48f, 0.08f)
                : idleAlbedo;
        Color emission = _crafted
            ? new Color(0.02f, 0.30f, 0.08f)
            : _crafting
                ? new Color(0.42f, 0.16f, 0.01f)
                : idleEmission;
        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            EmissionEnabled = true,
            Emission = emission,
            EmissionEnergyMultiplier = _crafting ? 2.2f : 1.5f,
            Metallic = 0.48f,
            Roughness = 0.34f
        };
    }
    private (Color Albedo, Color Emission) GetIdleColors()
    {
        return StationId switch
        {
            "station.smelter" =>
                (new Color(0.64f, 0.22f, 0.08f),
                 new Color(0.34f, 0.06f, 0.01f)),
            "station.refinery" =>
                (new Color(0.10f, 0.36f, 0.62f),
                 new Color(0.02f, 0.14f, 0.34f)),
            "station.distillation_column" =>
                (new Color(0.08f, 0.55f, 0.58f),
                 new Color(0.01f, 0.27f, 0.29f)),
            "station.chemical_processor" =>
                (new Color(0.32f, 0.58f, 0.12f),
                 new Color(0.12f, 0.30f, 0.02f)),
            _ =>
                (new Color(0.50f, 0.22f, 0.72f),
                 new Color(0.18f, 0.04f, 0.34f))
        };
    }

}
