using System;
using Godot;

public partial class StarterShipRepairTerminal : StaticBody3D, IInteractable
{
    private MeshInstance3D? _mesh;
    private bool _repaired;

    public bool IsRepaired => _repaired;

    public override void _Ready()
    {
        _mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (_mesh is null)
        {
            throw new InvalidOperationException(
                "Starter ship is missing MeshInstance3D.");
        }

        ApplyRepairState();
    }

    public void Interact(Node3D interactor)
    {
        if (GetTree().CurrentScene is not SalvageRepairSlice slice)
        {
            GD.PushError("Starter ship has no vertical-slice controller.");
            return;
        }

        slice.TryRepairShip(interactor);
    }

    public void SetRepaired(bool repaired)
    {
        _repaired = repaired;
        ApplyRepairState();
    }

    private void ApplyRepairState()
    {
        if (_mesh is null)
        {
            return;
        }

        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = _repaired
                ? new Color(0.12f, 0.58f, 0.24f)
                : new Color(0.64f, 0.12f, 0.09f),
            EmissionEnabled = true,
            Emission = _repaired
                ? new Color(0.02f, 0.22f, 0.05f)
                : new Color(0.24f, 0.02f, 0.01f),
            EmissionEnergyMultiplier = 1.3f,
            Metallic = 0.35f,
            Roughness = 0.48f
        };
    }
}
