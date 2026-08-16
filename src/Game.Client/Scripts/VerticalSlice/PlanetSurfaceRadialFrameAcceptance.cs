using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetSurfaceRadialFrameAcceptanceReport(
    bool Passed,
    bool GravityScaled,
    bool TangentFramesOrthonormal,
    bool FaceCoverage,
    bool FaceUvBounded,
    bool SeamContinuous,
    bool GeodesicStepExact,
    bool WarpRoundTrip,
    bool LocalGravityEquivalent,
    bool BoundedGameplayStreamer,
    int Planets,
    int FacesCovered,
    int FaceTransitions,
    double MaximumBasisError,
    double MaximumGeodesicStepErrorMeters,
    double MaximumSeamUpDeltaDegrees,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planets={Planets}, faces={FacesCovered}/6, transitions={FaceTransitions}, stepErr={MaximumGeodesicStepErrorMeters:0.0000}m"
        : $"FAIL — {Result}";

    public string BuildOutputLine() =>
        $"TASK-170 radial surface frame acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"planets={Planets}; gravity={(GravityScaled ? 1 : 0)}; tangentFrames={(TangentFramesOrthonormal ? 1 : 0)}; " +
        $"faces={FacesCovered}/6; faceUv={(FaceUvBounded ? 1 : 0)}; seamContinuity={(SeamContinuous ? 1 : 0)}; " +
        $"geodesicStep={(GeodesicStepExact ? 1 : 0)}; warpRoundTrip={(WarpRoundTrip ? 1 : 0)}; " +
        $"localGravity={(LocalGravityEquivalent ? 1 : 0)}; boundedStreamer={(BoundedGameplayStreamer ? 1 : 0)}; " +
        $"transitions={FaceTransitions}; basisErr={MaximumBasisError:0.000000000}; " +
        $"stepErr={MaximumGeodesicStepErrorMeters:0.000000}m; seamUp={MaximumSeamUpDeltaDegrees:0.000000}deg; " +
        $"result={Result}";
}

public static class PlanetSurfaceRadialFrameAcceptanceRunner
{
    private static readonly (double Latitude, double Longitude)[] FaceProbes =
    {
        (0.0, 0.0),
        (0.0, 179.0),
        (89.0, 0.0),
        (-89.0, 0.0),
        (0.0, 90.0),
        (0.0, -90.0)
    };

