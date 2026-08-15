using System;
using System.Globalization;
using Godot;

public partial class CubeSpherePrototype
{
    private PlanetEnvironmentProfile? _planetEnvironmentProfile;
    private Node3D? _environmentShells;
    private MeshInstance3D? _waterShell;
    private MeshInstance3D? _atmosphereShell;
    private MeshInstance3D? _cloudShellPrimary;
    private MeshInstance3D? _cloudShellSecondary;

    private void InitializePlanetEnvironmentPresentation()
    {
        if (_planetRoot is null)
        {
            return;
        }

        const string catalogPath = "res://Content/planet_environments.json";
        string json = FileAccess.GetFileAsString(catalogPath);
        PlanetEnvironmentCatalog catalog =
            PlanetEnvironmentCatalog.LoadFromJson(json);
        PlanetEnvironmentRuntime runtime = new(catalog);
        GalaxyPlanetDefinition previewPlanet = new(
            DeveloperToolContext.PreviewPlanetId,
            DeveloperToolContext.PreviewPlanetArchetype,
            1,
            0,
            DeveloperToolContext.PreviewHasAtmosphere,
            DeveloperToolContext.PreviewHasWater,
            DeveloperToolContext.PreviewPlanetSeed);
        _planetEnvironmentProfile = runtime.BuildProfile(
            previewPlanet,
            DeveloperToolContext.PreviewStarType);

        _environmentShells?.QueueFree();
        _environmentShells = new Node3D
        {
            Name = "EnvironmentShells"
        };
        _planetRoot.AddChild(_environmentShells);

        if (_planetEnvironmentProfile.WaterCoverage > 0.0)
        {
            float waterRadius = PlanetRadius - 1.2f +
                (float)_planetEnvironmentProfile.WaterCoverage * 0.8f;
            _waterShell = CreateSphereShell(
                "WaterShell",
                waterRadius,
                "res://Shaders/planet_water_shell.gdshader");
            ShaderMaterial waterMaterial =
                (ShaderMaterial)_waterShell.MaterialOverride!;
            waterMaterial.SetShaderParameter(
                "water_color",
                ToColor(_planetEnvironmentProfile.WaterColor));
            waterMaterial.SetShaderParameter(
                "wave_strength",
                0.05f +
                (float)_planetEnvironmentProfile.WaterCoverage * 0.06f);
        }

        if (_planetEnvironmentProfile.AtmosphereDensity > 0.0)
        {
            _atmosphereShell = CreateSphereShell(
                "AtmosphereShell",
                PlanetRadius * 1.075f,
                "res://Shaders/planet_atmosphere_shell.gdshader");
            ShaderMaterial atmosphereMaterial =
                (ShaderMaterial)_atmosphereShell.MaterialOverride!;
            atmosphereMaterial.SetShaderParameter(
                "atmosphere_color",
                ToColor(_planetEnvironmentProfile.AtmosphereColor));
            atmosphereMaterial.SetShaderParameter(
                "sunset_color",
                ToColor(_planetEnvironmentProfile.SunsetColor));
            atmosphereMaterial.SetShaderParameter(
                "density",
                (float)_planetEnvironmentProfile.AtmosphereDensity);
            atmosphereMaterial.SetShaderParameter(
                "star_direction_world",
                new Vector3(-0.35f, 0.75f, 0.55f).Normalized());
        }

        if (_planetEnvironmentProfile.CloudLayerCount >= 1)
        {
            _cloudShellPrimary = CreateCloudShell(
                "CloudShellPrimary",
                PlanetRadius * 1.035f,
                0.0f,
                0.012f);
        }

        if (_planetEnvironmentProfile.CloudLayerCount >= 2)
        {
            _cloudShellSecondary = CreateCloudShell(
                "CloudShellSecondary",
                PlanetRadius * 1.052f,
                4.7f,
                -0.008f);
        }

        GD.Print(
            "TASK-150 Planet Preview environment READY: " +
            $"planet={_planetEnvironmentProfile.PlanetId}; " +
            $"archetype={_planetEnvironmentProfile.Archetype}; " +
            $"radius={_planetEnvironmentProfile.RadiusKm.ToString("0.0", CultureInfo.InvariantCulture)}km; " +
            $"gravity={_planetEnvironmentProfile.SurfaceGravityG.ToString("0.00", CultureInfo.InvariantCulture)}g; " +
            $"water={_planetEnvironmentProfile.WaterCoverage.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"atmosphere={_planetEnvironmentProfile.AtmosphereDensity.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"clouds={_planetEnvironmentProfile.CloudLayerCount}; " +
            $"biomes={_planetEnvironmentProfile.ActiveBiomeIds.Count}; " +
            "presentation=spherical-water+simplified-atmosphere+scrolling-clouds.");
    }

    private MeshInstance3D CreateCloudShell(
        string name,
        float radius,
        float phase,
        float scrollSpeed)
    {
        MeshInstance3D shell = CreateSphereShell(
            name,
            radius,
            "res://Shaders/planet_cloud_shell.gdshader");
        ShaderMaterial material = (ShaderMaterial)shell.MaterialOverride!;
        material.SetShaderParameter(
            "cloud_density",
            (float)(_planetEnvironmentProfile?.CloudDensity ?? 0.0));
        material.SetShaderParameter("layer_phase", phase);
        material.SetShaderParameter("scroll_speed", scrollSpeed);
        return shell;
    }

    private MeshInstance3D CreateSphereShell(
        string name,
        float radius,
        string shaderPath)
    {
        if (_environmentShells is null)
        {
            throw new InvalidOperationException(
                "Planet environment shell root is unavailable.");
        }

        SphereMesh mesh = new()
        {
            Radius = radius,
            Height = radius * 2.0f,
            RadialSegments = 72,
            Rings = 36
        };
        Shader shader = GD.Load<Shader>(shaderPath) ??
            throw new InvalidOperationException(
                $"Unable to load environment shader {shaderPath}.");
        ShaderMaterial material = new()
        {
            Shader = shader
        };
        MeshInstance3D shell = new()
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _environmentShells.AddChild(shell);
        return shell;
    }

    private string BuildPlanetEnvironmentPrototypeHudLine()
    {
        PlanetEnvironmentProfile? profile = _planetEnvironmentProfile;
        if (profile is null)
        {
            return "Environment: unavailable";
        }

        return "Environment: " +
            $"{profile.Archetype} • R={profile.RadiusKm:F1} km • " +
            $"g={profile.SurfaceGravityG:F2} • T={profile.MeanTemperatureC:F0} C • " +
            $"water={profile.WaterCoverage:P0} • atmo={profile.AtmosphereDensity:F2} • " +
            $"clouds={profile.CloudLayerCount} • biomes={profile.ActiveBiomeIds.Count}";
    }

    private static Color ToColor(PlanetEnvironmentColor color) =>
        new(
            (float)color.R,
            (float)color.G,
            (float)color.B,
            1.0f);
}
