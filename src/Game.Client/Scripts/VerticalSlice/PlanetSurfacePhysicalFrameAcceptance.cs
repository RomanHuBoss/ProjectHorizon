using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed record PlanetSurfacePhysicalFrameAcceptanceReport(
    bool Passed,
    int Planets,
    bool FrameTransforms,
    bool WorldLogicalRoundTrip,
    bool VectorRemap,
    bool SeamHandoff,
    int FacesCovered,
    double MaximumPointRoundTripError,
    double MaximumVectorRoundTripError);

public static class PlanetSurfacePhysicalFrameAcceptanceRunner
{
    // World transforms use Godot.Vector3/Transform3D float storage. At planet-
    // scale logical addresses (~10^5 m), millimetre precision is not a valid
    // invariant; centimetre-class round-trip is the correct bounded budget.
    public const double PointRoundTripToleranceMeters = 0.02;

    public static PlanetSurfacePhysicalFrameAcceptanceReport Run(
        IReadOnlyList<PlanetEnvironmentProfile> profiles)
    {
        int planets = 0;
        bool frameTransforms = true;
        bool worldLogicalRoundTrip = true;
        bool vectorRemap = true;
        bool seamHandoff = true;
        HashSet<CubeSphereFaceId> faces = new();
        double maxPointError = 0.0;
        double maxVectorError = 0.0;

        foreach (PlanetEnvironmentProfile profile in profiles.Where(p => p.Landable))
        {
            planets++;
            PlanetSurfaceRadialFrameRuntime radial = new(profile);
            PlanetSurfacePhysicalFrameRuntime physical = new(radial);
            (double Lat, double Lon)[] probes =
            {
                (0.0, 0.0), (0.0, 90.0), (0.0, 179.0),
                (89.0, 0.0), (-89.0, 0.0), (0.0, -90.0)
            };
            foreach ((double lat, double lon) in probes)
            {
                PlanetSurfaceCanonicalLogicalAddress logical = radial.WarpTarget(lat, lon);
                PlanetSurfacePhysicalFrameState state = physical.Build(
                    logical.EastMeters,
                    logical.NorthMeters);
                faces.Add(state.Radial.CubeFace.Face);
                frameTransforms &=
                    Math.Abs(state.SurfaceBasis.X.Length() - 1.0f) <= 0.0001f &&
                    Math.Abs(state.SurfaceBasis.Y.Length() - 1.0f) <= 0.0001f &&
                    Math.Abs(state.SurfaceBasis.Z.Length() - 1.0f) <= 0.0001f &&
                    Math.Abs(state.SurfaceBasis.X.Dot(state.SurfaceBasis.Y)) <= 0.0001f &&
                    Math.Abs(state.SurfaceBasis.X.Dot(state.SurfaceBasis.Z)) <= 0.0001f &&
                    Math.Abs(state.SurfaceBasis.Y.Dot(state.SurfaceBasis.Z)) <= 0.0001f &&
                    state.SurfaceBasis.Y.Normalized().Dot(state.WorldUp) >= 0.99999f;

                Vector3 sampleLogical = new(
                    (float)(logical.EastMeters + 17.25),
                    3.75f,
                    (float)(logical.NorthMeters - 11.5));
                Vector3 world = state.LogicalToWorld(sampleLogical);
                Vector3 restored = state.WorldToLogical(world);
                double pointError = restored.DistanceTo(sampleLogical);
                maxPointError = Math.Max(maxPointError, pointError);
                worldLogicalRoundTrip &=
                    pointError <= PointRoundTripToleranceMeters;

                Vector3 localVelocity = new(2.5f, -1.25f, 4.0f);
                Vector3 worldVelocity = state.SurfaceBasis * localVelocity;
                Vector3 restoredVelocity = state.SurfaceBasis.Inverse() * worldVelocity;
                double vectorError = restoredVelocity.DistanceTo(localVelocity);
                maxVectorError = Math.Max(maxVectorError, vectorError);
                vectorRemap &= vectorError <= 0.0001;
            }

            PlanetSurfaceCanonicalLogicalAddress leftLogical = radial.WarpTarget(0.0, 44.999);
            PlanetSurfaceCanonicalLogicalAddress rightLogical = radial.WarpTarget(0.0, 45.001);
            PlanetSurfacePhysicalFrameState left = physical.Build(
                leftLogical.EastMeters,
                leftLogical.NorthMeters);
            PlanetSurfacePhysicalFrameState right = physical.Build(
                rightLogical.EastMeters,
                rightLogical.NorthMeters);
            seamHandoff &=
                left.Radial.CubeFace.Face != right.Radial.CubeFace.Face &&
                left.WorldUp.Dot(right.WorldUp) >= 0.99999f &&
                PlanetSurfacePhysicalFrameRuntime.MaximumAxisErrorDegrees(
                    left.SurfaceBasis,
                    right.SurfaceBasis) < 0.01;
        }

        bool passed = planets > 0 && frameTransforms && worldLogicalRoundTrip &&
            vectorRemap && seamHandoff && faces.Count == 6;
        return new PlanetSurfacePhysicalFrameAcceptanceReport(
            passed,
            planets,
            frameTransforms,
            worldLogicalRoundTrip,
            vectorRemap,
            seamHandoff,
            faces.Count,
            maxPointError,
            maxVectorError);
    }
}
