using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public enum WorldStreamingTravelMode
{
    OnFoot = 0,
    GroundVehicle = 1,
    AtmosphericFlight = 2
}

public enum WorldStreamingRegionDetail
{
    Full = 0,
    Simplified = 1,
    Preload = 2
}

public enum WorldStreamingPriority
{
    PlayerRegion = 1,
    DirectionOfMovement = 2,
    CollisionRegion = 3,
    VisibleRegion = 4,
    FarRegion = 5,
    PreGeneration = 6
}

public enum WorldStreamingFrameBudgetMode
{
    Regular = 0,
    ForcedPreload = 1,
    LoadingScreen = 2
}

public readonly record struct WorldStreamingRegionCoordinate(int X, int Z);

public readonly record struct WorldStreamingObserverSample(
    double EastMeters,
    double NorthMeters,
    double VelocityEastMetersPerSecond,
    double VelocityNorthMetersPerSecond,
    WorldStreamingTravelMode TravelMode);

public sealed record WorldStreamingRegionPlan(
    WorldStreamingRegionCoordinate Coordinate,
    WorldStreamingRegionDetail Detail,
    WorldStreamingPriority Priority,
    double DistanceMeters,
    double ForwardScore);

public sealed record WorldStreamingPlan(
    WorldStreamingObserverSample Observer,
    double FullDetailRadiusMeters,
    double SimplifiedRadiusMeters,
    IReadOnlyList<WorldStreamingRegionPlan> Regions)
{
    public int FullCount => Regions.Count(region =>
        region.Detail == WorldStreamingRegionDetail.Full);
    public int SimplifiedCount => Regions.Count(region =>
        region.Detail == WorldStreamingRegionDetail.Simplified);
    public int PreloadCount => Regions.Count(region =>
        region.Detail == WorldStreamingRegionDetail.Preload);
}

public static class WorldStreamingRuntime
{
    public const double RegionSizeMeters = 1_000.0;
    public const double OnFootFullDetailRadiusMeters = 2_000.0;
    public const double GroundVehicleFullDetailRadiusMeters = 5_000.0;
    public const double AtmosphericFlightFullDetailRadiusMeters = 15_000.0;
    public const double SimplifiedRadiusMultiplier = 1.50;
    public const double PreloadAheadMeters = 3_000.0;
    public const double CollisionPriorityRadiusMeters = 2_000.0;
    public const double RegularMainThreadBudgetMilliseconds = 2.0;
    public const double ForcedPreloadMainThreadBudgetMilliseconds = 5.0;
    public const double LoadingScreenMainThreadBudgetMilliseconds = 10.0;
    public const double ReplanIntervalSeconds = 0.25;

    public static double ResolveFullDetailRadiusMeters(
        WorldStreamingTravelMode travelMode) => travelMode switch
    {
        WorldStreamingTravelMode.OnFoot => OnFootFullDetailRadiusMeters,
        WorldStreamingTravelMode.GroundVehicle => GroundVehicleFullDetailRadiusMeters,
        WorldStreamingTravelMode.AtmosphericFlight => AtmosphericFlightFullDetailRadiusMeters,
        _ => OnFootFullDetailRadiusMeters
    };

    public static double ResolveMainThreadBudgetMilliseconds(
        WorldStreamingFrameBudgetMode mode) => mode switch
    {
        WorldStreamingFrameBudgetMode.Regular =>
            RegularMainThreadBudgetMilliseconds,
        WorldStreamingFrameBudgetMode.ForcedPreload =>
            ForcedPreloadMainThreadBudgetMilliseconds,
        WorldStreamingFrameBudgetMode.LoadingScreen =>
            LoadingScreenMainThreadBudgetMilliseconds,
        _ => RegularMainThreadBudgetMilliseconds
    };

    public static int ResolveWorkerCount(int logicalProcessorCount) =>
        Math.Max(1, Math.Min(4, logicalProcessorCount - 2));

    public static WorldStreamingRegionCoordinate WorldToRegion(
        double eastMeters,
        double northMeters) => new(
            (int)Math.Floor((eastMeters + RegionSizeMeters * 0.5) /
                RegionSizeMeters),
            (int)Math.Floor((northMeters + RegionSizeMeters * 0.5) /
                RegionSizeMeters));

