using System;
using System.Collections.Generic;
using Godot;

public partial class ShipLandingTestSite : Node3D
{
    [Export(PropertyHint.Range, "10.0,10000.0,1.0")]
    public float PlanetRadius { get; set; } = 120.0f;

    [Export]
    public Vector3 ReferenceDirection { get; set; } = Vector3.Up;

    [Export(PropertyHint.Range, "2.0,30.0,0.5")]
    public float CandidateAngularOffsetDegrees { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "5.0,45.0,0.5")]
    public float RejectedSlopeDegrees { get; set; } = 18.0f;

    [Export(PropertyHint.Range, "0.1,10.0,0.1")]
    public float ObstacleRadius { get; set; } = 2.4f;

    private Node3D? _slopePatch;
    private Node3D? _obstacleRock;
    private Node3D? _safePad;

    public Vector3 SlopeCandidateDirection => NormalizeDirection(
        ReferenceDirection);

    public Vector3 ObstacleCandidateDirection => RotateAroundAxis(
        SlopeCandidateDirection,
        GetCandidateRotationAxis(SlopeCandidateDirection),
        Mathf.DegToRad(CandidateAngularOffsetDegrees));

    public Vector3 SafeCandidateDirection => RotateAroundAxis(
        SlopeCandidateDirection,
        GetCandidateRotationAxis(SlopeCandidateDirection),
        Mathf.DegToRad(-CandidateAngularOffsetDegrees));

    public override void _Ready()
    {
        _slopePatch = GetNodeOrNull<Node3D>("SlopePatch");
        _obstacleRock = GetNodeOrNull<Node3D>("ObstacleRock");
        _safePad = GetNodeOrNull<Node3D>("SafePad");

        if (_slopePatch is null || _obstacleRock is null || _safePad is null)
        {
            throw new InvalidOperationException(
                "LandingTestSite requires SlopePatch, ObstacleRock and SafePad.");
        }

        ConfigureSlopePatch(_slopePatch, SlopeCandidateDirection);
        ConfigureRadialNode(
            _obstacleRock,
            ObstacleCandidateDirection,
            PlanetRadius + ObstacleRadius);
        ConfigureRadialNode(
            _safePad,
            SafeCandidateDirection,
            PlanetRadius + 0.12f);

        GD.Print(
            "Landing test site ready: " +
            $"slope={RejectedSlopeDegrees:F1}°; " +
            $"offset={CandidateAngularOffsetDegrees:F1}°; " +
            $"obstacleRadius={ObstacleRadius:F1} m");
    }

    public IReadOnlyList<Vector3> GetCandidateDirections()
    {
        return new[]
        {
            SlopeCandidateDirection,
            ObstacleCandidateDirection,
            SafeCandidateDirection
        };
    }

    public Vector3 GetTestApproachDirection()
    {
        return SlopeCandidateDirection;
    }

    private void ConfigureSlopePatch(Node3D node, Vector3 direction)
    {
        ConfigureRadialNode(node, direction, PlanetRadius + 0.55f);
        node.RotateObjectLocal(
            Vector3.Right,
            Mathf.DegToRad(RejectedSlopeDegrees));
    }

    private void ConfigureRadialNode(
        Node3D node,
        Vector3 direction,
        float radialDistance)
    {
        Vector3 radialUp = NormalizeDirection(direction);
        Basis basis = CreateRadialBasis(radialUp);
        node.Transform = new Transform3D(
            basis,
            radialUp * radialDistance);
    }

    private static Basis CreateRadialBasis(Vector3 radialUp)
    {
        Vector3 reference = Math.Abs(radialUp.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 forward = reference.Slide(radialUp).Normalized();
        Vector3 right = forward.Cross(radialUp).Normalized();
        Vector3 back = right.Cross(radialUp).Normalized();
        return new Basis(right, radialUp, back).Orthonormalized();
    }

    private static Vector3 GetCandidateRotationAxis(Vector3 direction)
    {
        Vector3 axis = direction.Cross(Vector3.Forward);
        if (axis.LengthSquared() <= 0.000001f)
        {
            axis = direction.Cross(Vector3.Right);
        }

        return axis.Normalized();
    }

    private static Vector3 RotateAroundAxis(
        Vector3 vector,
        Vector3 axis,
        float angle)
    {
        Vector3 normalizedAxis = axis.Normalized();
        float cosine = Mathf.Cos(angle);
        float sine = Mathf.Sin(angle);
        return ((vector * cosine) +
            (normalizedAxis.Cross(vector) * sine) +
            (normalizedAxis * normalizedAxis.Dot(vector) * (1.0f - cosine)))
            .Normalized();
    }

    private static Vector3 NormalizeDirection(Vector3 direction)
    {
        return direction.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : direction.Normalized();
    }
}
