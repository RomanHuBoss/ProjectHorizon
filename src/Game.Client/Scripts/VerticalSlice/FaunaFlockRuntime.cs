using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed record FaunaFlockSample(
    string InstanceId,
    string FaunaId,
    Vector3 Position,
    Vector3 Velocity,
    bool Alive);

public sealed record FaunaFlockSteering(
    Vector3 Separation,
    Vector3 Cohesion,
    Vector3 Alignment,
    Vector3 Combined,
    int Neighbors);

/// <summary>Simplified boids steering required by specification 12.2.</summary>
public static class FaunaFlockRuntime
{
    public const float NeighborRadiusMeters = 12.0f;
    public const float SeparationRadiusMeters = 3.2f;

    public static FaunaFlockSteering Compute(
        FaunaFlockSample self,
        IReadOnlyList<FaunaFlockSample> population)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(population);
        FaunaFlockSample[] neighbors = population
            .Where(other => other.Alive &&
                !string.Equals(other.InstanceId, self.InstanceId, StringComparison.Ordinal) &&
                string.Equals(other.FaunaId, self.FaunaId, StringComparison.Ordinal) &&
                other.Position.DistanceSquaredTo(self.Position) <=
                    NeighborRadiusMeters * NeighborRadiusMeters)
            .ToArray();
        if (neighbors.Length == 0)
        {
            return new FaunaFlockSteering(
                Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0);
        }

        Vector3 separation = Vector3.Zero;
        Vector3 center = Vector3.Zero;
        Vector3 velocity = Vector3.Zero;
        foreach (FaunaFlockSample neighbor in neighbors)
        {
            Vector3 delta = self.Position - neighbor.Position;
            float distance = Math.Max(0.05f, delta.Length());
            if (distance <= SeparationRadiusMeters)
            {
                separation += delta.Normalized() *
                    ((SeparationRadiusMeters - distance) / SeparationRadiusMeters);
            }
            center += neighbor.Position;
            velocity += neighbor.Velocity;
        }
        center /= neighbors.Length;
        velocity /= neighbors.Length;
        Vector3 cohesion = center - self.Position;
        if (cohesion.LengthSquared() > 0.0001f)
        {
            cohesion = cohesion.Normalized();
        }
        Vector3 alignment = velocity.LengthSquared() > 0.0001f
            ? velocity.Normalized()
            : Vector3.Zero;
        if (separation.LengthSquared() > 0.0001f)
        {
            separation = separation.Normalized();
        }
        Vector3 combined = separation * 1.25f + cohesion * 0.55f + alignment * 0.45f;
        if (combined.LengthSquared() > 1.0f)
        {
            combined = combined.Normalized();
        }
        return new FaunaFlockSteering(
            separation,
            cohesion,
            alignment,
            combined,
            neighbors.Length);
    }
}
