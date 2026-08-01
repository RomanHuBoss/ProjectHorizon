using Godot;

public partial class TestInteractable : StaticBody3D, IInteractable
{
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _inactiveMaterial = null!;
    private StandardMaterial3D _activeMaterial = null!;
    private bool _isActive;

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");

        _inactiveMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.32f, 0.78f),
            Roughness = 0.65f
        };

        _activeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.78f, 0.28f),
            Roughness = 0.45f
        };

        ApplyVisualState();
    }

    public void Interact(Node3D interactor)
    {
        _isActive = !_isActive;
        ApplyVisualState();

        GD.Print(
            $"InteractionTerminal: {(_isActive ? "active" : "inactive")}; " +
            $"interactor={interactor.Name}");
    }

    private void ApplyVisualState()
    {
        _mesh.MaterialOverride = _isActive
            ? _activeMaterial
            : _inactiveMaterial;
    }
}
