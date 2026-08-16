using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed record PlanetSurfaceCurvedCollisionAcceptanceReport(
    bool Passed,
    int Planets,
    bool Curvature,
    bool Normals,
    bool RebaseContinuity,
    int FacesCovered,
    double MaximumSagMeters,
    double MaximumRoundTripErrorMeters);

public static class PlanetSurfaceCurvedCollisionAcceptanceRunner
{
    public const double RebaseRoundTripToleranceMeters = 0.03;

    public static PlanetSurfaceCurvedCollisionAcceptanceReport Run(
        IReadOnlyList<PlanetEnvironmentProfile> profiles)
    {
        int planets = 0;
        bool curvature = true;
        bool normals = true;
        bool continuity = true;
        HashSet<CubeSphereFaceId> faces = new();
        double maxSag = 0.0;
        double maxError = 0.0;

        foreach (PlanetEnvironmentProfile profile in profiles.Where(p => p.Landable))
        {
            planets++;
            double radius = Math.Max(
                PlanetSurfaceTopologyRuntime.MinimumRadiusMeters,
                profile.RadiusKm * 1000.0);
            PlanetSurfaceRadialFrameRuntime radial = new(profile);
            PlanetSurfacePhysicalFrameRuntime physical = new(radial);
            PlanetSurfaceTerrainProfile terrain = PlanetSurfaceTerrainRuntime.BuildProfile(
            profile,
            profile.Seed == long.MinValue ? long.MaxValue : Math.Max(1L, Math.Abs(profile.Seed)));

            foreach ((double lat, double lon) in new[]
                     {
                         (0.0, 0.0), (0.0, 90.0), (0.0, 179.0),
                         (89.0, 0.0), (-89.0, 0.0), (0.0, -90.0)
                     })
            {
                PlanetSurfaceCanonicalLogicalAddress address = radial.WarpTarget(lat, lon);
                PlanetSurfacePhysicalFrameState state = physical.Build(
                    address.EastMeters, address.NorthMeters);
                faces.Add(state.Radial.CubeFace.Face);

                PlanetSurfaceCurvedPatchDescriptor patch = new(
                    radius, address.EastMeters, address.NorthMeters);
                double sag64 = patch.TangentSagMeters(
                    address.EastMeters + 64.0,
                    address.NorthMeters);
                maxSag = Math.Max(maxSag, sag64);
                curvature &= sag64 > 0.0 && sag64 < 1.0;

                Vector3 up = patch.SurfaceUpLocal(
                    address.EastMeters + 48.0,
                    address.NorthMeters + 32.0);
                Vector3 terrainNormal = patch.TerrainNormalLocal(
                    terrain,
                    address.EastMeters + 12.0,
                    address.NorthMeters - 9.0);
                normals &= Math.Abs(up.Length() - 1.0f) <= 0.0001f &&
                    Math.Abs(terrainNormal.Length() - 1.0f) <= 0.0001f &&
                    up.Y > 0.99f && terrainNormal.Dot(up) > 0.45f;

                PlanetSurfacePhysicalFrameState next = physical.Build(
                    address.EastMeters + 4096.0,
                    address.NorthMeters);
                PlanetSurfaceCurvedPatchDescriptor nextPatch = new(
                    radius, address.EastMeters + 4096.0, address.NorthMeters);
                Vector3 logical = patch.ToGameplayLogicalPoint(
                    address.EastMeters + 73.25,
                    5.5,
                    address.NorthMeters - 28.75);
                Vector3 world = state.GameplayTransform * logical;
                Vector3 mapped = PlanetSurfacePhysicalFrameRuntime.MapCurvedPoint(
                    state.GameplayTransform,
                    next.GameplayTransform,
                    world,
                    patch,
                    nextPatch);
                Vector3 restored = PlanetSurfacePhysicalFrameRuntime.MapCurvedPoint(
                    next.GameplayTransform,
                    state.GameplayTransform,
                    mapped,
                    nextPatch,
                    patch);
                double error = restored.DistanceTo(world);
                maxError = Math.Max(maxError, error);
                continuity &= error <= RebaseRoundTripToleranceMeters;
            }
        }

        bool passed = planets > 0 && curvature && normals && continuity &&
            faces.Count == 6;
        return new PlanetSurfaceCurvedCollisionAcceptanceReport(
            passed,
            planets,
            curvature,
            normals,
            continuity,
            faces.Count,
            maxSag,
            maxError);
    }
}
