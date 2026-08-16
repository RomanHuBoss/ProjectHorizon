using Godot;

namespace ProjectHorizon.Tests.Unit;

public sealed class SurfaceFlightSafetyTests
{
    [Fact]
    public void Task1787_ModelContractPasses()
    {
        SurfaceFlightSafetyAcceptanceReport report =
            SurfaceFlightSafetyAcceptanceRunner.Run();

        Assert.True(report.Passed, report.Result);
        Assert.True(report.MonotonicBrake);
        Assert.True(report.NoReverseAfterStop);
        Assert.True(report.SmoothAtmosphereDynamics);
        Assert.True(report.AtmosphereVisualEnvelopeMatched);
        Assert.True(report.SmoothClimbLimiter);
        Assert.True(report.HandoffOutsideAtmosphere);
        Assert.True(report.HandoffVelocityContinuity);
        Assert.True(report.SurfaceResidencyEnvelope);
    }

    [Fact]
    public void Brake_HeldForeverStopsAtZeroWithoutReversing()
    {
        Vector3 initial = new(0.0f, 0.0f, -85.0f);
        Vector3 velocity = initial;
        float previous = velocity.Length();
        for (int index = 0; index < 600; index++)
        {
            velocity = ArcadeShipBrakeRuntime.ApplyMonotonicBrake(
                velocity,
                38.0f,
                1.0f / 60.0f);
            Assert.True(velocity.Length() <= previous + 0.0001f);
            Assert.True(velocity.Dot(initial) >= -0.0001f);
            previous = velocity.Length();
        }

        Assert.Equal(Vector3.Zero, velocity);
    }

    [Fact]
    public void AtmosphereDynamics_FadeSmoothlyAcrossVisualHandoff()
    {
        float height = (float)
            OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters;
        float previous = 1.0f;
        for (float altitude = 0.0f; altitude <= height; altitude += 10.0f)
        {
            float current = ArcadeShipController.ComputeAtmosphereBlend(
                altitude,
                (float)OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters,
                height);
            Assert.InRange(previous - current, -0.0001f, 0.03f);
            previous = current;
        }

        Assert.True(
            PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters > height);
    }

    [Fact]
    public void PlanetaryHandoff_PreservesIncomingSpeedInsteadOfHardStopping()
    {
        StageOneVoyageRuntime voyage = new(new StageOneVoyageSaveData(
            StageOneVoyageLocation.InboundFlight,
            Piloted: true,
            StationVisited: true,
            StationVisitedThisLoop: true,
            TakeoffCount: 1,
            DockingCount: 1,
            LandingCount: 0,
            CompletedLoops: 0,
            PositionX: 0.0,
            PositionY: 35.0,
            PositionZ: -1582.0,
            RotationX: 0.0,
            RotationY: Math.PI,
            RotationZ: 0.0,
            VelocityX: 0.0,
            VelocityY: 0.0,
            VelocityZ: 85.0,
            LastCheckpoint: "flight.inbound"));

        voyage.ArriveAtPlanetaryApproach(85.0);
        double speed = Math.Sqrt(
            (voyage.VelocityX * voyage.VelocityX) +
            (voyage.VelocityY * voyage.VelocityY) +
            (voyage.VelocityZ * voyage.VelocityZ));

        Assert.InRange(speed, 84.99, 85.01);
        Assert.True(voyage.VelocityY < 0.0);
        Assert.True(voyage.VelocityZ > 0.0);
    }

    [Fact]
    public void Brake_RejectsEnvironmentalReverseImpulse()
    {
        Vector3 before = new(0.0f, 0.0f, -0.20f);
        Vector3 afterEnvironment = new(0.0f, 0.0f, 0.45f);
        Vector3 braked = ArcadeShipBrakeRuntime.ApplyMonotonicBrakeEnvelope(
            before,
            afterEnvironment,
            38.0f,
            1.0f / 60.0f);

        Assert.Equal(Vector3.Zero, braked);
    }

    [Fact]
    public void AtmosphereDynamics_ExactlyComplementVisualVacuumBlend()
    {
        float start = (float)OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters;
        float end = (float)OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters;
        for (float altitude = 0.0f; altitude <= end + 50.0f; altitude += 5.0f)
        {
            float atmosphere = ArcadeShipController.ComputeAtmosphereBlend(
                altitude,
                start,
                end);
            float vacuum = (float)
                OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(altitude);
            Assert.InRange(Math.Abs((atmosphere + vacuum) - 1.0f), 0.0f, 0.0001f);
        }
    }

    [Fact]
    public void AtmosphericClimbLimiter_CannotCreateBoundaryImpulse()
    {
        const float radialSpeed = 85.0f;
        const float dt = 1.0f / 60.0f;
        for (float blend = 0.0f; blend <= 1.0001f; blend += 0.01f)
        {
            float limited = ArcadeShipController.ComputeSmoothAtmosphericClimbSpeed(
                radialSpeed,
                blend,
                18.0f,
                85.0f,
                42.0f,
                dt);
            Assert.True(radialSpeed - limited <= (42.0f * blend * dt) + 0.0001f);
            Assert.True(limited <= radialSpeed + 0.0001f);
        }
    }
}
