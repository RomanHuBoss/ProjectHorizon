using System;
using System.Collections.Generic;
using System.Globalization;

public sealed record PlanetSurfaceFrameAcceptanceReport(
    bool Passed,
    int RebaseCount,
    double MaximumLocalMeters,
    int TraversalSamples,
    bool LogicalContinuity,
    bool ChunkIdentityStable,
    bool ColdRestoreStable,
    bool PlanetResetStable,
    bool GeodesicStable)
{
    public string BuildHudLine() =>
        $"{(Passed ? "PASS" : "FAIL")} rebases={RebaseCount}; " +
        $"local<={MaximumLocalMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
        $"logical={(LogicalContinuity ? 1 : 0)}; chunks={(ChunkIdentityStable ? 1 : 0)}; " +
        $"restore={(ColdRestoreStable ? 1 : 0)}; planetReset={(PlanetResetStable ? 1 : 0)}";

    public string BuildOutputLine() =>
        $"TASK-162 planet-global surface frame acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"rebases={RebaseCount}; traversalSamples={TraversalSamples}; " +
        $"maxLocal={MaximumLocalMeters.ToString("0.000", CultureInfo.InvariantCulture)}m; " +
        $"threshold={PlanetSurfaceFrameRuntime.RebaseThresholdMeters:0}m; " +
        $"cell={PlanetSurfaceFrameRuntime.RebaseCellSizeMeters:0}m; " +
        $"logicalContinuity={(LogicalContinuity ? 1 : 0)}; " +
        $"chunkIdentity={(ChunkIdentityStable ? 1 : 0)}; " +
        $"coldRestore={(ColdRestoreStable ? 1 : 0)}; " +
        $"planetReset={(PlanetResetStable ? 1 : 0)}; geodesic={(GeodesicStable ? 1 : 0)}; " +
        "persistence=logical-xz/no-schema-bump; frame=bounded-local.";
}

public static class PlanetSurfaceFrameAcceptanceRunner
{
    public static PlanetSurfaceFrameAcceptanceReport Run()
    {
        PlanetSurfaceFrameRuntime frame = new();
        frame.Reset("acceptance.planet.alpha");

        List<(double East, double North)> route = new();
        for (int step = 0; step <= 48; step++)
        {
            double t = step / 48.0;
            route.Add((
                -72_000.0 + 151_000.0 * t,
                58_000.0 * Math.Sin(t * Math.PI * 3.25) - 17_000.0));
        }

        bool logicalContinuity = true;
        bool chunkIdentity = true;
        double maxLocal = 0.0;
        int rebases = 0;
        foreach ((double expectedEast, double expectedNorth) in route)
        {
            (double localEast, double localNorth) = frame.ToLocal(
                expectedEast,
                expectedNorth);
            PlanetSurfaceFrameRebase rebase = frame.PlanRebase(
                localEast,
                localNorth);
            if (rebase.Required)
            {
                frame.Apply(rebase);
                rebases++;
                (localEast, localNorth) = frame.ToLocal(
                    expectedEast,
                    expectedNorth);
            }

            maxLocal = Math.Max(
                maxLocal,
                Math.Max(Math.Abs(localEast), Math.Abs(localNorth)));
            PlanetSurfaceLogicalPosition roundTrip = frame.ToLogical(
                localEast,
                0.0,
                localNorth);
            logicalContinuity &=
                Math.Abs(roundTrip.EastMeters - expectedEast) < 0.000001 &&
                Math.Abs(roundTrip.NorthMeters - expectedNorth) < 0.000001;

            PlanetSurfaceChunkCoordinate expectedChunk =
                PlanetSurfaceStreamingRuntime.WorldToChunk(
                    expectedEast,
                    expectedNorth);
            PlanetSurfaceChunkCoordinate roundTripChunk =
                PlanetSurfaceStreamingRuntime.WorldToChunk(
                    roundTrip.EastMeters,
                    roundTrip.NorthMeters);
            chunkIdentity &= expectedChunk == roundTripChunk;
        }

        (double savedEast, double savedNorth) = route[^1];
        PlanetSurfaceFrameRuntime restored = new();
        restored.RestoreAtLogicalPosition(
            "acceptance.planet.alpha",
            savedEast,
            savedNorth);
        (double restoredLocalEast, double restoredLocalNorth) = restored.ToLocal(
            savedEast,
            savedNorth);
        PlanetSurfaceLogicalPosition restoredRoundTrip = restored.ToLogical(
            restoredLocalEast,
            0.0,
            restoredLocalNorth);
        bool coldRestore =
            Math.Abs(restoredRoundTrip.EastMeters - savedEast) < 0.000001 &&
            Math.Abs(restoredRoundTrip.NorthMeters - savedNorth) < 0.000001 &&
            Math.Abs(restoredLocalEast) <= PlanetSurfaceFrameRuntime.LocalCoordinateToleranceMeters &&
            Math.Abs(restoredLocalNorth) <= PlanetSurfaceFrameRuntime.LocalCoordinateToleranceMeters;

        restored.Reset("acceptance.planet.beta");
        bool planetReset =
            restored.PlanetId == "acceptance.planet.beta" &&
            restored.OriginEastMeters == 0.0 &&
            restored.OriginNorthMeters == 0.0 &&
            restored.RebaseCount == 0;

        PlanetSurfaceGeodesicAddress geodesic =
            PlanetSurfaceStreamingRuntime.BuildGeodesicAddress(
                42.0,
                savedEast,
                savedNorth);
        bool geodesicStable =
            double.IsFinite(geodesic.LatitudeDegrees) &&
            double.IsFinite(geodesic.LongitudeDegrees) &&
            geodesic.LatitudeDegrees is >= -90.0 and <= 90.0 &&
            geodesic.LongitudeDegrees is >= -180.0 and <= 180.0 &&
            geodesic.SurfaceDistanceMeters > 0.0;

        bool passed =
            rebases >= 8 &&
            maxLocal <= PlanetSurfaceFrameRuntime.LocalCoordinateToleranceMeters &&
            logicalContinuity &&
            chunkIdentity &&
            coldRestore &&
            planetReset &&
            geodesicStable;

        return new PlanetSurfaceFrameAcceptanceReport(
            passed,
            rebases,
            maxLocal,
            route.Count,
            logicalContinuity,
            chunkIdentity,
            coldRestore,
            planetReset,
            geodesicStable);
    }
}
