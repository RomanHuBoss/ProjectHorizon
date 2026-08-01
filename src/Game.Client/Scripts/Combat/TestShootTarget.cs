using Godot;

public partial class TestShootTarget : StaticBody3D, IHitscanTarget
{
    [Export(PropertyHint.Range, "0.05,1.0,0.05")]
    public float HitFlashSeconds { get; set; } = 0.2f;

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _idleMaterial = null!;
    private StandardMaterial3D _hitMaterial = null!;
    private double _flashRemaining;
    private int _hitCount;

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");

        _idleMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.58f, 0.12f),
            Roughness = 0.55f
        };

        _hitMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.08f, 0.08f),
            EmissionEnabled = true,
            Emission = new Color(0.65f, 0.01f, 0.01f),
            Roughness = 0.35f
        };

        _mesh.MaterialOverride = _idleMaterial;
        SetProcess(false);
    }

    public void ReceiveHit(Node3D source, Vector3 position, Vector3 normal)
    {
        _hitCount++;
        _flashRemaining = HitFlashSeconds;
        _mesh.MaterialOverride = _hitMaterial;
        SetProcess(true);

        GD.Print(
            $"ShootTarget: hit #{_hitCount}; " +
            $"source={source.Name}; position={position}; normal={normal}");
    }

    public override void _Process(double delta)
    {
        _flashRemaining -= delta;

        if (_flashRemaining > 0.0)
        {
            return;
        }

        _flashRemaining = 0.0;
        _mesh.MaterialOverride = _idleMaterial;
        SetProcess(false);
    }
}