    public static WorldStreamingPlan BuildPlan(
        WorldStreamingObserverSample observer,
        CancellationToken cancellationToken = default)
    {
        double fullRadius = ResolveFullDetailRadiusMeters(observer.TravelMode);
        double simplifiedRadius = fullRadius * SimplifiedRadiusMultiplier;
        double planningRadius = simplifiedRadius + PreloadAheadMeters;
        WorldStreamingRegionCoordinate center = WorldToRegion(
            observer.EastMeters,
            observer.NorthMeters);
        int cellRadius = (int)Math.Ceiling(planningRadius / RegionSizeMeters) + 1;

        double speed = Math.Sqrt(
            observer.VelocityEastMetersPerSecond * observer.VelocityEastMetersPerSecond +
            observer.VelocityNorthMetersPerSecond * observer.VelocityNorthMetersPerSecond);
        double directionEast = speed > 0.25
            ? observer.VelocityEastMetersPerSecond / speed
            : 0.0;
        double directionNorth = speed > 0.25
            ? observer.VelocityNorthMetersPerSecond / speed
            : 0.0;

        List<WorldStreamingRegionPlan> regions = new();
        for (int dz = -cellRadius; dz <= cellRadius; dz++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                WorldStreamingRegionCoordinate coordinate = new(
                    center.X + dx,
                    center.Z + dz);
                double centerEast = coordinate.X * RegionSizeMeters;
                double centerNorth = coordinate.Z * RegionSizeMeters;
                double offsetEast = centerEast - observer.EastMeters;
                double offsetNorth = centerNorth - observer.NorthMeters;
                double distance = Math.Sqrt(
                    offsetEast * offsetEast + offsetNorth * offsetNorth);
                double forwardMeters =
                    (offsetEast * directionEast) + (offsetNorth * directionNorth);
                double lateralMeters = speed > 0.25
                    ? Math.Abs(
                        (-offsetEast * directionNorth) +
                        (offsetNorth * directionEast))
                    : double.PositiveInfinity;
                double forwardScore = speed > 0.25 && distance > 1.0
                    ? forwardMeters / distance
                    : 0.0;

                WorldStreamingRegionDetail detail;
                if (distance <= fullRadius)
                {
                    detail = WorldStreamingRegionDetail.Full;
                }
                else if (distance <= simplifiedRadius)
                {
                    detail = WorldStreamingRegionDetail.Simplified;
                }
                else if (speed > 0.25 &&
                    forwardMeters > 0.0 &&
                    forwardMeters <= planningRadius &&
                    lateralMeters <= RegionSizeMeters * 2.25)
                {
                    detail = WorldStreamingRegionDetail.Preload;
                }
                else
                {
                    continue;
                }

                WorldStreamingPriority priority = ResolvePriority(
                    coordinate,
                    center,
                    detail,
                    distance,
                    forwardScore);
                regions.Add(new WorldStreamingRegionPlan(
                    coordinate,
                    detail,
                    priority,
                    distance,
                    forwardScore));
            }
        }

        WorldStreamingRegionPlan[] ordered = regions
            .OrderBy(region => (int)region.Priority)
            .ThenBy(region => region.DistanceMeters)
            .ThenByDescending(region => region.ForwardScore)
            .ThenBy(region => region.Coordinate.Z)
            .ThenBy(region => region.Coordinate.X)
            .ToArray();
        return new WorldStreamingPlan(
            observer,
            fullRadius,
            simplifiedRadius,
            ordered);
    }

    public static WorldStreamingPriority ResolvePriority(
        WorldStreamingRegionCoordinate coordinate,
        WorldStreamingRegionCoordinate center,
        WorldStreamingRegionDetail detail,
        double distanceMeters,
        double forwardScore)
    {
        if (coordinate == center)
        {
            return WorldStreamingPriority.PlayerRegion;
        }
        if (detail == WorldStreamingRegionDetail.Full && forwardScore >= 0.45)
        {
            return WorldStreamingPriority.DirectionOfMovement;
        }
        if (detail == WorldStreamingRegionDetail.Full &&
            distanceMeters <= CollisionPriorityRadiusMeters)
        {
            return WorldStreamingPriority.CollisionRegion;
        }
        if (detail == WorldStreamingRegionDetail.Full)
        {
            return WorldStreamingPriority.VisibleRegion;
        }
        if (detail == WorldStreamingRegionDetail.Simplified)
        {
            return WorldStreamingPriority.FarRegion;
        }
        return WorldStreamingPriority.PreGeneration;
    }
}
