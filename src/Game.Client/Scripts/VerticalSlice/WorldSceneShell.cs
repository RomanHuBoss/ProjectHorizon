using Godot;

public partial class WorldSceneShell : Node3D
{
    [Export]
    public WorldSceneKind Kind { get; set; } = WorldSceneKind.Surface;

    [Export]
    public string EnvironmentProfile { get; set; } = "surface";
}
