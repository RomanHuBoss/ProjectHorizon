using Godot;

public partial class ProductionModelLodController : Node3D
{
    [Export(PropertyHint.Range, "1,100000,1")]
    public float Lod1DistanceMeters { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "2,500000,1")]
    public float Lod2DistanceMeters { get; set; } = 500.0f;

    [Export]
    public bool ForceLod0 { get; set; }

    public int ActiveLod { get; private set; }
    public int LodCount => 3;

    private Node3D? _lod0;
    private Node3D? _lod1;
    private Node3D? _lod2;

    public override void _Ready()
    {
        _lod0 = GetNodeOrNull<Node3D>("LOD0");
        _lod1 = GetNodeOrNull<Node3D>("LOD1");
        _lod2 = GetNodeOrNull<Node3D>("LOD2");
        SetMeta("production_lod_chain", "LOD0/LOD1/LOD2");
        ApplyLod(0);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (ForceLod0)
        {
            ApplyLod(0);
            return;
        }

        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera is null)
        {
            ApplyLod(0);
            return;
        }

        float distance = GlobalPosition.DistanceTo(camera.GlobalPosition);
        int target = distance >= Lod2DistanceMeters
            ? 2
            : distance >= Lod1DistanceMeters ? 1 : 0;
        ApplyLod(target);
    }

    private void ApplyLod(int lod)
    {
        ActiveLod = Mathf.Clamp(lod, 0, 2);
        if (_lod0 is not null) _lod0.Visible = ActiveLod == 0;
        if (_lod1 is not null) _lod1.Visible = ActiveLod == 1;
        if (_lod2 is not null) _lod2.Visible = ActiveLod == 2;
    }
}
