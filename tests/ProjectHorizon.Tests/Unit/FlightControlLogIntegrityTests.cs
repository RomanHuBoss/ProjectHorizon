using Godot;

namespace ProjectHorizon.Tests.Unit;

public sealed class FlightControlLogIntegrityTests
{
    [Fact]
    public void VirtualFlightStick_RetainsDeflectionUntilPilotRecenters()
    {
        Vector2 stick = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
            Vector2.Zero,
            new Vector2(50.0f, -30.0f),
            0.0035f,
            2.0f,
            invertPitch: false,
            invertHorizontal: false);
        Vector2 noMotion = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
            stick,
            Vector2.Zero,
            0.0035f,
            2.0f,
            invertPitch: false,
            invertHorizontal: false);

        Assert.Equal(stick, noMotion);
        Assert.True(stick.Length() > 0.2f);
    }

    [Fact]
    public void VirtualFlightStick_HorizontalIsRollDominant_VerticalIsPitch()
    {
        Vector3 right = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand(
            new Vector2(0.0f, 0.85f));
        Vector3 up = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand(
            new Vector2(0.85f, 0.0f));

        Assert.True(right.Z >= 0.70f); // positive local-forward roll = right bank
        Assert.True(right.Y < 0.0f);   // negative local-up yaw = nose right
        Assert.True(Math.Abs(right.Z) >= Math.Abs(right.Y) * 4.0f);
        Assert.InRange(Math.Abs(right.X), 0.0f, 0.001f);
        Assert.True(Math.Abs(up.X) >= 0.70f);
        Assert.InRange(Math.Abs(up.Y), 0.0f, 0.001f);
        Assert.InRange(Math.Abs(up.Z), 0.0f, 0.001f);
    }

    [Fact]
    public void VirtualFlightStick_MouseRightProducesRightBankAndRightYaw()
    {
        Vector2 stick = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
            Vector2.Zero,
            new Vector2(80.0f, 0.0f),
            0.0035f,
            1.45f,
            invertPitch: false,
            invertHorizontal: false);
        Vector3 command = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand(stick);

        Assert.True(stick.Y > 0.0f);
        Assert.True(command.Z > 0.0f);
        Assert.True(command.Y < 0.0f);
    }

    [Fact]
    public void VirtualStick_ResponseHasDeadZoneAndProgressiveCurve()
    {
        Assert.Equal(0.0f, ArcadeFlightAssistRuntime.ApplyVirtualStickResponse(0.02f));
        float medium = ArcadeFlightAssistRuntime.ApplyVirtualStickResponse(0.5f);
        float large = ArcadeFlightAssistRuntime.ApplyVirtualStickResponse(0.9f);
        Assert.InRange(medium, 0.1f, 0.6f);
        Assert.True(large > medium);
    }

    [Fact]
    public void CameraEnvelope_KeepsStarfieldInsideStableFrustum()
    {
        Assert.True(ArcadeShipController.DefaultFlightCameraNearMeters >= 0.20f);
        Assert.True(ArcadeShipController.DefaultFlightCameraFarMeters <= 900000.0f);
        Assert.True(
            OrbitalHandoffPresentationRuntime.StarfieldRadiusMeters <=
            ArcadeShipController.DefaultFlightCameraFarMeters * 0.90f);
    }

    [Fact]
    public void Acceptance_PassesTheIntegratedHotfixContract()
    {
        FlightControlLogIntegrityAcceptanceReport report =
            FlightControlLogIntegrityAcceptanceRunner.Evaluate(
                ArcadeShipController.DefaultFlightCameraNearMeters,
                ArcadeShipController.DefaultFlightCameraFarMeters,
                surfaceGuardHysteresis: true,
                surfaceWeatherOwnership: true,
                overflowSafeTerrainDistance: true);
        Assert.True(report.Passed, report.Result);
    }
}
