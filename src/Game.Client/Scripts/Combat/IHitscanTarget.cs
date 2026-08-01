using Godot;

public interface IHitscanTarget
{
    void ReceiveHit(Node3D source, Vector3 position, Vector3 normal);
}
