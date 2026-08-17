using System;
using Godot;

public partial class SalvageRepairSlice
{
    private GraphicsQualityProfile _graphicsQualityProfile = GraphicsQualityProfile.Medium;
    private GraphicsQualitySettings _graphicsQualitySettings = GraphicsQualityProfilePolicy.Medium;
    private string _graphicsQualityAcceptanceHud = "READY";
    private bool? _graphicsQualityAcceptancePassed;
    private bool _graphicsQualityReadyPrinted;
    private double _appliedParticleAmountScale = -1.0;

    private double GraphicsVegetationDistanceScale => _graphicsQualitySettings.VegetationDistanceScale;
    private double GraphicsVegetationDensityScale => _graphicsQualitySettings.VegetationDensityScale;
    private bool GraphicsShadowsEnabled =>
        _graphicsQualitySettings.ShadowQuality != GraphicsShadowQuality.Disabled;
    private double GraphicsShadowMaxDistanceMeters => _graphicsQualitySettings.ShadowMaxDistanceMeters;

    private RuntimePerformanceProfile ResolveGraphicsPerformanceBudgetProfile() =>
        GraphicsQualityProfilePolicy.ResolvePerformanceBudgetProfile(_graphicsQualityProfile);

    private void InitializeGraphicsQualityProfiles()
    {
        ApplyGraphicsQualityFromUserSettings(printReady: true);
    }

    private void ApplyGraphicsQualityFromUserSettings(bool printReady)
    {
        GameUserSettings settings = GameUserSettingsService.Current;
        RendererProfileSnapshot renderer = RendererProfileDiagnostics.Capture();
        GraphicsQualityProfile requested = settings.GraphicsQualityProfile;
        GraphicsQualityProfile effective = renderer.IsCompatibilityRenderer
            ? GraphicsQualityProfile.Compatibility
            : requested;
        _graphicsQualityProfile = effective;
        _graphicsQualitySettings = GraphicsQualityProfilePolicy.Get(effective);

        if (_runtimePerformanceTelemetry.SampleCount > 0 || _runtimePerformanceReadyPrinted)
        {
            _runtimePerformanceProfile = ResolveGraphicsPerformanceBudgetProfile();
            _runtimePerformanceQualitySettings = RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                _runtimePerformanceProfile,
                _runtimePerformanceTelemetry.Governor.State);
        }
        ApplyGraphicsQualityPresentationSettings();

