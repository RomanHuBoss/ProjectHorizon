using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _planetaryGlobeAcceptanceHud = "READY";

    private void InitializePlanetaryGlobeRuntime()
    {
        if (_planetEnvironmentRuntime is null ||
            _galaxyNavigationRuntime is null ||
            _starSystemSimulationNode is null)
        {
            return;
        }

        PlanetEnvironmentProfile profile = PlanetEnvironment.BuildProfile(
            GalaxyNavigation.CurrentPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        PlanetSurfaceTopologyRuntime topology = new(profile.RadiusKm);
        DetailedPlanetGlobeDiagnostics globe =
            _starSystemSimulationNode.CreateDetailedGlobeDiagnostics();
        GD.Print(
            "TASK-168 planetary globe READY: " +
            $"planet={profile.PlanetId}; topology=spherical-geodesic; " +
            $"radius={profile.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)}km; " +
            $"circumference={(topology.CircumferenceMeters / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)}km; " +
            $"globeFaces={globe.FaceCount}/6; globeResolution={DetailedPlanetGlobeNode.FaceResolution}; " +
            "orbitRepresentation=single-detailed-cube-sphere; surface=tangent-bounded; " +
            "curvature=distant-proxy; persistence=logical-xz/no-schema-bump; F5=acceptance.");
    }

    private string BuildPlanetaryGlobeHudLine()
    {
        if (_planetEnvironmentRuntime is null ||
            _galaxyNavigationRuntime is null ||
            _planetSurfaceFrame is null)
        {
            return "planet geodesy: unavailable";
        }

        PlanetEnvironmentProfile profile = PlanetEnvironment.BuildProfile(
            GalaxyNavigation.CurrentPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        PlanetSurfaceTopologyRuntime topology = new(profile.RadiusKm);
        PlanetSurfaceLogicalPosition logical =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetSurfaceGeographicAddress address = topology.FromLogical(
            logical.EastMeters,
            logical.NorthMeters);
        int globeFaces = _starSystemSimulationNode?
            .CreateDetailedGlobeDiagnostics().FaceCount ?? 0;
        return
            "planet geodesy: " +
            $"lat={address.LatitudeDegrees.ToString("0.0000", CultureInfo.InvariantCulture)}°; " +
            $"lon={address.LongitudeDegrees.ToString("0.0000", CultureInfo.InvariantCulture)}°; " +
            $"R={profile.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)}km; " +
            $"globe={globeFaces}/6; tangent=bounded";
    }

    private void RunPlanetaryGlobeAcceptance()
    {
        if (_starSystemSimulationNode is null ||
            _planetEnvironmentRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            _planetaryGlobeAcceptanceHud = "FAIL — runtime unavailable";
            GD.PushError(
                "TASK-168 planetary globe and geodesy acceptance FAIL: runtime unavailable");
            return;
        }

        PlanetEnvironmentProfile[] profiles = GalaxyNavigation.CurrentSystem.Planets
            .Select(planet => PlanetEnvironment.BuildProfile(
                planet,
                GalaxyNavigation.CurrentSystem.StarType))
            .ToArray();
        PlanetaryGlobeAcceptanceReport report = PlanetaryGlobeAcceptanceRunner.Run(
            profiles,
            _starSystemSimulationNode,
            GalaxyNavigation.CurrentPlanetId);
        _planetaryGlobeAcceptanceHud = report.BuildHudLine();
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
