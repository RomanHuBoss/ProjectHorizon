using System;
using Godot;

public sealed record SurfaceFlightSafetyAcceptanceReport(
    bool Passed,
    bool MonotonicBrake,
    bool NoReverseAfterStop,
    bool SmoothAtmosphereDynamics,
    bool AtmosphereVisualEnvelopeMatched,
    bool SmoothClimbLimiter,
    bool HandoffOutsideAtmosphere,
    bool HandoffVelocityContinuity,
    bool SurfaceResidencyEnvelope,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-178.7 surface solidity/braking/handoff acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"monotonicBrake={(MonotonicBrake ? 1 : 0)}; noReverse={(NoReverseAfterStop ? 1 : 0)}; " +
        $"smoothDynamics={(SmoothAtmosphereDynamics ? 1 : 0)}; " +
        $"envelopeMatched={(AtmosphereVisualEnvelopeMatched ? 1 : 0)}; " +
        $"smoothClimbLimiter={(SmoothClimbLimiter ? 1 : 0)}; " +
        $"handoffOutsideAtmosphere={(HandoffOutsideAtmosphere ? 1 : 0)}; " +
        $"handoffVelocity={(HandoffVelocityContinuity ? 1 : 0)}; " +
        $"surfaceResidency={(SurfaceResidencyEnvelope ? 1 : 0)}; result={Result}";
}

public static class SurfaceFlightSafetyAcceptanceRunner
{
    public static SurfaceFlightSafetyAcceptanceReport Run()
    {
        Vector3 initial = new(13.0f, -7.0f, -82.0f);
        Vector3 velocity = initial;
        float previousSpeed = velocity.Length();
        bool monotonicBrake = true;
        bool noReverse = true;
        for (int index = 0; index < 240; index++)
        {
            velocity = ArcadeShipBrakeRuntime.ApplyMonotonicBrake(
                velocity,
                38.0f,
                1.0f / 60.0f);
            float speed = velocity.Length();
            monotonicBrake &= speed <= previousSpeed + 0.0001f;
            if (speed > 0.0001f)
            {
                noReverse &= velocity.Dot(initial) >= -0.0001f;
            }
            previousSpeed = speed;
        }
        monotonicBrake &= velocity.Length() <= 0.0001f;
        noReverse &= velocity == Vector3.Zero;
        Vector3 disturbedBrake = ArcadeShipBrakeRuntime.ApplyMonotonicBrakeEnvelope(
            new Vector3(0.0f, 0.0f, -0.20f),
            new Vector3(0.0f, 0.0f, 0.45f),
            38.0f,
            1.0f / 60.0f);
        noReverse &= disturbedBrake == Vector3.Zero;

        float atmosphereStart = (float)
            OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters;
        float atmosphereHeight = (float)
            OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters;
        bool smoothDynamics =
            ArcadeShipController.ComputeAtmosphereBlend(
                0.0f, atmosphereStart, atmosphereHeight) >= 0.999f &&
            ArcadeShipController.ComputeAtmosphereBlend(
                atmosphereHeight, atmosphereStart, atmosphereHeight) <= 0.001f;
        bool envelopeMatched = true;
        float prior = 1.0f;
        float maxStep = 0.0f;
        for (float altitude = 0.0f; altitude <= atmosphereHeight + 20.0f; altitude += 5.0f)
        {
            float blend = ArcadeShipController.ComputeAtmosphereBlend(
                altitude,
                atmosphereStart,
                atmosphereHeight);
            float vacuum = (float)
                OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(altitude);
            smoothDynamics &= blend <= prior + 0.0001f;
            maxStep = Math.Max(maxStep, Math.Abs(prior - blend));
            prior = blend;
            envelopeMatched &= Math.Abs((blend + vacuum) - 1.0f) <= 0.0002f;
        }
        smoothDynamics &= maxStep <= 0.025f;

        bool smoothClimbLimiter = true;
        const float climbProbeSpeed = 85.0f;
        const float climbProbeDelta = 1.0f / 60.0f;
        for (float blend = 0.0f; blend <= 1.0001f; blend += 0.02f)
        {
            float limited = ArcadeShipController.ComputeSmoothAtmosphericClimbSpeed(
                climbProbeSpeed,
                blend,
                18.0f,
                85.0f,
                42.0f,
                climbProbeDelta);
            float correction = climbProbeSpeed - limited;
            smoothClimbLimiter &= correction >= -0.0001f &&
                correction <= (42.0f * blend * climbProbeDelta) + 0.0001f;
        }

        bool handoffOutsideAtmosphere =
            PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters >=
                OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters + 40.0;

        StageOneVoyageRuntime voyage = new(new StageOneVoyageSaveData(
            StageOneVoyageLocation.InboundFlight,
            Piloted: true,
            StationVisited: true,
            StationVisitedThisLoop: true,
            TakeoffCount: 1,
            DockingCount: 1,
            LandingCount: 0,
            CompletedLoops: 0,
            PositionX: StageOneVoyageRuntime.StationDockPositionX,
            PositionY: StageOneVoyageRuntime.StationDockPositionY,
            PositionZ: StageOneVoyageRuntime.StationUndockPositionZ,
            RotationX: 0.0,
            RotationY: Math.PI,
            RotationZ: 0.0,
            VelocityX: 0.0,
            VelocityY: 0.0,
            VelocityZ: 85.0,
            LastCheckpoint: "flight.inbound"));
        voyage.ArriveAtPlanetaryApproach(85.0);
        double handoffSpeed = Math.Sqrt(
            (voyage.VelocityX * voyage.VelocityX) +
            (voyage.VelocityY * voyage.VelocityY) +
            (voyage.VelocityZ * voyage.VelocityZ));
        bool handoffVelocity = Math.Abs(handoffSpeed - 85.0) <= 0.01 &&
            voyage.VelocityY < -60.0 && voyage.VelocityZ > 0.0 &&
            voyage.RotationX < -0.7;

        bool surfaceResidency =
            SalvageRepairSlice.PlanetRuntimeActivationAltitudeMeters >=
                PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters + 150.0 &&
            SalvageRepairSlice.PlanetRuntimeActivationAltitudeMeters <= 1200.0f;

        bool passed = monotonicBrake && noReverse && smoothDynamics &&
            envelopeMatched && smoothClimbLimiter && handoffOutsideAtmosphere &&
            handoffVelocity && surfaceResidency;
        return new SurfaceFlightSafetyAcceptanceReport(
            passed,
            monotonicBrake,
            noReverse,
            smoothDynamics,
            envelopeMatched,
            smoothClimbLimiter,
            handoffOutsideAtmosphere,
            handoffVelocity,
            surfaceResidency,
            passed
                ? "swept terrain residency, zero-clamped braking and matched atmosphere/vacuum dynamics verified"
                : "one or more TASK-178.7 surface/brake/handoff invariants failed");
    }
}
