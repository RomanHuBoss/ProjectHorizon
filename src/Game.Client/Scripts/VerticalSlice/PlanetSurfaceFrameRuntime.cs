using System;

public readonly record struct PlanetSurfaceLogicalPosition(
    double EastMeters,
    double HeightMeters,
    double NorthMeters);

public readonly record struct PlanetSurfaceFrameRebase(
    double ShiftEastMeters,
    double ShiftNorthMeters)
{
    public bool Required =>
        Math.Abs(ShiftEastMeters) > 0.000001 ||
        Math.Abs(ShiftNorthMeters) > 0.000001;
}

public readonly record struct PlanetSurfaceFrameSnapshot(
    string PlanetId,
    double OriginEastMeters,
    double OriginNorthMeters,
    int RebaseCount,
    double MaximumObservedLocalDistanceMeters);

/// <summary>
/// Keeps Godot scene-space coordinates bounded while preserving a deterministic,
/// planet-global east/north logical coordinate frame. This class is deliberately
/// Godot-independent so rebasing math can be acceptance-tested without a scene tree.
/// </summary>
public sealed class PlanetSurfaceFrameRuntime
{
    public const double RebaseCellSizeMeters = 4096.0;
    public const double RebaseThresholdMeters = 2048.0;
    public const double LocalCoordinateToleranceMeters = 2048.0001;
    public const double PlanetLogicalHalfExtentMeters = 300_000.0;

    public string PlanetId { get; private set; } = string.Empty;
    public double OriginEastMeters { get; private set; }
    public double OriginNorthMeters { get; private set; }
    public int RebaseCount { get; private set; }
    public double MaximumObservedLocalDistanceMeters { get; private set; }

    public void Reset(string planetId)
    {
        PlanetId = planetId ?? string.Empty;
        OriginEastMeters = 0.0;
        OriginNorthMeters = 0.0;
        RebaseCount = 0;
        MaximumObservedLocalDistanceMeters = 0.0;
    }

    public void RestoreAtLogicalPosition(
        string planetId,
        double logicalEastMeters,
        double logicalNorthMeters)
    {
        PlanetId = planetId ?? string.Empty;
        OriginEastMeters = logicalEastMeters;
        OriginNorthMeters = logicalNorthMeters;
        RebaseCount = 0;
        MaximumObservedLocalDistanceMeters = Math.Max(
            Math.Abs(logicalEastMeters - OriginEastMeters),
            Math.Abs(logicalNorthMeters - OriginNorthMeters));
    }

    public PlanetSurfaceLogicalPosition ToLogical(
        double localEastMeters,
        double heightMeters,
        double localNorthMeters)
    {
        ObserveLocal(localEastMeters, localNorthMeters);
        return new PlanetSurfaceLogicalPosition(
            OriginEastMeters + localEastMeters,
            heightMeters,
            OriginNorthMeters + localNorthMeters);
    }

    public (double EastMeters, double NorthMeters) ToLocal(
        double logicalEastMeters,
        double logicalNorthMeters) =>
        (
            logicalEastMeters - OriginEastMeters,
            logicalNorthMeters - OriginNorthMeters
        );

    public PlanetSurfaceFrameRebase PlanRebase(
        double localEastMeters,
        double localNorthMeters)
    {
        ObserveLocal(localEastMeters, localNorthMeters);
        return new PlanetSurfaceFrameRebase(
            CalculateAxisShift(localEastMeters),
            CalculateAxisShift(localNorthMeters));
    }

    public void Apply(PlanetSurfaceFrameRebase rebase)
    {
        if (!rebase.Required)
        {
            return;
        }

        OriginEastMeters += rebase.ShiftEastMeters;
        OriginNorthMeters += rebase.ShiftNorthMeters;
        RebaseCount++;
    }

    public PlanetSurfaceFrameSnapshot CreateSnapshot() => new(
        PlanetId,
        OriginEastMeters,
        OriginNorthMeters,
        RebaseCount,
        MaximumObservedLocalDistanceMeters);

    public static double CalculateAxisShift(double localCoordinateMeters)
    {
        if (Math.Abs(localCoordinateMeters) <= RebaseThresholdMeters)
        {
            return 0.0;
        }

        return Math.Floor(
            (localCoordinateMeters + RebaseThresholdMeters) /
            RebaseCellSizeMeters) * RebaseCellSizeMeters;
    }

    private void ObserveLocal(double localEastMeters, double localNorthMeters)
    {
        MaximumObservedLocalDistanceMeters = Math.Max(
            MaximumObservedLocalDistanceMeters,
            Math.Max(Math.Abs(localEastMeters), Math.Abs(localNorthMeters)));
    }
}
