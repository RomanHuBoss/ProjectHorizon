using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private string _orbitalNavigationPresentationAcceptanceHud = "READY";
    private bool? _orbitalNavigationPresentationAcceptancePassed;
    private bool _orbitalNavigationPresentationReadyPrinted;

    private void UpdateOrbitalNavigationPresentationRuntime()
    {
        if (_orbitalNavigationPresentationReadyPrinted ||
            _starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null)
        {
            return;
        }

        StarSystemSimulationRuntime runtime = StarSystemSimulation;
        StarSystemBodyDefinition[] planets = runtime.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Planet)
            .OrderBy(body => body.OrbitRadius)
            .ToArray();
        StarSystemBodyDefinition[] moons = runtime.Definitions
            .Where(body => body.Kind == StarSystemBodyKind.Moon)
            .ToArray();
        double minimumPlanetGap = planets.Length <= 1
            ? 0.0
            : planets.Zip(planets.Skip(1), (left, right) =>
                right.OrbitRadius - left.OrbitRadius).Min();
        double minimumMoonRealPeriod = moons.Length == 0
            ? 0.0
            : moons.Min(body => body.OrbitPeriodSeconds /
                StarSystemSimulationRuntime.OrbitTimeScale);

        _orbitalNavigationPresentationReadyPrinted = true;
        GD.Print(
            "TASK-178.2 orbital navigation/presentation READY: " +
            $"orbitClock={StarSystemSimulationRuntime.OrbitTimeScale:0.###}x; " +
            $"planetOrbitMin={planets.Min(body => body.OrbitRadius):0}m; " +
            $"planetGapMin={minimumPlanetGap:0}m; " +
            $"moonOrbitMin={(moons.Length == 0 ? 0.0 : moons.Min(body => body.OrbitRadius)):0}m; " +
            $"moonRealPeriodMin={minimumMoonRealPeriod:0}s; " +
            "stationAutoDock=1; stationInterior=hangar; localTrafficProxies=hidden; " +
            "spaceEnvironment=explicit-dark-fogfree; F5=acceptance.");
    }

    private void RunOrbitalNavigationPresentationAcceptance()
    {
        bool stationInteriorReady = ValidateStationInteriorShell();
        OrbitalNavigationPresentationAcceptanceReport report =
            OrbitalNavigationPresentationAcceptanceRunner.Run(
                GalaxyNavigation.CurrentSystem,
                stationInteriorReady);
        _orbitalNavigationPresentationAcceptancePassed = report.Passed;
        _orbitalNavigationPresentationAcceptanceHud = report.BuildHudLine();
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }

    private static bool ValidateStationInteriorShell()
    {
        PackedScene? packed = GD.Load<PackedScene>(
            WorldSceneCoordinatorNode.StationScenePath);
        if (packed is null)
        {
            return false;
        }

        Node? instance = null;
        try
        {
            instance = packed.Instantiate();
            if (instance is not WorldSceneShell shell ||
                shell.Kind != WorldSceneKind.StationInterior)
            {
                return false;
            }

            int meshes = CountDescendants<MeshInstance3D>(instance);
            int lights = CountDescendants<OmniLight3D>(instance);
            return meshes >= 7 && lights >= 3;
        }
        finally
        {
            instance?.Free();
        }
    }

    private static int CountDescendants<TNode>(Node root)
        where TNode : Node
    {
        int count = root is TNode ? 1 : 0;
        foreach (Node child in root.GetChildren())
        {
            count += CountDescendants<TNode>(child);
        }
        return count;
    }
}
