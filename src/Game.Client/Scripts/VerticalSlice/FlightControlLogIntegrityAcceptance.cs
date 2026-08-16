using System;
using Godot;

public sealed record FlightControlLogIntegrityAcceptanceReport(
    bool Passed,
    bool StatefulVirtualStick,
    bool RollDominantHorizontal,
    bool VerticalPitch,
    bool StableCameraFrustum,
    bool StarfieldInsideFrustum,
    bool SurfaceGuardHysteresis,
    bool SurfaceWeatherOwnership,
    bool OverflowSafeTerrainDistance,
    string Result)
{
    public string BuildOutputLine() =>
        "TASK-180.3 flight-control/log-integrity acceptance " +
        $"{(Passed ? "PASS" : "FAIL")}: " +
        $"virtualStick={(StatefulVirtualStick ? 1 : 0)}; " +
        $"rollDominant={(RollDominantHorizontal ? 1 : 0)}; " +
        $"verticalPitch={(VerticalPitch ? 1 : 0)}; " +
        $"frustum={(StableCameraFrustum ? 1 : 0)}; " +
        $"starfieldInside={(StarfieldInsideFrustum ? 1 : 0)}; " +
        $"surfaceHysteresis={(SurfaceGuardHysteresis ? 1 : 0)}; " +
        $"weatherOwnership={(SurfaceWeatherOwnership ? 1 : 0)}; " +
        $"overflowSafe={(OverflowSafeTerrainDistance ? 1 : 0)}; result={Result}";
}

public static class FlightControlLogIntegrityAcceptanceRunner
{
    public static FlightControlLogIntegrityAcceptanceReport Evaluate(
        float cameraNearMeters,
        float cameraFarMeters,
        bool surfaceGuardHysteresis,
        bool surfaceWeatherOwnership,
        bool overflowSafeTerrainDistance)
    {
        Vector2 stick = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
            Vector2.Zero,
            new Vector2(64.0f, -48.0f),
            0.0035f,
            2.0f,
            invertPitch: false,
            invertHorizontal: false);
        Vector2 retained = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
            stick,
            Vector2.Zero,
            0.0035f,
            2.0f,
            invertPitch: false,
            invertHorizontal: false);
        bool statefulVirtualStick =
            ArcadeShipController.StatefulVirtualFlightStickEnabled &&
            retained.DistanceTo(stick) <= 0.000001f &&
            stick.Length() > 0.2f;

        Vector3 horizontal = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand(
            new Vector2(0.0f, 0.85f));
        Vector3 vertical = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand(
            new Vector2(0.85f, 0.0f));
        bool rollDominantHorizontal =
            horizontal.Z >= 0.70f && // right stick -> right bank
            horizontal.Y < 0.0f &&    // right stick -> right yaw in Godot local axes
            Math.Abs(horizontal.Z) >= Math.Abs(horizontal.Y) * 4.0f &&
            Math.Abs(horizontal.X) <= 0.001f;
        bool verticalPitch =
            Math.Abs(vertical.X) >= 0.70f &&
            Math.Abs(vertical.Y) <= 0.001f &&
            Math.Abs(vertical.Z) <= 0.001f;

        bool stableCameraFrustum =
            float.IsFinite(cameraNearMeters) &&
            float.IsFinite(cameraFarMeters) &&
            cameraNearMeters >= 0.20f &&
            cameraFarMeters <= ArcadeShipController.DefaultFlightCameraFarMeters &&
            cameraFarMeters / cameraNearMeters <= 4_500_000.0f;
        bool starfieldInsideFrustum =
            OrbitalHandoffPresentationRuntime.StarfieldRadiusMeters > 0.0f &&
            OrbitalHandoffPresentationRuntime.StarfieldRadiusMeters <=
                cameraFarMeters * 0.90f;

        bool passed = statefulVirtualStick && rollDominantHorizontal &&
            verticalPitch && stableCameraFrustum && starfieldInsideFrustum &&
            surfaceGuardHysteresis && surfaceWeatherOwnership &&
            overflowSafeTerrainDistance;
        return new FlightControlLogIntegrityAcceptanceReport(
            passed,
            statefulVirtualStick,
            rollDominantHorizontal,
            verticalPitch,
            stableCameraFrustum,
            starfieldInsideFrustum,
            surfaceGuardHysteresis,
            surfaceWeatherOwnership,
            overflowSafeTerrainDistance,
            passed
                ? "stateful roll-dominant virtual stick and log-derived runtime guards verified"
                : "virtual-stick, camera-frustum, surface-guard, weather ownership or overflow contract failed");
    }
}
