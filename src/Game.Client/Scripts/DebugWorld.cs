using Godot;

public partial class DebugWorld : Node3D
{
    public override void _Ready()
    {
        GD.Print("Project Horizon: DebugWorld успешно запущен.");
        GD.Print($"Rendering method: {RenderingServer.GetCurrentRenderingMethod()}");
        GD.Print($"Rendering driver: {RenderingServer.GetCurrentRenderingDriverName()}");
    }
}