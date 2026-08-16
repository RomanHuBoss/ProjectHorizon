using Godot;
using Xunit;

namespace ProjectHorizon.Tests.Unit;

public sealed class RuntimeIntegrityTests
{
    [Fact]
    public void Task1801_StationSweepBlocksHighSpeedCenterlineTraversal()
    {
        bool hit = OrbitalStationCollisionRuntime.TrySweepExpandedAabb(
            new Vector3(0.0f, 0.0f, 80.0f),
            new Vector3(0.0f, 0.0f, -80.0f),
            new Vector3(27.0f, 8.0f, 19.0f),
            OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters,
            out OrbitalStationCollisionHit contact);

        Assert.True(hit);
        Assert.InRange(contact.SegmentFraction, 0.0f, 1.0f);
        Assert.True(contact.LocalSurfaceNormal.LengthSquared() > 0.99f);
    }

    [Fact]
    public void Task1801_StationSweepLeavesDockApproachOutsideCoreFree()
    {
        bool hit = OrbitalStationCollisionRuntime.TrySweepExpandedAabb(
            new Vector3(0.0f, 0.0f, 80.0f),
            new Vector3(0.0f, 0.0f, 31.0f),
            new Vector3(27.0f, 8.0f, 19.0f),
            OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters,
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void Task1801_RuntimeIntegrityContractRequiresAllPhysicalGuards()
    {
        RuntimeIntegrityAcceptanceReport pass =
            RuntimeIntegrityAcceptanceRunner.Evaluate(
                planetClosed: true,
                planetFaces: 6,
                stationCollisionShapes: 24,
                stationSweepGuard: true,
                terrainObserverResolved: true);
        RuntimeIntegrityAcceptanceReport fail =
            RuntimeIntegrityAcceptanceRunner.Evaluate(
                planetClosed: false,
                planetFaces: 6,
                stationCollisionShapes: 3,
                stationSweepGuard: false,
                terrainObserverResolved: false);

        Assert.True(pass.Passed);
        Assert.False(fail.Passed);
    }
}
