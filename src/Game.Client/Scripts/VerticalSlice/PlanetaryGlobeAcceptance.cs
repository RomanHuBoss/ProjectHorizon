using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetaryGlobeAcceptanceReport(
    bool Passed,
    bool SphericalAddressing,
    bool CircumnavigationWrap,
    bool PoleNormalization,
    bool GeodesicSymmetry,
    bool CubeSphereSeams,
    bool CurvatureScale,
    bool DetailedGlobePrepared,
    bool SingleDetailedGlobe,
    bool GameplayStreamerBounded,
    int Planets,
    int GlobeFaces,
    int GlobeVertices,
    int GlobeTriangles,
    double MaximumWrapError,
    double MaximumGeodesicError,
    double MinimumCurvatureSag,
    double MaximumCurvatureSag,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS planets={Planets}, globe={GlobeFaces}/6, wrap={MaximumWrapError:0.000000}°, sag={MinimumCurvatureSag:0.00}..{MaximumCurvatureSag:0.00}m"
        : $"FAIL — {Result}";

    public string BuildOutputLine() =>
        $"TASK-168 planetary globe and geodesy acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"planets={Planets}; sphericalAddressing={(SphericalAddressing ? 1 : 0)}; " +
        $"circumnavigation={(CircumnavigationWrap ? 1 : 0)}; poleNormalization={(PoleNormalization ? 1 : 0)}; " +
        $"geodesicSymmetry={(GeodesicSymmetry ? 1 : 0)}; cubeSphereSeams={(CubeSphereSeams ? 1 : 0)}; " +
        $"curvature={(CurvatureScale ? 1 : 0)}; detailedGlobe={(DetailedGlobePrepared ? 1 : 0)}; " +
        $"singleDetailedGlobe={(SingleDetailedGlobe ? 1 : 0)}; boundedGameplayStreamer={(GameplayStreamerBounded ? 1 : 0)}; " +
        $"faces={GlobeFaces}; vertices={GlobeVertices}; triangles={GlobeTriangles}; " +
        $"wrapErr={MaximumWrapError:0.000000}deg; geodesicErr={MaximumGeodesicError:0.000000}m; " +
        $"sag={MinimumCurvatureSag:0.000}..{MaximumCurvatureSag:0.000}m; result={Result}";
}

