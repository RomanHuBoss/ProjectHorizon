using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public readonly record struct PlanetSurfaceChunkCoordinate(int X, int Z);

public sealed record PlanetSurfaceStreamingSpec(
    PlanetSurfaceChunkCoordinate Coordinate,
    int LodLevel,
    int VisualResolution,
    bool GenerateCollision,
    TerrainEdgeStitchMask StitchMask,
    TerrainEdgeStitchMask SkirtMask);

public readonly record struct PlanetSurfaceGeodesicAddress(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double SurfaceDistanceMeters,
    double CircumferenceMeters);

public static class PlanetSurfaceStreamingRuntime
{
    public const int ActiveRadius = 2;
    public const int HighDetailRadius = 1;
    public const int CollisionRadius = 1;
    public const int HighDetailResolution = 33;
    public const int LowDetailResolution = 17;
    public const int CollisionResolution = 33;
    public const double ChunkSizeMeters = 32.0;
    public const double SwitchHysteresisMeters = 3.0;
    public const double SkirtDepthMeters = 2.0;
    // Tangent-plane traversal envelope before a future floating-origin/cube-sphere
    // promotion. Residency remains bounded regardless of this logical extent.
    public const double NavigationTraversalExtentMeters = 8_192.0;
    public const int ExpectedActiveChunks = 25;
    public const int ExpectedHighDetailChunks = 9;
    public const int ExpectedLowDetailChunks = 16;
    public const int ExpectedCollisionChunks = 9;

    public static PlanetSurfaceChunkCoordinate WorldToChunk(
        double x,
        double z)
    {
        double half = ChunkSizeMeters * 0.5;
        int chunkX = (int)Math.Floor((x + half) / ChunkSizeMeters);
        int chunkZ = (int)Math.Floor((z + half) / ChunkSizeMeters);
        return new PlanetSurfaceChunkCoordinate(chunkX, chunkZ);
    }

    public static int ExpectedRetainedChunkCount(
        PlanetSurfaceChunkCoordinate previous,
        PlanetSurfaceChunkCoordinate next)
    {
        int side = (ActiveRadius * 2) + 1;
        int deltaX = Math.Abs(next.X - previous.X);
        int deltaZ = Math.Abs(next.Z - previous.Z);
        return Math.Max(0, side - deltaX) *
            Math.Max(0, side - deltaZ);
    }

    public static IReadOnlyList<PlanetSurfaceStreamingSpec> BuildPlan(
        PlanetSurfaceChunkCoordinate center)
    {
        Dictionary<PlanetSurfaceChunkCoordinate, PlanetSurfaceStreamingSpec> specs =
            new();

        for (int offsetZ = -ActiveRadius; offsetZ <= ActiveRadius; offsetZ++)
        {
            for (int offsetX = -ActiveRadius; offsetX <= ActiveRadius; offsetX++)
            {
                PlanetSurfaceChunkCoordinate coordinate = new(
                    center.X + offsetX,
                    center.Z + offsetZ);
                int distance = Math.Max(Math.Abs(offsetX), Math.Abs(offsetZ));
                int lodLevel = distance <= HighDetailRadius ? 0 : 1;
                specs[coordinate] = new PlanetSurfaceStreamingSpec(
                    coordinate,
                    lodLevel,
                    lodLevel == 0 ? HighDetailResolution : LowDetailResolution,
                    distance <= CollisionRadius,
                    TerrainEdgeStitchMask.None,
                    TerrainEdgeStitchMask.None);
            }
        }

        foreach (PlanetSurfaceChunkCoordinate coordinate in specs.Keys.ToArray())
        {
            PlanetSurfaceStreamingSpec spec = specs[coordinate];
            TerrainEdgeStitchMask stitch = TerrainEdgeStitchMask.None;
            TerrainEdgeStitchMask skirt = TerrainEdgeStitchMask.None;
            AddStitch(specs, coordinate.X, coordinate.Z - 1, spec.LodLevel,
                TerrainEdgeStitchMask.North, ref stitch);
            AddStitch(specs, coordinate.X + 1, coordinate.Z, spec.LodLevel,
                TerrainEdgeStitchMask.East, ref stitch);
            AddStitch(specs, coordinate.X, coordinate.Z + 1, spec.LodLevel,
                TerrainEdgeStitchMask.South, ref stitch);
            AddStitch(specs, coordinate.X - 1, coordinate.Z, spec.LodLevel,
                TerrainEdgeStitchMask.West, ref stitch);

            if (!specs.ContainsKey(new PlanetSurfaceChunkCoordinate(
                coordinate.X, coordinate.Z - 1)))
            {
                skirt |= TerrainEdgeStitchMask.North;
            }
            if (!specs.ContainsKey(new PlanetSurfaceChunkCoordinate(
                coordinate.X + 1, coordinate.Z)))
            {
                skirt |= TerrainEdgeStitchMask.East;
            }
            if (!specs.ContainsKey(new PlanetSurfaceChunkCoordinate(
                coordinate.X, coordinate.Z + 1)))
            {
                skirt |= TerrainEdgeStitchMask.South;
            }
            if (!specs.ContainsKey(new PlanetSurfaceChunkCoordinate(
                coordinate.X - 1, coordinate.Z)))
            {
                skirt |= TerrainEdgeStitchMask.West;
            }

            specs[coordinate] = spec with
            {
                StitchMask = stitch,
                SkirtMask = skirt
            };
        }

        return specs.Values
            .OrderBy(spec => Math.Max(
                Math.Abs(spec.Coordinate.X - center.X),
                Math.Abs(spec.Coordinate.Z - center.Z)))
            .ThenBy(spec => spec.Coordinate.Z)
            .ThenBy(spec => spec.Coordinate.X)
            .ToArray();
    }

