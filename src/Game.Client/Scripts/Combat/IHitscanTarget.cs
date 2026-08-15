using Godot;

/// <summary>Scene-local contract for objects that can receive a resolved hitscan impact.</summary>
public interface IHitscanTarget
{
    /// <summary>Applies one hitscan impact at the resolved world-space contact.</summary>
    void ReceiveHit(Node3D source, Vector3 position, Vector3 normal);
}
