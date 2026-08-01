using System;
using Godot;

public partial class HitscanWeapon : Node3D
{
    [Export(PropertyHint.Range, "0.05,2.0,0.05")]
    public float CooldownSeconds { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "1.0,250.0,1.0")]
    public float RangeMeters { get; set; } = 50.0f;

    private RayCast3D _fireRay = null!;
    private Node3D _shotSource = null!;
    private ulong _nextAllowedShotAtMilliseconds;

    public override void _Ready()
    {
        _fireRay = GetNode<RayCast3D>("FireRay");
        _fireRay.TargetPosition = new Vector3(0.0f, 0.0f, -RangeMeters);

        CollisionObject3D? owningBody = FindOwningBody();

        if (owningBody is not null)
        {
            _shotSource = owningBody;
            _fireRay.AddException(owningBody);
        }
        else
        {
            _shotSource = this;
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured ||
            !inputEvent.IsActionPressed("fire_primary"))
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        TryFire();
    }

    private void TryFire()
    {
        ulong now = Time.GetTicksMsec();

        if (now < _nextAllowedShotAtMilliseconds)
        {
            return;
        }

        ulong cooldownMilliseconds = (ulong)Math.Max(
            1.0,
            Math.Ceiling(CooldownSeconds * 1000.0));

        _nextAllowedShotAtMilliseconds = now + cooldownMilliseconds;

        _fireRay.ForceRaycastUpdate();

        if (!_fireRay.IsColliding())
        {
            GD.Print("HitscanWeapon: miss");
            return;
        }

        GodotObject? collider = _fireRay.GetCollider();
        Vector3 hitPosition = _fireRay.GetCollisionPoint();
        Vector3 hitNormal = _fireRay.GetCollisionNormal();

        if (collider is IHitscanTarget target)
        {
            target.ReceiveHit(_shotSource, hitPosition, hitNormal);
            return;
        }

        if (collider is Node colliderNode)
        {
            GD.Print($"HitscanWeapon: hit {colliderNode.Name}");
        }
    }

    private CollisionObject3D? FindOwningBody()
    {
        Node? current = GetParent();

        while (current is not null)
        {
            if (current is CollisionObject3D collisionObject)
            {
                return collisionObject;
            }

            current = current.GetParent();
        }

        return null;
    }
}