        if (printReady && !_graphicsQualityReadyPrinted)
        {
            _graphicsQualityReadyPrinted = true;
            GD.Print(
                "TASK-202 graphics quality READY: " +
                $"requested={requested}; effective={effective}; renderer={(renderer.IsCompatibilityRenderer ? "Compatibility" : renderer.RenderingMethod)}; " +
                $"vegetation=density:{_graphicsQualitySettings.VegetationDensityScale:0.00}/distance:{_graphicsQualitySettings.VegetationDistanceScale:0.00}; " +
                $"surfaceDistance={_graphicsQualitySettings.SurfaceDistanceScale:0.00}; " +
                $"shadows={_graphicsQualitySettings.ShadowQuality}:{_graphicsQualitySettings.ShadowMaxDistanceMeters:0}m; " +
                $"clouds<={_graphicsQualitySettings.MaximumCloudLayers}; water={_graphicsQualitySettings.WaterWaveScale:0.00}; " +
                $"post={(_graphicsQualitySettings.GlowEnabled ? 1 : 0)}; particles={_graphicsQualitySettings.ParticleAmountScale:0.00}; " +
                $"simplifiedShaders={(_graphicsQualitySettings.SimplifiedShaders ? 1 : 0)}; adaptiveCeiling=TASK-200; F5=acceptance.");
        }
    }

    private void ApplyGraphicsQualityPresentationSettings()
    {
        int performanceCloudLimit = _runtimePerformanceQualitySettings.MaximumCloudLayers;
        double performanceSecondaryOpacity = _runtimePerformanceQualitySettings.SecondaryCloudOpacityScale;
        int cloudLimit = Math.Min(_graphicsQualitySettings.MaximumCloudLayers, performanceCloudLimit);
        double secondaryOpacity = Math.Min(
            _graphicsQualitySettings.SecondaryCloudOpacityScale,
            performanceSecondaryOpacity);
        _planetAtmosphereCloudNode?.SetGraphicsQuality(
            cloudLimit,
            secondaryOpacity,
            _graphicsQualitySettings.AtmosphereQualityScale,
            _graphicsQualitySettings.SimplifiedShaders);
        _planetaryWaterNode?.SetGraphicsQuality(
            _graphicsQualitySettings.WaterWaveScale,
            _graphicsQualitySettings.WaterDepthScale,
            _graphicsQualitySettings.UnderwaterDistortionScale,
            _graphicsQualitySettings.SimplifiedShaders);
        if (_underwaterPostMaterial is not null)
        {
            _underwaterPostMaterial.SetShaderParameter(
                "distortion_scale",
                (float)_graphicsQualitySettings.UnderwaterDistortionScale);
            _underwaterPostMaterial.SetShaderParameter(
                "simplified_shading",
                _graphicsQualitySettings.SimplifiedShaders);
        }
        _worldStreamingCoordinator?.SetPresentationDistanceScale(
            _graphicsQualitySettings.SurfaceDistanceScale);
        ApplyGraphicsShadowQuality();
        ApplyGraphicsPostEffects();
        if (Math.Abs(_appliedParticleAmountScale - _graphicsQualitySettings.ParticleAmountScale) > 0.0001)
        {
            ApplyParticleQualityRecursive(this, _graphicsQualitySettings.ParticleAmountScale);
            _appliedParticleAmountScale = _graphicsQualitySettings.ParticleAmountScale;
        }
        UpdateRegionalVegetationVisibility();
    }

    private void ApplyGraphicsShadowQuality()
    {
        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (sun is null)
        {
            return;
        }
        bool surfaceOwned = _surfaceRuntimeActive;
        sun.ShadowEnabled = surfaceOwned && GraphicsShadowsEnabled;
        sun.Set("directional_shadow_max_distance", (float)Math.Max(1.0, GraphicsShadowMaxDistanceMeters));
        int shadowMode = _graphicsQualitySettings.ShadowQuality switch
        {
            GraphicsShadowQuality.High => 2,
            GraphicsShadowQuality.Medium => 1,
            _ => 0
        };
        sun.Set("directional_shadow_mode", shadowMode);
        sun.Set("directional_shadow_blend_splits", _graphicsQualitySettings.ShadowQuality == GraphicsShadowQuality.High);
    }

    private void ApplyGraphicsPostEffects()
    {
        WorldEnvironment? world = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (world?.Environment is not Godot.Environment environment)
        {
            return;
        }
        bool glow = _graphicsQualitySettings.GlowEnabled &&
            !_graphicsQualitySettings.SimplifiedShaders;
        environment.Set("glow_enabled", glow);
        environment.Set("glow_intensity", (float)_graphicsQualitySettings.GlowIntensity);
    }

    private static void ApplyParticleQualityRecursive(Node node, double amountScale)
    {
        float clamped = (float)Math.Clamp(amountScale, 0.1, 1.0);
        foreach (Node child in node.GetChildren())
        {
            string type = child.GetClass();
            if (type is "GPUParticles3D" or "CPUParticles3D")
            {
                child.Set("amount_ratio", clamped);
            }
            ApplyParticleQualityRecursive(child, clamped);
        }
    }

    private bool ShouldRenderVegetationBatchForGraphics(
        EcologyMultiMeshGroup group,
        double observerDistanceMeters)
    {
        if (observerDistanceMeters <= VegetationRegionRuntime.DemotionDistanceMeters ||
            GraphicsVegetationDensityScale >= 0.999 ||
            group.Placements.Count == 0)
        {
            return true;
        }

        string id = group.Placements[0].InstanceId;
        uint hash = 2166136261u;
        foreach (char c in id)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        double normalized = (hash & 0x00FFFFFFu) / 16777215.0;
        return normalized <= GraphicsVegetationDensityScale;
    }

    private void RunGraphicsQualityAcceptance()
    {
        RendererProfileSnapshot renderer = RendererProfileDiagnostics.Capture();
        GraphicsQualityAcceptanceReport report = GraphicsQualityAcceptanceRunner.Evaluate(
            GameUserSettingsService.Current.GraphicsQualityProfile,
            _graphicsQualityProfile,
            renderer.IsCompatibilityRenderer,
            _planetAtmosphereCloudNode,
            _planetaryWaterNode,
            GraphicsShadowsEnabled,
            PerformanceVegetationDistanceScale,
            GraphicsVegetationDensityScale);
        _graphicsQualityAcceptancePassed = report.Passed;
        _graphicsQualityAcceptanceHud = report.Passed
            ? $"PASS {_graphicsQualityProfile} veg={GraphicsVegetationDensityScale:0.00}/{PerformanceVegetationDistanceScale:0.00} cloud={report.LiveCloudLimit}"
            : "FAIL graphics profile contract";
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
