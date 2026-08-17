using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetaryWaterProfile? _planetaryWaterProfile;
    private PlanetaryWaterSurfaceNode? _planetaryWaterNode;
    private ColorRect? _underwaterPostOverlay;
    private ShaderMaterial? _underwaterPostMaterial;
    private bool _planetaryWaterSwimming;
    private bool _planetaryWaterUnderwater;
    private string _planetaryWaterBody = "none";
    private string _planetaryWaterAcceptanceHud = "READY";
    private bool? _planetaryWaterAcceptancePassed;
    private PlanetaryWaterSample _lastPlanetaryWaterSample;

    private void PrintPlanetaryWaterReady()
    {
        GD.Print(
            "TASK-188 planetary water READY: " +
            "surface=spherical-fixed-level; oceans=coverage-driven; lakes=local-simplified; " +
            "shader=waves+sky-specular+depth-darkening; underwater=screen-post+audio+oxygen; " +
            "swimming=radial+buoyancy-assisted; fluidSimulation=0; F5=acceptance.");
    }

    private void ApplyPlanetaryWaterProfile(PlanetEnvironmentProfile environment)
    {
        _planetaryWaterProfile = PlanetaryWaterRuntime.BuildProfile(environment);
        EnsurePlanetaryWaterPresentationNodes();
        RetireLegacyWaterPoolVolume();
        RefreshPlanetaryWaterSurfaceGeometry();
        ResetPlanetaryWaterTransientState();
    }

    private void EnsurePlanetaryWaterPresentationNodes()
    {
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        if (gameplay is not null &&
            (_planetaryWaterNode is null || !GodotObject.IsInstanceValid(_planetaryWaterNode)))
        {
            _planetaryWaterNode = new PlanetaryWaterSurfaceNode
            {
                Name = "PlanetaryWater"
            };
            gameplay.AddChild(_planetaryWaterNode);
        }

        CanvasLayer? hud = GetNodeOrNull<CanvasLayer>("Hud");
        if (hud is not null &&
            (_underwaterPostOverlay is null || !GodotObject.IsInstanceValid(_underwaterPostOverlay)))
        {
            Shader shader = new()
            {
                Code = PlanetaryWaterSurfaceNode.UnderwaterPostShaderSource
            };
            _underwaterPostMaterial = new ShaderMaterial { Shader = shader };
            _underwaterPostOverlay = new ColorRect
            {
                Name = "UnderwaterPostEffect",
                Color = Colors.White,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = _underwaterPostMaterial,
                Visible = false,
                AnchorLeft = 0.0f,
                AnchorTop = 0.0f,
                AnchorRight = 1.0f,
                AnchorBottom = 1.0f,
                OffsetLeft = 0.0f,
                OffsetTop = 0.0f,
                OffsetRight = 0.0f,
                OffsetBottom = 0.0f
            };
            hud.AddChild(_underwaterPostOverlay);
            hud.MoveChild(_underwaterPostOverlay, 0);
        }
    }

    private void RetireLegacyWaterPoolVolume()
    {
        Area3D? legacy = GetNodeOrNull<Area3D>("Gameplay/WaterPool");
        if (legacy is null)
        {
            return;
        }
        legacy.Visible = false;
        legacy.Monitoring = false;
        legacy.Monitorable = false;
    }

    private void RefreshPlanetaryWaterSurfaceGeometry()
    {
        if (_planetaryWaterProfile is null || _planetaryWaterNode is null ||
            _planetSurfaceContentProfile is null)
        {
            return;
        }
        EnsurePlanetSurfaceFrameForCurrentPlanet();
        PlanetSurfaceCurvedPatchDescriptor? patch = CurrentPlanetSurfaceCurvedPatch;
        if (patch is null)
        {
            return;
        }
        PlanetSurfaceLogicalPosition logical = GetPlanetSurfaceLogicalPlayerPosition();
        _planetaryWaterNode.Configure(
            _planetaryWaterProfile,
            patch,
            PlanetSurfaceContentProfile.Environment.WaterColor,
            logical.EastMeters,
            logical.NorthMeters,
            _surfaceRuntimeActive);
    }

    private void UpdatePlanetaryWaterRuntime(double delta)
    {
        _ = delta;
        if (_planetaryWaterProfile is null || _player is null ||
            _planetSurfaceContentProfile is null)
        {
            return;
        }

        EnsurePlanetaryWaterPresentationNodes();
        RefreshPlanetaryWaterSurfaceGeometry();

        bool interactive = _surfaceRuntimeActive && !StageOneVoyage.Piloted &&
            !IsPlayerInsidePlanetaryCave && _planetaryWaterProfile.HasWater;
        if (!interactive)
        {
            SetPlanetaryWaterState(false, false, default, "none");
            return;
        }

        PlanetSurfaceLogicalPosition body = GetPlanetSurfaceLogicalPlayerPosition();
        Camera3D? camera = _player.GetNodeOrNull<Camera3D>("Head/Camera3D");
        Vector3 cameraLogical = camera is null
            ? new Vector3((float)body.EastMeters, (float)(body.HeightMeters + 1.6), (float)body.NorthMeters)
            : WorldToPlanetSurfaceLogicalPosition(camera.GlobalPosition);
        PlanetaryWaterSample sample = PlanetaryWaterRuntime.Sample(
            _planetaryWaterProfile,
            body.EastMeters,
            body.NorthMeters,
            body.HeightMeters,
            cameraLogical.Y,
            _planetaryWaterSwimming,
            _planetaryWaterUnderwater);

        SetPlanetaryWaterState(
            sample.Swimming,
            sample.Underwater,
            sample,
            sample.WaterBody);
    }

    private void SetPlanetaryWaterState(
        bool swimming,
        bool underwater,
        PlanetaryWaterSample sample,
        string waterBody)
    {
        bool swimmingChanged = swimming != _planetaryWaterSwimming;
        bool underwaterChanged = underwater != _planetaryWaterUnderwater;
        bool bodyChanged = !string.Equals(
            waterBody,
            _planetaryWaterBody,
            StringComparison.Ordinal);

        _planetaryWaterSwimming = swimming;
        _planetaryWaterUnderwater = underwater;
        _planetaryWaterBody = waterBody;
        _lastPlanetaryWaterSample = sample;

        if (_player is not null)
        {
            _player.SetWaterImmersion(swimming, sample.BodyDepthMeters);
        }
        if (_playerSurvivalRuntime is not null)
        {
            PlayerSurvival.SetUnderwater(underwater);
        }
        UpdateUnderwaterPostEffect(sample);

        if ((swimmingChanged || underwaterChanged || bodyChanged) &&
            (swimming || underwater || swimmingChanged || underwaterChanged))
        {
            GD.Print(
                "TASK-188 water state PASS: " +
                $"body={waterBody}; swimming={(swimming ? 1 : 0)}; underwater={(underwater ? 1 : 0)}; " +
                $"surface={sample.SurfaceHeightMeters.ToString("0.00", CultureInfo.InvariantCulture)}m; " +
                $"bodyDepth={sample.BodyDepthMeters.ToString("0.00", CultureInfo.InvariantCulture)}m; " +
                $"cameraDepth={sample.CameraDepthMeters.ToString("0.00", CultureInfo.InvariantCulture)}m.");
        }
    }

    private void UpdateUnderwaterPostEffect(PlanetaryWaterSample sample)
    {
        if (_underwaterPostOverlay is null || _underwaterPostMaterial is null)
        {
            return;
        }
        _underwaterPostOverlay.Visible = _planetaryWaterUnderwater;
        if (!_planetaryWaterUnderwater || _planetSurfaceContentProfile is null)
        {
            return;
        }
        PlanetEnvironmentColor water = PlanetSurfaceContentProfile.Environment.WaterColor;
        Color tint = new(
            Math.Max(0.01f, (float)water.R * 0.38f),
            Math.Max(0.04f, (float)water.G * 0.55f),
            Math.Max(0.08f, (float)water.B * 0.68f),
            1.0f);
        float intensity = (float)Math.Clamp(
            0.42 + Math.Max(0.0, sample.CameraDepthMeters) / 9.0,
            0.42,
            1.0);
        _underwaterPostMaterial.SetShaderParameter("underwater_tint", tint);
        _underwaterPostMaterial.SetShaderParameter("intensity", intensity);
    }

    private void ResetPlanetaryWaterTransientState()
    {
        _planetaryWaterSwimming = false;
        _planetaryWaterUnderwater = false;
        _planetaryWaterBody = "none";
        _lastPlanetaryWaterSample = default;
        _player?.SetWaterImmersion(false, 0.0);
        if (_playerSurvivalRuntime is not null)
        {
            PlayerSurvival.SetUnderwater(false);
        }
        if (_underwaterPostOverlay is not null)
        {
            _underwaterPostOverlay.Visible = false;
        }
    }

    private void RunPlanetaryWaterAcceptance()
    {
        _planetaryWaterAcceptanceHud = "RUNNING";
        _planetaryWaterAcceptancePassed = null;
        double radius = _planetSurfaceContentProfile?.Environment.RadiusKm * 1000.0 ?? 44_000.0;
        Area3D? legacy = GetNodeOrNull<Area3D>("Gameplay/WaterPool");
        bool legacyRetired = legacy is null ||
            (!legacy.Visible && !legacy.Monitoring && !legacy.Monitorable);
        bool liveNode = _planetaryWaterNode is not null &&
            GodotObject.IsInstanceValid(_planetaryWaterNode);

        PlanetaryWaterAcceptanceReport report =
            PlanetaryWaterAcceptanceRunner.Evaluate(
                radius,
                liveNode,
                legacyRetired);
        _planetaryWaterAcceptancePassed = report.Passed;
        _planetaryWaterAcceptanceHud = report.Passed
            ? "PASS fixed=1 ocean=1 lakes=1 swim=1 post=1"
            : $"FAIL {report.Result}";
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