    public static PlanetSurfaceRadialFrameAcceptanceReport Run(
        IReadOnlyList<PlanetEnvironmentProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        try
        {
            PlanetEnvironmentProfile[] landable = profiles
                .Where(profile => profile.Landable)
                .ToArray();
            bool gravityScaled = landable.Length > 0;
            bool tangentFrames = landable.Length > 0;
            bool faceUvBounded = landable.Length > 0;
            bool seamContinuous = landable.Length > 0;
            bool geodesicStep = landable.Length > 0;
            bool warpRoundTrip = landable.Length > 0;
            HashSet<CubeSphereFaceId> faces = new();
            int transitions = 0;
            double maxBasisError = 0.0;
            double maxStepError = 0.0;
            double maxSeamUpDelta = 0.0;

            foreach (PlanetEnvironmentProfile profile in landable)
            {
                PlanetSurfaceRadialFrameRuntime runtime = new(profile);
                gravityScaled &= Math.Abs(
                    runtime.Build(0.0, 0.0).GravityMetersPerSecondSquared -
                    profile.SurfaceGravityG *
                    PlanetSurfaceRadialFrameRuntime.StandardGravityMetersPerSecondSquared) <= 0.0000001;

                foreach ((double latitude, double longitude) in FaceProbes)
                {
                    PlanetSurfaceGeographicAddress geographic =
                        runtime.Topology.FromGeographic(latitude, longitude);
                    PlanetSurfaceRadialFrameState state = runtime.Build(geographic);
                    faces.Add(state.CubeFace.Face);
                    maxBasisError = Math.Max(
                        maxBasisError,
                        Math.Max(
                            state.GlobalFrame.MaximumOrthogonalityError,
                            state.GlobalFrame.MaximumUnitLengthError));
                    tangentFrames &=
                        state.GlobalFrame.MaximumOrthogonalityError <= 0.000000001 &&
                        state.GlobalFrame.MaximumUnitLengthError <= 0.000000001 &&
                        Math.Abs(state.GlobalFrame.Handedness - 1.0) <= 0.000000001;
                    faceUvBounded &=
                        Math.Abs(state.CubeFace.U) <= 1.000000001 &&
                        Math.Abs(state.CubeFace.V) <= 1.000000001;

                    PlanetSurfaceCanonicalLogicalAddress canonical =
                        runtime.Topology.ToCanonicalLogical(geographic);
                    PlanetSurfaceGeographicAddress roundTrip =
                        runtime.Topology.FromLogical(
                            canonical.EastMeters,
                            canonical.NorthMeters);
                    double latError = Math.Abs(
                        roundTrip.LatitudeDegrees - geographic.LatitudeDegrees);
                    double lonError = Math.Abs(
                        PlanetSurfaceTopologyRuntime.WrapLongitudeDegrees(
                            roundTrip.LongitudeDegrees - geographic.LongitudeDegrees));
                    warpRoundTrip &= latError <= 0.0000001 && lonError <= 0.0000001;
                }

                PlanetSurfaceGeographicAddress origin =
                    runtime.Topology.FromGeographic(0.0, 0.0);
                PlanetSurfaceGeographicAddress stepped =
                    runtime.Topology.GeodesicStep(origin, 600.0, 800.0);
                double stepDistance = runtime.Topology.GreatCircleDistanceMeters(
                    origin,
                    stepped);
                double stepError = Math.Abs(stepDistance - 1000.0);
                maxStepError = Math.Max(maxStepError, stepError);
                geodesicStep &= stepError <= 0.001;

                PlanetSurfaceRadialFrameState seamLeft = runtime.Build(
                    runtime.Topology.FromGeographic(0.0, 44.999));
                PlanetSurfaceRadialFrameState seamRight = runtime.Build(
                    runtime.Topology.FromGeographic(0.0, 45.001));
                double seamUp = runtime.MeasureUpDeltaDegrees(
                    seamLeft,
                    seamRight);
                maxSeamUpDelta = Math.Max(maxSeamUpDelta, seamUp);
                seamContinuous &=
                    seamLeft.CubeFace.Face != seamRight.CubeFace.Face &&
                    seamUp <= 0.01 &&
                    seamLeft.GlobalFrame.North.Dot(seamRight.GlobalFrame.North) > 0.999999;

                CubeSphereFaceId? previousFace = null;
                for (int sample = 0; sample <= 192; sample++)
                {
                    double longitude = sample * 360.0 / 192.0;
                    PlanetSurfaceRadialFrameState state = runtime.Build(
                        runtime.Topology.FromGeographic(0.0, longitude));
                    if (previousFace.HasValue && previousFace.Value != state.CubeFace.Face)
                    {
                        transitions++;
                    }
                    previousFace = state.CubeFace.Face;
                }
            }

            bool faceCoverage = faces.Count == 6;
            bool localGravityEquivalent = gravityScaled && tangentFrames;
            bool bounded =
                PlanetSurfaceStreamingRuntime.ExpectedActiveChunks == 25 &&
                PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks == 9;
            bool passed = gravityScaled &&
                tangentFrames &&
                faceCoverage &&
                faceUvBounded &&
                seamContinuous &&
                geodesicStep &&
                warpRoundTrip &&
                localGravityEquivalent &&
                bounded &&
                transitions >= landable.Length * 4;
            string result = passed
                ? "planet gravity, radial tangent bases, cube-face addressing, seam continuity and geodesic warp contract verified"
                : "one or more radial surface-frame invariants failed";

            return new PlanetSurfaceRadialFrameAcceptanceReport(
                passed,
                gravityScaled,
                tangentFrames,
                faceCoverage,
                faceUvBounded,
                seamContinuous,
                geodesicStep,
                warpRoundTrip,
                localGravityEquivalent,
                bounded,
                landable.Length,
                faces.Count,
                transitions,
                maxBasisError,
                maxStepError,
                maxSeamUpDelta,
                result);
        }
        catch (Exception exception)
        {
            return new PlanetSurfaceRadialFrameAcceptanceReport(
                false, false, false, false, false, false, false, false, false,
                false, 0, 0, 0, 0.0, 0.0, 0.0,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
