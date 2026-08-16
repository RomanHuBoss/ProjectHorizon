using System;
using Godot;

public sealed record SpaceflightCollisionRecoveryAcceptanceReport(
    bool Passed,
    bool HeadingCoupling,
    bool DriftOptOut,
    bool SpeedConservation,
    bool SweptPlanetCollision,
    bool HighSpeedTunnelingBlocked,
    bool MissRejected,
    bool EntryShellCrossing,
    double FinalHeadingErrorDegrees,
    string Result)
{
    public string BuildOutputLine() =>
        "TASK-178.5 spaceflight kinematics/collision acceptance " +
        (Passed ? "PASS" : "FAIL") + ": " +
        $"headingCoupling={(HeadingCoupling ? 1 : 0)}; " +
        $"driftOptOut={(DriftOptOut ? 1 : 0)}; " +
        $"speedConservation={(SpeedConservation ? 1 : 0)}; " +
        $"sweptPlanetCollision={(SweptPlanetCollision ? 1 : 0)}; " +
        $"highSpeedTunnelingBlocked={(HighSpeedTunnelingBlocked ? 1 : 0)}; " +
        $"missRejected={(MissRejected ? 1 : 0)}; " +
        $"entryShellCrossing={(EntryShellCrossing ? 1 : 0)}; " +
        $"headingError={FinalHeadingErrorDegrees:0.00}deg; result={Result}";
}

public static class SpaceflightCollisionRecoveryAcceptanceRunner
{
    public static SpaceflightCollisionRecoveryAcceptanceReport Run()
    {
        Basis turned = Basis.Identity.Rotated(
            Vector3.Up,
            Mathf.DegToRad(100.0f));
        ShipControlCommand neutral = ShipControlCommand.Neutral;
        Vector3 initialVelocity = Vector3.Forward * 72.0f;
        Vector3 aligned = initialVelocity;
        for (int i = 0; i < 120; i++)
        {
            aligned = ArcadeFlightAssistRuntime.AlignVelocityToShipAxes(
                aligned,
                turned,
                neutral,
                flightAssistEnabled: true,
                deltaSeconds: 1.0f / 60.0f);
        }

        double headingError = ArcadeFlightAssistRuntime.HeadingErrorDegrees(
            aligned,
            turned);
        bool headingCoupling = headingError <= 3.0;
        bool speedConservation = Math.Abs(
            aligned.Length() - initialVelocity.Length()) <= 0.02;
        Vector3 drift = ArcadeFlightAssistRuntime.AlignVelocityToShipAxes(
            initialVelocity,
            turned,
            neutral,
            flightAssistEnabled: false,
            deltaSeconds: 1.0f);
        bool driftOptOut = drift.IsEqualApprox(initialVelocity);

        bool swept = OrbitalBodyCollisionRuntime.TrySweepSphere(
            new Vector3(-2500.0f, 0.0f, 0.0f),
            new Vector3(2500.0f, 0.0f, 0.0f),
            Vector3.Zero,
            1000.0f,
            out float fraction,
            out Vector3 impact,
            out Vector3 normal);
        bool sweptPlanetCollision = swept &&
            Math.Abs(fraction - 0.3f) <= 0.001f &&
            Math.Abs(impact.X + 1000.0f) <= 0.1f &&
            normal.Dot(Vector3.Left) >= 0.999f;
        bool highSpeedTunnelingBlocked = swept && fraction is > 0.0f and < 1.0f;

        bool missRejected = !OrbitalBodyCollisionRuntime.TrySweepSphere(
            new Vector3(-2500.0f, 1400.0f, 0.0f),
            new Vector3(2500.0f, 1400.0f, 0.0f),
            Vector3.Zero,
            1000.0f,
            out _,
            out _,
            out _);

        bool entryShellCrossing = OrbitalBodyCollisionRuntime.CrossedOuterShell(
            new Vector3(0.0f, 0.0f, 1500.0f),
            new Vector3(0.0f, 0.0f, 1210.0f),
            Vector3.Zero,
            1220.0f);

        bool passed = headingCoupling && driftOptOut && speedConservation &&
            sweptPlanetCollision && highSpeedTunnelingBlocked && missRejected &&
            entryShellCrossing;
        return new SpaceflightCollisionRecoveryAcceptanceReport(
            passed,
            headingCoupling,
            driftOptOut,
            speedConservation,
            sweptPlanetCollision,
            highSpeedTunnelingBlocked,
            missRejected,
            entryShellCrossing,
            headingError,
            passed
                ? "arcade heading coupling, optional inertial drift and continuous planetary collision verified"
                : "one or more TASK-178.5 flight/collision invariants failed");
    }
}
