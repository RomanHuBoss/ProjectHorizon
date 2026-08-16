using Godot;

namespace ProjectHorizon.Tests.Unit;

public sealed class SpaceflightCollisionRecoveryTests
{
    [Fact]
    public void SpaceflightCollisionRecovery_ModelContractPasses()
    {
        SpaceflightCollisionRecoveryAcceptanceReport report =
            SpaceflightCollisionRecoveryAcceptanceRunner.Run();

        Assert.True(report.Passed, report.Result);
        Assert.True(report.HeadingCoupling);
        Assert.True(report.DriftOptOut);
        Assert.True(report.SpeedConservation);
        Assert.True(report.SweptPlanetCollision);
        Assert.True(report.HighSpeedTunnelingBlocked);
        Assert.True(report.EntryShellCrossing);
    }

    [Fact]
    public void SweptSphere_CatchesSegmentThatCrossesEntirePlanetInOneTick()
    {
        bool hit = OrbitalBodyCollisionRuntime.TrySweepSphere(
            new Vector3(-5000.0f, 0.0f, 0.0f),
            new Vector3(5000.0f, 0.0f, 0.0f),
            Vector3.Zero,
            1200.0f,
            out float fraction,
            out Vector3 impact,
            out _);

        Assert.True(hit);
        Assert.InRange(fraction, 0.379f, 0.381f);
        Assert.InRange(impact.X, -1201.0f, -1199.0f);
    }

    [Fact]
    public void HeadingAssist_CurvesVelocityTowardTurnedNoseWithoutChangingSpeed()
    {
        Basis basis = Basis.Identity.Rotated(Vector3.Up, Mathf.Pi / 2.0f);
        Vector3 velocity = Vector3.Forward * 60.0f;
        for (int i = 0; i < 90; i++)
        {
            velocity = ArcadeFlightAssistRuntime.AlignVelocityToShipAxes(
                velocity,
                basis,
                ShipControlCommand.Neutral,
                flightAssistEnabled: true,
                deltaSeconds: 1.0f / 60.0f);
        }

        Assert.InRange(
            ArcadeFlightAssistRuntime.HeadingErrorDegrees(velocity, basis),
            0.0f,
            2.0f);
        Assert.InRange(velocity.Length(), 59.98f, 60.02f);
    }
}
