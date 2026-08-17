using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetAtmosphereCloudProfile? _planetAtmosphereCloudProfile;
    private PlanetAtmosphereCloudNode? _planetAtmosphereCloudNode;
    private string _planetAtmosphereCloudAcceptanceHud = "READY";
    private bool? _planetAtmosphereCloudAcceptancePassed;

    private void PrintPlanetAtmosphereCloudReady()
    {
        GD.Print(
            "TASK-190 planetary atmosphere/clouds READY: " +
            "atmosphere=spherical-shell+directional-scattering+horizon+sunset; " +
            "clouds=1..2-spherical-layers+scrolling-noise+density+surface-shadow; " +
            "volumetricRayMarch=0; legacyCloudBlobs=retired; surfaceContactLatch=TASK-190; F5=acceptance.");
    }

    private void ApplyPlanetAtmosphereCloudProfile(PlanetEnvironmentProfile environment)
    {
        _planetAtmosphereCloudProfile = PlanetAtmosphereCloudRuntime.BuildProfile(environment);
        EnsurePlanetAtmosphereCloudNode();
        _planetAtmosphereCloudNode?.Configure(_planetAtmosphereCloudProfile);
        _planetAtmosphereCloudNode?.SetPerformanceQuality(
            _runtimePerformanceQualitySettings.MaximumCloudLayers,
            _runtimePerformanceQualitySettings.SecondaryCloudOpacityScale);
        _planetAtmosphereCloudNode?.SetGraphicsQuality(
            Math.Min(_graphicsQualitySettings.MaximumCloudLayers,
                _runtimePerformanceQualitySettings.MaximumCloudLayers),
            Math.Min(_graphicsQualitySettings.SecondaryCloudOpacityScale,
                _runtimePerformanceQualitySettings.SecondaryCloudOpacityScale),
            _graphicsQualitySettings.AtmosphereQualityScale,
            _graphicsQualitySettings.SimplifiedShaders);
        RetireLegacyCloudClusters();
    }

    private void EnsurePlanetAtmosphereCloudNode()
    {
        if (_planetAtmosphereCloudNode is not null &&
            GodotObject.IsInstanceValid(_planetAtmosphereCloudNode))
        {
            return;
        }
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is null)
        {
            return;
        }
        _planetAtmosphereCloudNode = new PlanetAtmosphereCloudNode
        {
            Name = "PlanetAtmosphereCloudShells"
        };
        gameplay.AddChild(_planetAtmosphereCloudNode);
        _planetAtmosphereCloudNode.SetGraphicsQuality(
            Math.Min(_graphicsQualitySettings.MaximumCloudLayers,
                _runtimePerformanceQualitySettings.MaximumCloudLayers),
            Math.Min(_graphicsQualitySettings.SecondaryCloudOpacityScale,
                _runtimePerformanceQualitySettings.SecondaryCloudOpacityScale),
            _graphicsQualitySettings.AtmosphereQualityScale,
            _graphicsQualitySettings.SimplifiedShaders);
    }

    private void RetireLegacyCloudClusters()
    {
        if (_planetSurfaceCloudRoot is null ||
            !GodotObject.IsInstanceValid(_planetSurfaceCloudRoot))
        {
            return;
        }
        foreach (Node child in _planetSurfaceCloudRoot.GetChildren())
        {
            child.QueueFree();
        }
        _planetSurfaceCloudRoot.Visible = false;
        _planetSurfaceCloudMaterial = null;
    }

    private void UpdatePlanetAtmosphereCloudRuntime(double delta)
    {
        _ = delta;
        if (_planetAtmosphereCloudProfile is null ||
            _planetSurfaceContentProfile is null ||
            _planetWeatherState is null)
        {
            return;
        }
        EnsurePlanetAtmosphereCloudNode();
        if (_planetAtmosphereCloudNode is null)
        {
            return;
        }

        WorldSceneKind kind = _worldSceneCoordinatorRuntime is null
            ? WorldSceneKind.Surface
            : WorldScenes.Current.Kind;
        double altitude = _voyageShip?.AltitudeAboveSurface ?? double.PositiveInfinity;
        bool visible = _surfaceRuntimeActive &&
            (kind == WorldSceneKind.Surface ||
                (kind == WorldSceneKind.Orbit &&
                    OrbitalHandoffPresentationRuntime.Evaluate(altitude).SurfaceSkyOwned));

        Vector3 observer = StageOneVoyage.Piloted && _voyageShip is not null
            ? _voyageShip.GlobalPosition
            : _player?.GlobalPosition ?? Vector3.Zero;
        Basis basis = _planetSurfacePhysicalFrameState?.SurfaceBasis ??
            (GetNodeOrNull<Node3D>("Gameplay")?.GlobalTransform.Basis ?? Basis.Identity);
        PlanetAtmosphereCloudFrame frame = PlanetAtmosphereCloudRuntime.BuildFrame(
            _planetAtmosphereCloudProfile,
            _planetWeatherState);
        _planetAtmosphereCloudNode.UpdateFrame(frame, basis, observer, visible);
    }

    private double CurrentCloudShadowFactor()
    {
        if (_planetAtmosphereCloudProfile is null || _planetWeatherState is null)
        {
            return 1.0;
        }
        return PlanetAtmosphereCloudRuntime.BuildFrame(
            _planetAtmosphereCloudProfile,
            _planetWeatherState).CloudShadowFactor;
    }

    private void RunPlanetAtmosphereCloudAcceptance()
    {
        bool liveNode = _planetAtmosphereCloudNode is not null &&
            GodotObject.IsInstanceValid(_planetAtmosphereCloudNode) &&
            _planetAtmosphereCloudNode.NoiseTexturesReady;
        bool legacyRetired = _planetSurfaceCloudRoot is null ||
            _planetSurfaceCloudRoot.GetChildCount() == 0;
        PlanetEnvironmentProfile environment = PlanetSurfaceContentProfile.Environment;
        PlanetWeatherState weather = _planetWeatherState ??
            PlanetWeatherRuntime.BuildState(environment, 9.0);
        PlanetAtmosphereCloudAcceptanceReport report =
            PlanetAtmosphereCloudAcceptanceRunner.Evaluate(
                environment,
                weather,
                liveNode,
                legacyRetired);
        _planetAtmosphereCloudAcceptancePassed = report.Passed;
        _planetAtmosphereCloudAcceptanceHud = report.Passed
            ? $"PASS shell=1 clouds={report.CloudLayerCount} noise=1 shadow=1 noRayMarch=1"
            : "FAIL atmosphere/cloud contract";
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
