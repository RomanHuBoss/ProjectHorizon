using System;

public sealed record PlanetSurfaceRadialFrameState(
    string PlanetId,
    PlanetSurfaceGeographicAddress Geographic,
    PlanetSurfaceCubeFaceAddress CubeFace,
    PlanetSurfaceTangentFrame GlobalFrame,
    double GravityMetersPerSecondSquared)
{
    public string FaceName => CubeFace.FaceName;
}

/// <summary>
/// TASK-170 bridge between the persistent logical surface cover and a true
/// planet-radial coordinate frame. The live vertical-slice keeps a small Godot
/// tangent patch around the player; within that patch local +Y is defined to be
/// the current global radial-up vector. Re-centering therefore changes the
/// planet-global frame without forcing large Vector3 coordinates into physics.
/// </summary>
public sealed class PlanetSurfaceRadialFrameRuntime
{
    public const double StandardGravityMetersPerSecondSquared = 9.80665;

    public PlanetSurfaceRadialFrameRuntime(PlanetEnvironmentProfile environment)
    {
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        if (!environment.Landable)
        {
            throw new InvalidOperationException(
                "Radial surface frame requires a landable planet.");
        }
        Topology = new PlanetSurfaceTopologyRuntime(environment.RadiusKm);
    }

    public PlanetEnvironmentProfile Environment { get; }
    public PlanetSurfaceTopologyRuntime Topology { get; }

    public PlanetSurfaceRadialFrameState Build(
        double logicalEastMeters,
        double logicalNorthMeters)
    {
        PlanetSurfaceGeographicAddress geographic = Topology.FromLogical(
            logicalEastMeters,
            logicalNorthMeters);
        return Build(geographic);
    }

    public PlanetSurfaceRadialFrameState Build(
        PlanetSurfaceGeographicAddress geographic)
    {
        PlanetSurfaceGeographicAddress normalized = Topology.FromGeographic(
            geographic.LatitudeDegrees,
            geographic.LongitudeDegrees);
        return new PlanetSurfaceRadialFrameState(
            Environment.PlanetId,
            normalized,
            Topology.ToCubeFaceAddress(normalized),
            Topology.BuildTangentFrame(normalized),
            Environment.SurfaceGravityG * StandardGravityMetersPerSecondSquared);
    }

    public PlanetSurfaceCanonicalLogicalAddress WarpTarget(
        double latitudeDegrees,
        double longitudeDegrees) =>
        Topology.ToCanonicalLogical(
            Topology.FromGeographic(latitudeDegrees, longitudeDegrees));

    public double MeasureUpDeltaDegrees(
        PlanetSurfaceRadialFrameState left,
        PlanetSurfaceRadialFrameState right)
    {
        double dot = Math.Clamp(
            left.GlobalFrame.Up.Dot(right.GlobalFrame.Up),
            -1.0,
            1.0);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }
}
