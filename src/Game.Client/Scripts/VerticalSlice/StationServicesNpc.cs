using System;
using Godot;

public partial class StationServicesNpc : StaticBody3D, IInteractable
{
    [Export]
    public string NpcId { get; set; } = "npc.unassigned";

    private MeshInstance3D? _mesh;

    public override void _Ready()
    {
        if (!GameContentCatalog.IsStableId(NpcId) ||
            string.Equals(NpcId, "npc.unassigned", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Station services NPC {Name} has no valid NpcId.");
        }

        _mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (_mesh is null)
        {
            throw new InvalidOperationException(
                $"Station services NPC {Name} is missing MeshInstance3D.");
        }

        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.34f, 0.62f),
            EmissionEnabled = true,
            Emission = new Color(0.02f, 0.14f, 0.36f),
            EmissionEnergyMultiplier = 1.7f,
            Metallic = 0.28f,
            Roughness = 0.42f
        };
    }

    public void Interact(Node3D interactor)
    {
        if (GetTree().CurrentScene is not SalvageRepairSlice slice)
        {
            GD.PushError($"Station services NPC {Name} has no controller.");
            return;
        }

        slice.OpenStationServices(this, interactor);
    }
}
