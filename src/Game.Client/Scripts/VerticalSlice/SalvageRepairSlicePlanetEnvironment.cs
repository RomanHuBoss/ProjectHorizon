using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetEnvironmentCatalog? _planetEnvironmentCatalog;
    private PlanetEnvironmentRuntime? _planetEnvironmentRuntime;
    private string _planetEnvironmentAcceptanceHud = "READY";

    private PlanetEnvironmentCatalog PlanetEnvironmentCatalog =>
        _planetEnvironmentCatalog ??
        throw new InvalidOperationException(
            "Planet environment catalog is unavailable.");

    private PlanetEnvironmentRuntime PlanetEnvironment =>
        _planetEnvironmentRuntime ??
        throw new InvalidOperationException(
            "Planet environment runtime is unavailable.");

    private PlanetEnvironmentCatalog LoadPlanetEnvironmentCatalog(
        EcologyCatalog ecologyCatalog)
    {
        const string path = "res://Content/planet_environments.json";
        using FileAccess file = FileAccess.Open(
            path,
            FileAccess.ModeFlags.Read) ??
            throw new InvalidOperationException($"Unable to open {path}.");
        PlanetEnvironmentCatalog catalog =
            PlanetEnvironmentCatalog.LoadFromJson(file.GetAsText());
        catalog.ValidateBiomeReferences(ecologyCatalog);
        GD.Print(
            "TASK-150 planet environment catalog READY: " +
            $"schema={catalog.SchemaVersion}; " +
            $"archetypes={catalog.Archetypes.Count}/9; " +
            "radius=20-80km; biomes=max8; clouds=0-2; " +
            "water=spherical-fixed-level; atmosphere=simplified-shell.");
        return catalog;
    }

    private void InitializePlanetEnvironmentRuntime()
    {
        _planetEnvironmentRuntime = new PlanetEnvironmentRuntime(
            PlanetEnvironmentCatalog,
            EcologyCatalog);
        PlanetEnvironmentProfile current = PlanetEnvironment.BuildProfile(
            GalaxyNavigation.CurrentPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        GD.Print(
            "TASK-150 planet environment READY: " +
            $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
            $"planets={GalaxyNavigation.CurrentSystem.Planets.Count}; " +
            $"planet={current.PlanetId}; archetype={current.Archetype}; " +
            $"radius={current.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)}km; " +
            $"gravity={current.SurfaceGravityG.ToString("0.00", CultureInfo.InvariantCulture)}g; " +
            $"temperature={current.MeanTemperatureC.ToString("0.0", CultureInfo.InvariantCulture)}C; " +
            $"water={current.WaterCoverage.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"atmosphere={current.AtmosphereDensity.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"clouds={current.CloudLayerCount}; biomes={current.ActiveBiomeIds.Count}; " +
            "systemMap=M; PlanetPreview=developer-workbench; F5=acceptance.");
    }

    private string BuildPlanetEnvironmentMapDetail(
        GalaxyPlanetDefinition planet,
        GalaxyStarType starType)
    {
        PlanetEnvironmentProfile profile = PlanetEnvironment.BuildProfile(
            planet,
            starType);
        return LF(
            "ui.galaxy.planet_environment_row",
            ("radius", profile.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)),
            ("gravity", profile.SurfaceGravityG.ToString("0.00", CultureInfo.InvariantCulture)),
            ("temperature", profile.MeanTemperatureC.ToString("0", CultureInfo.InvariantCulture)),
            ("water", (profile.WaterCoverage * 100.0).ToString("0", CultureInfo.InvariantCulture)),
            ("atmosphere", profile.AtmosphereDensity.ToString("0.00", CultureInfo.InvariantCulture)),
            ("clouds", profile.CloudLayerCount),
            ("biomes", profile.ActiveBiomeIds.Count),
            ("landable", profile.Landable ? 1 : 0));
    }

    private string BuildPlanetEnvironmentHudLine()
    {
        if (_planetEnvironmentRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            return L("ui.hud.planet_environment.unavailable");
        }

        PlanetEnvironmentProfile profile = PlanetEnvironment.BuildProfile(
            GalaxyNavigation.CurrentPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        return LF(
            "ui.hud.planet_environment.summary",
            ("planet", profile.PlanetId),
            ("archetype", LocalizeGalaxyPlanetArchetype(profile.Archetype)),
            ("radius", profile.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)),
            ("gravity", profile.SurfaceGravityG.ToString("0.00", CultureInfo.InvariantCulture)),
            ("temperature", profile.MeanTemperatureC.ToString("0", CultureInfo.InvariantCulture)),
            ("water", (profile.WaterCoverage * 100.0).ToString("0", CultureInfo.InvariantCulture)),
            ("clouds", profile.CloudLayerCount));
    }

    private void RunPlanetEnvironmentAcceptance()
    {
        PlanetEnvironmentAcceptanceReport report =
            PlanetEnvironmentAcceptanceRunner.Run(
                PlanetEnvironmentCatalog,
                EcologyCatalog);
        _planetEnvironmentAcceptanceHud = report.BuildHudLine();
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