    public static PlanetSurfaceGeodesicAddress BuildGeodesicAddress(
        double planetRadiusKm,
        double eastMeters,
        double northMeters)
    {
        PlanetSurfaceTopologyRuntime topology = new(planetRadiusKm);
        PlanetSurfaceGeographicAddress address = topology.FromLogical(
            eastMeters,
            northMeters);
        // Keep SurfaceDistanceMeters as the unwrapped logical traversal distance
        // for backward-compatible streamer diagnostics; latitude/longitude are now
        // globally periodic and pole-safe through TASK-168 spherical topology.
        double distance = Math.Sqrt(
            eastMeters * eastMeters + northMeters * northMeters);
        return new PlanetSurfaceGeodesicAddress(
            address.LatitudeDegrees,
            address.LongitudeDegrees,
            distance,
            topology.CircumferenceMeters);
    }

    public static string BuildChunkSignature(
        PlanetSurfaceTerrainProfile profile,
        PlanetSurfaceChunkCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double centerX = coordinate.X * ChunkSizeMeters;
        double centerZ = coordinate.Z * ChunkSizeMeters;
        double half = ChunkSizeMeters * 0.5;
        (double X, double Z)[] probes =
        {
            (-half, -half), (0.0, -half), (half, -half),
            (-half, 0.0), (0.0, 0.0), (half, 0.0),
            (-half, half), (0.0, half), (half, half)
        };
        return string.Join(
            "|",
            probes.Select(probe =>
                PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile,
                    centerX + probe.X,
                    centerZ + probe.Z)
                .ToString("0.000", CultureInfo.InvariantCulture)));
    }

    public static double MeasureSharedEdgeError(
        PlanetSurfaceTerrainProfile profile,
        PlanetSurfaceChunkCoordinate left,
        PlanetSurfaceChunkCoordinate right)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (right.X != left.X + 1 || right.Z != left.Z)
        {
            throw new ArgumentException(
                "Shared-edge validation expects horizontal adjacent chunks.");
        }

        double leftBoundaryX = (left.X * ChunkSizeMeters) +
            (ChunkSizeMeters * 0.5);
        double rightBoundaryX = (right.X * ChunkSizeMeters) -
            (ChunkSizeMeters * 0.5);
        double centerZ = left.Z * ChunkSizeMeters;
        double half = ChunkSizeMeters * 0.5;
        double highStep = ChunkSizeMeters / (HighDetailResolution - 1);
        double maximumError = Math.Abs(leftBoundaryX - rightBoundaryX);

        // A LOD0 edge has 33 samples and the neighboring LOD1 edge has 17.
        // TASK-B stitching moves every odd high-detail midpoint onto the linear
        // segment between the two even samples. Compare that stitched height
        // with the exact low-detail interpolation at the same world position.
        for (int sample = 0; sample < HighDetailResolution; sample++)
        {
            double z = centerZ - half + sample * highStep;
            double rightHeight = PlanetSurfaceTerrainRuntime.SampleHeight(
                profile, rightBoundaryX, z);
            double stitchedLeftHeight;
            double lowInterpolatedHeight;
            if ((sample & 1) == 0)
            {
                stitchedLeftHeight = PlanetSurfaceTerrainRuntime.SampleHeight(
                    profile, leftBoundaryX, z);
                lowInterpolatedHeight = rightHeight;
            }
            else
            {
                double beforeZ = z - highStep;
                double afterZ = z + highStep;
                stitchedLeftHeight = 0.5 * (
                    PlanetSurfaceTerrainRuntime.SampleHeight(
                        profile, leftBoundaryX, beforeZ) +
                    PlanetSurfaceTerrainRuntime.SampleHeight(
                        profile, leftBoundaryX, afterZ));
                lowInterpolatedHeight = 0.5 * (
                    PlanetSurfaceTerrainRuntime.SampleHeight(
                        profile, rightBoundaryX, beforeZ) +
                    PlanetSurfaceTerrainRuntime.SampleHeight(
                        profile, rightBoundaryX, afterZ));
            }
            maximumError = Math.Max(
                maximumError,
                Math.Abs(stitchedLeftHeight - lowInterpolatedHeight));
        }
        return maximumError;
    }

    private static void AddStitch(
        IReadOnlyDictionary<PlanetSurfaceChunkCoordinate, PlanetSurfaceStreamingSpec> specs,
        int neighborX,
        int neighborZ,
        int lodLevel,
        TerrainEdgeStitchMask edge,
        ref TerrainEdgeStitchMask mask)
    {
        if (specs.TryGetValue(
                new PlanetSurfaceChunkCoordinate(neighborX, neighborZ),
                out PlanetSurfaceStreamingSpec? neighbor) &&
            neighbor is not null &&
            neighbor.LodLevel > lodLevel)
        {
            mask |= edge;
        }
    }
}
