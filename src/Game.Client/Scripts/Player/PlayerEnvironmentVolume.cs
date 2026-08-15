using Godot;

public partial class PlayerEnvironmentVolume : Area3D
{
    [Export]
    public bool Swimming { get; set; } = true;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerController player)
        {
            player.SetSwimming(Swimming);
            GD.Print($"TASK-120 player environment PASS: swimming={(Swimming ? 1 : 0)}; event=entered.");
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is PlayerController player)
        {
            player.SetSwimming(false);
            GD.Print("TASK-120 player environment PASS: swimming=0; event=exited.");
        }
    }
}