public static class PlanetaryGlobeAcceptanceRunner
{
    public static PlanetaryGlobeAcceptanceReport Run(
        IReadOnlyList<PlanetEnvironmentProfile> profiles,
        StarSystemSimulationNode liveNode,
        string currentPlanetId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(liveNode);
        try
        {
            PlanetEnvironmentProfile[] landable = profiles
                .Where(profile => profile.Landable)
                .ToArray();
            bool sphericalAddressing = landable.Length > 0;
            bool circumnavigation = true;
            bool poleNormalization = true;
            bool geodesicSymmetry = true;
            bool curvature = true;
            double maximumWrapError = 0.0;
            double maximumGeodesicError = 0.0;
            double minimumSag = double.MaxValue;
            double maximumSag = 0.0;

            foreach (PlanetEnvironmentProfile profile in landable)
            {
                PlanetSurfaceTopologyRuntime topology = new(profile.RadiusKm);
                PlanetSurfaceGeographicAddress origin = topology.FromLogical(0.0, 0.0);
                PlanetSurfaceGeographicAddress wrapped = topology.FromLogical(
                    topology.CircumferenceMeters,
                    0.0);
                double longitudeError = Math.Abs(
                    PlanetSurfaceTopologyRuntime.WrapLongitudeDegrees(
                        wrapped.LongitudeDegrees - origin.LongitudeDegrees));
                double latitudeError = Math.Abs(
                    wrapped.LatitudeDegrees - origin.LatitudeDegrees);
                maximumWrapError = Math.Max(
                    maximumWrapError,
                    Math.Max(longitudeError, latitudeError));
                circumnavigation &= longitudeError <= 0.000001 &&
                    latitudeError <= 0.000001;

                PlanetSurfaceGeographicAddress pole = topology.FromLogical(
                    topology.CircumferenceMeters * 0.19,
                    topology.CircumferenceMeters * 0.74);
                poleNormalization &=
                    pole.LatitudeDegrees is >= -90.0 and <= 90.0 &&
                    pole.LongitudeDegrees is >= -180.0 and < 180.0;

                double aToB = topology.GreatCircleDistanceMeters(
                    12_345.0,
                    -8_765.0,
                    -33_210.0,
                    27_540.0);
                double bToA = topology.GreatCircleDistanceMeters(
                    -33_210.0,
                    27_540.0,
                    12_345.0,
                    -8_765.0);
                double symmetryError = Math.Abs(aToB - bToA);
                maximumGeodesicError = Math.Max(
                    maximumGeodesicError,
                    symmetryError);
                geodesicSymmetry &= symmetryError <= 0.000001 &&
                    aToB >= 0.0 &&
                    aToB <= Math.PI * topology.RadiusMeters + 0.001;

                double sag = topology.TangentSagMeters(420.0);
                minimumSag = Math.Min(minimumSag, sag);
                maximumSag = Math.Max(maximumSag, sag);
                curvature &= sag > 0.25 && sag < 6.0;
            }

            CubeSphereBuildData cube = CubeSphereMeshBuilder.Build(
                DetailedPlanetGlobeNode.FaceResolution,
                8.0f,
                0.32f,
                0.21f,
                20260816);
            bool cubeSphereSeams =
                cube.Faces.Count == 6 &&
                cube.SeamComparisons == cube.ExpectedSeamComparisons &&
                cube.MaximumSeamPositionError <= 0.0001f &&
                cube.MaximumSeamNormalError <= 0.0001f;

            DetailedPlanetGlobeDiagnostics globe =
                liveNode.CreateDetailedGlobeDiagnostics();
            bool detailedPrepared =
                liveNode.PreparedDetailedGlobeCount == 1 &&
                string.Equals(
                    globe.PlanetId,
                    currentPlanetId,
                    StringComparison.Ordinal) &&
                globe.FaceCount == 6 &&
                globe.Vertices > 0 &&
                globe.Triangles > 0 &&
                globe.SeamComparisons == globe.ExpectedSeamComparisons &&
                globe.MaximumSeamPositionError <= 0.0001f &&
                globe.MaximumSeamNormalError <= 0.0001f;
            bool singleDetailed = liveNode.PreparedDetailedGlobeCount == 1;
            bool boundedGameplayStreamer =
                PlanetSurfaceStreamingRuntime.ExpectedActiveChunks == 25 &&
                PlanetSurfaceStreamingRuntime.ExpectedCollisionChunks == 9;

            bool passed = sphericalAddressing &&
                circumnavigation &&
                poleNormalization &&
                geodesicSymmetry &&
                cubeSphereSeams &&
                curvature &&
                detailedPrepared &&
                singleDetailed &&
                boundedGameplayStreamer;
            string result = passed
                ? "global spherical address, seamless cube-sphere globe, tangent curvature and single detailed-orbit planet verified"
                : "one or more planetary globe/geodesy invariants failed";

            return new PlanetaryGlobeAcceptanceReport(
                passed,
                sphericalAddressing,
                circumnavigation,
                poleNormalization,
                geodesicSymmetry,
                cubeSphereSeams,
                curvature,
                detailedPrepared,
                singleDetailed,
                boundedGameplayStreamer,
                landable.Length,
                globe.FaceCount,
                globe.Vertices,
                globe.Triangles,
                maximumWrapError,
                maximumGeodesicError,
                minimumSag == double.MaxValue ? 0.0 : minimumSag,
                maximumSag,
                result);
        }
        catch (Exception exception)
        {
            return new PlanetaryGlobeAcceptanceReport(
                false, false, false, false, false, false, false, false, false, false,
                0, 0, 0, 0, 0.0, 0.0, 0.0, 0.0,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
