using System;
using Godot;

public sealed record FlightFeelHotfixAcceptanceReport(
    bool Passed,
    bool SurfaceSunIsolation,
    double StarAngularDiameterDegrees,
    bool ManualCrashEnvelope,
    bool LethalImpactEnvelope,
    bool MouseNoseFirst,
    bool MouseBanking,
    bool FullAttitudeRotation,
    string Result)
{
    public string BuildOutputLine() =>
        "TASK-180.2 flight feel hotfix acceptance " +
        $"{(Passed ? "PASS" : "FAIL")}: " +
        $"surfaceSunIsolated={(SurfaceSunIsolation ? 1 : 0)}; " +
        $"starAngularDiameter={StarAngularDiameterDegrees:0.0}deg; " +
        $"manualCrash={(ManualCrashEnvelope ? 1 : 0)}; " +
        $"lethalImpact={(LethalImpactEnvelope ? 1 : 0)}; " +
        $"mouseNose={(MouseNoseFirst ? 1 : 0)}; " +
        $"mouseBank={(MouseBanking ? 1 : 0)}; " +
        $"fullRotation={(FullAttitudeRotation ? 1 : 0)}; result={Result}";
}

public static class FlightFeelHotfixAcceptanceRunner
{
    public const double MinimumStarAngularDiameterDegrees = 4.0;

    public static FlightFeelHotfixAcceptanceReport Evaluate(
        double starAngularDiameterDegrees)
    {
        bool surfaceSunIsolation =
            PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
                true, WorldSceneKind.Surface) &&
            !PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
                true, WorldSceneKind.Orbit) &&
            !PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
                true, WorldSceneKind.InterplanetaryTransit);

        bool manualCrashEnvelope =
            PlanetaryApproachRuntime.MaximumManualOrbitalEntrySpeed > 0.0 &&
            PlanetaryApproachRuntime.MaximumManualOrbitalEntrySpeed <
                PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed;
        bool lethalImpactEnvelope =
            !PlanetaryImpactRuntime.IsLethalSurfaceImpact(5.0f, 18.0f) &&
            PlanetaryImpactRuntime.IsLethalSurfaceImpact(18.0f, 32.0f);

        Vector3 horizontal = ArcadeFlightAssistRuntime.BuildMouseAttitudeCommand(
            new Vector2(0.0f, -0.8f),
            0.62f);
        Vector3 vertical = ArcadeFlightAssistRuntime.BuildMouseAttitudeCommand(
            new Vector2(0.75f, 0.0f),
            0.62f);
        bool mouseNoseFirst =
            Math.Abs(horizontal.Y) >= 0.75f &&
            Math.Abs(horizontal.X) <= 0.001f &&
            Math.Abs(vertical.X) >= 0.70f &&
            Math.Abs(vertical.Y) <= 0.001f;
        bool mouseBanking = Math.Abs(horizontal.Z) >= 0.35f;
        bool fullAttitudeRotation =
            ArcadeShipController.FullAttitudeRotationEnabled &&
            !ArcadeShipController.MouseTranslationCouplingEnabled;
        bool starScale = double.IsFinite(starAngularDiameterDegrees) &&
            starAngularDiameterDegrees >= MinimumStarAngularDiameterDegrees;

        bool passed = surfaceSunIsolation && starScale &&
            manualCrashEnvelope && lethalImpactEnvelope && mouseNoseFirst &&
            mouseBanking && fullAttitudeRotation;
        return new FlightFeelHotfixAcceptanceReport(
            passed,
            surfaceSunIsolation,
            starAngularDiameterDegrees,
            manualCrashEnvelope,
            lethalImpactEnvelope,
            mouseNoseFirst,
            mouseBanking,
            fullAttitudeRotation,
            passed
                ? "surface-only local sun, substantial system star, crashable planet and nose-first banked mouse attitude verified"
                : "stellar ownership/scale, impact envelope or mouse attitude contract failed");
    }
}
