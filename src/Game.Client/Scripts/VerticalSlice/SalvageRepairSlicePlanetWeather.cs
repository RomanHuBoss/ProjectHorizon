using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private PlanetWeatherRuntime? _planetWeatherRuntime;
    private PlanetWeatherState? _planetWeatherState;
    private Node3D? _planetWeatherFxRoot;
    private string _planetWeatherFxKey = string.Empty;
    private string _planetWeatherAcceptanceHud = "READY";
    private long _lastWeatherAnnouncedCell = long.MinValue;

    private PlanetWeatherRuntime PlanetWeather =>
        _planetWeatherRuntime ??
        throw new InvalidOperationException("Planet weather runtime is unavailable.");

    private void InitializePlanetWeatherRuntime(PlanetWeatherSaveData? saveData)
    {
        if (_planetSurfaceContentProfile is null)
        {
            return;
        }
        _planetWeatherRuntime = new PlanetWeatherRuntime(
            PlanetSurfaceContentProfile.Environment,
            saveData);
        PlanetWeatherState state = PlanetWeather.Current;
        _planetWeatherState = state;
        EnsurePlanetWeatherFxRoot();
        ApplyPlanetWeatherPresentation(state, forceFx: true);
        GD.Print(
            "TASK-166 planetary weather READY: " +
            $"planet={state.PlanetId}; " +
            $"day={state.DayIndex}; " +
            $"time={state.LocalHour.ToString("0.00", CultureInfo.InvariantCulture)}h; " +
            $"weather={state.Kind}; " +
            $"wind={state.WindMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture)}m/s; " +
            $"visibility={state.Visibility.ToString("0.00", CultureInfo.InvariantCulture)}; " +
            $"dayLength={PlanetWeatherRuntime.DefaultDayDurationSeconds:0}s; persistence=game-hours; " +
            "developer=set_time/set_weather; F5=acceptance.");
    }

    private void SyncPlanetWeatherToActivePlanet()
    {
        if (_planetWeatherRuntime is null || _planetSurfaceContentProfile is null)
        {
            return;
        }
        PlanetWeather.SetEnvironment(PlanetSurfaceContentProfile.Environment);
        PlanetWeatherState state = PlanetWeather.Current;
        _planetWeatherState = state;
        ApplyPlanetWeatherPresentation(state, forceFx: true);
    }

    private void UpdatePlanetWeather(double delta)
    {
        if (_planetWeatherRuntime is null || _planetSurfaceContentProfile is null)
        {
            return;
        }
        if (!string.Equals(
                PlanetWeather.Environment.PlanetId,
                PlanetSurfaceContentProfile.PlanetId,
                StringComparison.Ordinal))
        {
            PlanetWeather.SetEnvironment(PlanetSurfaceContentProfile.Environment);
        }

        PlanetWeatherState previous = _planetWeatherState ?? PlanetWeather.Current;
        PlanetWeatherState current = PlanetWeather.Advance(
            _surfaceRuntimeActive ? delta : 0.0);
        _planetWeatherState = current;
        if (_surfaceRuntimeActive)
        {
            double driftScale = Math.Clamp(
                current.WindMetersPerSecond / 8.0,
                0.20,
                3.5);
            _planetSurfaceCloudDrift += Math.Max(0.0, delta) * driftScale * 3.0;
        }
        bool weatherChanged = previous.Kind != current.Kind ||
            previous.DayIndex != current.DayIndex;
        ApplyPlanetWeatherPresentation(current, weatherChanged);
        ApplyPlanetWeatherToFauna(current);
        _audioDirector?.SetWeatherIntensity(
            _surfaceRuntimeActive
                ? (float)Math.Clamp(
                    current.WindMetersPerSecond / 22.0 +
                    (current.Kind == PlanetWeatherKind.Storm ? 0.22 : 0.0),
                    0.10,
                    1.0)
                : 0.0f);

        long cell = current.DayIndex * 12L +
            (long)Math.Floor(current.LocalHour / PlanetWeatherRuntime.WeatherCellHours);
        if (weatherChanged && cell != _lastWeatherAnnouncedCell)
        {
            _lastWeatherAnnouncedCell = cell;
            GD.Print(
                "TASK-166 weather transition: " +
                $"planet={current.PlanetId}; day={current.DayIndex}; " +
                $"time={current.LocalHour.ToString("0.00", CultureInfo.InvariantCulture)}h; " +
                $"weather={current.Kind}; intensity={current.Intensity.ToString("0.00", CultureInfo.InvariantCulture)}; " +
                $"wind={current.WindMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture)}m/s; " +
                $"precip={current.Precipitation.ToString("0.00", CultureInfo.InvariantCulture)}; " +
                $"visibility={current.Visibility.ToString("0.00", CultureInfo.InvariantCulture)}.");
        }
    }

    private void ApplyPlanetWeatherPresentation(
        PlanetWeatherState state,
        bool forceFx)
    {
        if (_planetSurfaceSkyProfile is null)
        {
            return;
        }

        PlanetSurfaceSkyProfile baseSky = _planetSurfaceSkyProfile;
        double azimuth = state.SunAzimuthDegrees * Math.PI / 180.0;
        double elevation = state.SunElevationDegrees * Math.PI / 180.0;
        Vector3 towardSun = new(
            (float)(Math.Cos(elevation) * Math.Sin(azimuth)),
            (float)Math.Sin(elevation),
            (float)(Math.Cos(elevation) * Math.Cos(azimuth)));
        if (towardSun.LengthSquared() > 0.0001f)
        {
            _planetSurfaceSunDirection = towardSun.Normalized();
        }

        Color baseTop = ToColor(baseSky.SkyTopColor);
        Color baseHorizon = ToColor(baseSky.SkyHorizonColor);
        Color sunset = ToColor(PlanetSurfaceContentProfile.Environment.SunsetColor);
        Color night = new(0.008f, 0.014f, 0.032f);
        float daylight = (float)state.Daylight;
        float sunsetFactor = (float)Math.Clamp(
            1.0 - Math.Abs(state.SunElevationDegrees) / 18.0,
            0.0,
            1.0) * (1.0f - Math.Abs(daylight - 0.5f) * 1.35f);
        Color top = night.Lerp(baseTop, daylight);
        Color horizon = night.Lightened(0.06f).Lerp(baseHorizon, daylight)
            .Lerp(sunset, Mathf.Clamp(sunsetFactor * 0.58f, 0.0f, 0.58f));

        if (_planetSurfaceSkyMaterial is not null)
        {
            _planetSurfaceSkyMaterial.Set("sky_top_color", top);
            _planetSurfaceSkyMaterial.Set("sky_horizon_color", horizon);
            _planetSurfaceSkyMaterial.Set(
                "ground_horizon_color",
                horizon.Darkened(0.48f));
            _planetSurfaceSkyMaterial.Set(
                "sky_energy_multiplier",
                (float)(0.12 + state.Daylight * 1.02));
        }

        WorldEnvironment? world = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (world?.Environment is Godot.Environment environment)
        {
            double visibilityPenalty = 1.0 + (1.0 - state.Visibility) * 2.4;
            environment.Set(
                "fog_density",
                (float)(baseSky.FogDensity * state.FogMultiplier * visibilityPenalty));
            environment.Set("fog_light_color", horizon.Lightened(0.08f));
            environment.Set(
                "ambient_light_energy",
                (float)(0.14 + state.Daylight * 0.76));
            environment.Set("ambient_light_color", horizon);
        }

        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (sun is not null)
        {
            double weatherDim = state.Kind switch
            {
                PlanetWeatherKind.Storm => 0.48 + 0.22 * (1.0 - state.Intensity),
                PlanetWeatherKind.Toxic => 0.62,
                PlanetWeatherKind.Wind => 0.90,
                _ => 1.0
            };
            sun.LightEnergy = (float)(baseSky.SunEnergy *
                Math.Max(0.02, state.Daylight) * weatherDim);
            sun.LightColor = state.Kind == PlanetWeatherKind.Toxic
                ? ToColor(baseSky.SunColor).Lerp(new Color(0.68f, 0.90f, 0.38f), 0.24f)
                : ToColor(baseSky.SunColor);
            Vector3 ray = -SurfaceLocalDirectionToWorld(
                _planetSurfaceSunDirection).Normalized();
            sun.LookAt(ray, SurfaceLocalDirectionToWorld(Vector3.Up).Normalized());
        }

        if (_planetSurfaceCloudRoot is not null)
        {
            _planetSurfaceCloudRoot.Visible = _surfaceRuntimeActive &&
                baseSky.CloudClusterCount > 0;
            float windAngle = Mathf.DegToRad((float)state.WindDirectionDegrees);
            if (_player is not null)
            {
                PlanetSurfaceLogicalPosition logicalPlayer =
                    GetPlanetSurfaceLogicalPlayerPosition();
                _planetSurfaceCloudRoot.Position = new Vector3(
                    (float)logicalPlayer.EastMeters +
                        Mathf.Sin(windAngle) * (float)(_planetSurfaceCloudDrift * 0.18),
                    0.0f,
                    (float)logicalPlayer.NorthMeters +
                        Mathf.Cos(windAngle) * (float)(_planetSurfaceCloudDrift * 0.18));
            }
        }
        if (_planetSurfaceCloudMaterial is not null)
        {
            Color cloud = ToColor(baseSky.SkyHorizonColor).Lightened(0.62f);
            float alpha = (float)Math.Clamp(
                baseSky.CloudOpacity * state.CloudMultiplier,
                0.04,
                0.86);
            if (state.Kind == PlanetWeatherKind.Storm)
            {
                cloud = cloud.Darkened(0.42f);
            }
            else if (state.Kind == PlanetWeatherKind.Toxic)
            {
                cloud = cloud.Lerp(new Color(0.42f, 0.62f, 0.18f), 0.42f);
            }
            _planetSurfaceCloudMaterial.AlbedoColor = new Color(
                cloud.R, cloud.G, cloud.B, alpha);
        }

        UpdatePlanetSurfaceSunVisual();
        if (_planetSurfaceSunVisual is not null &&
            GodotObject.IsInstanceValid(_planetSurfaceSunVisual))
        {
            _planetSurfaceSunVisual.Visible = _surfaceRuntimeActive &&
                state.SunElevationDegrees > -5.0 && state.Visibility > 0.28;
        }
        if (forceFx || WeatherFxKey(state) != _planetWeatherFxKey)
        {
            RebuildPlanetWeatherFx(state);
        }
        UpdatePlanetWeatherFx(state);
    }

    private void EnsurePlanetWeatherFxRoot()
    {
        if (_planetWeatherFxRoot is not null &&
            GodotObject.IsInstanceValid(_planetWeatherFxRoot))
        {
            return;
        }
        _planetWeatherFxRoot = new Node3D { Name = "PlanetWeatherFx" };
        (GetNodeOrNull<Node3D>("Gameplay") ?? this).AddChild(_planetWeatherFxRoot);
    }

    private string WeatherFxKey(PlanetWeatherState state) =>
        $"{state.PlanetId}:{state.DayIndex}:{state.Kind}:{(state.Precipitation > 0.05 ? 1 : 0)}";

    private void RebuildPlanetWeatherFx(PlanetWeatherState state)
    {
        EnsurePlanetWeatherFxRoot();
        if (_planetWeatherFxRoot is null)
        {
            return;
        }
        foreach (Node child in _planetWeatherFxRoot.GetChildren())
        {
            child.QueueFree();
        }
        _planetWeatherFxKey = WeatherFxKey(state);
        if (state.Kind is not (PlanetWeatherKind.Storm or PlanetWeatherKind.Toxic) ||
            state.Intensity < 0.20)
        {
            return;
        }

        bool snow = state.Kind == PlanetWeatherKind.Storm &&
            state.Precipitation >= 0.05 &&
            PlanetSurfaceContentProfile.Environment.MeanTemperatureC +
            state.TemperatureOffsetC < 1.0;
        bool dryStorm = state.Kind == PlanetWeatherKind.Storm &&
            state.Precipitation < 0.05;
        Mesh mesh;
        Color particleColor;
        if (state.Kind == PlanetWeatherKind.Toxic)
        {
            mesh = new SphereMesh
            {
                Radius = 0.035f,
                Height = 0.07f,
                RadialSegments = 6,
                Rings = 3
            };
            particleColor = new Color(0.46f, 0.90f, 0.24f, 0.44f);
        }
        else if (snow)
        {
            mesh = new SphereMesh
            {
                Radius = 0.028f,
                Height = 0.056f,
                RadialSegments = 6,
                Rings = 3
            };
            particleColor = new Color(0.94f, 0.97f, 1.0f, 0.72f);
        }
        else if (dryStorm)
        {
            mesh = new BoxMesh { Size = new Vector3(0.11f, 0.028f, 0.028f) };
            particleColor = new Color(0.72f, 0.57f, 0.34f, 0.38f);
        }
        else
        {
            mesh = new BoxMesh { Size = new Vector3(0.018f, 0.48f, 0.018f) };
            particleColor = new Color(0.62f, 0.76f, 0.92f, 0.46f);
        }
        StandardMaterial3D material = new()
        {
            AlbedoColor = particleColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        if (mesh is PrimitiveMesh primitive)
        {
            primitive.Material = material;
        }

        int count = state.Kind == PlanetWeatherKind.Toxic ? 56 :
            72 + (int)Math.Round(state.Precipitation * 64.0);
        MultiMesh multi = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = count
        };
        RandomNumberGenerator random = new()
        {
            Seed = unchecked((ulong)PlanetSurfaceContentProfile.Environment.Seed) ^
                unchecked((ulong)(state.DayIndex + 1) * 0xA17B3UL) ^
                (ulong)state.Kind
        };
        for (int index = 0; index < count; index++)
        {
            Vector3 position = new(
                random.RandfRange(-18.0f, 18.0f),
                random.RandfRange(1.5f, 18.0f),
                random.RandfRange(-18.0f, 18.0f));
            float scale = random.RandfRange(0.75f, 1.35f);
            multi.SetInstanceTransform(index, new Transform3D(
                Basis.Identity.Scaled(Vector3.One * scale), position));
        }
        _planetWeatherFxRoot.AddChild(new MultiMeshInstance3D
        {
            Name = state.Kind == PlanetWeatherKind.Toxic
                ? "ToxicMotes"
                : dryStorm ? "DustField" : snow ? "SnowField" : "RainField",
            Multimesh = multi
        });
    }

    private void UpdatePlanetWeatherFx(PlanetWeatherState state)
    {
        if (_planetWeatherFxRoot is null || _player is null)
        {
            return;
        }
        bool active = _surfaceRuntimeActive &&
            state.Kind is PlanetWeatherKind.Storm or PlanetWeatherKind.Toxic;
        _planetWeatherFxRoot.Visible = active;
        if (!active)
        {
            return;
        }
        float angle = Mathf.DegToRad((float)state.WindDirectionDegrees);
        double time = Time.GetTicksMsec() / 1000.0;
        float vertical = state.Kind == PlanetWeatherKind.Toxic
            ? (float)Math.Sin(time * 0.35) * 1.1f
            : (float)(-((time * (7.0 + state.Intensity * 8.0)) % 12.0));
        Vector3 anchor = StageOneVoyage.Piloted && _voyageShip is not null
            ? _voyageShip.GlobalPosition
            : _player.GlobalPosition;
        Vector3 localOffset = new(
            Mathf.Sin(angle) * (float)(time % 8.0) * 0.22f,
            13.0f + vertical,
            Mathf.Cos(angle) * (float)(time % 8.0) * 0.22f);
        _planetWeatherFxRoot.GlobalPosition = anchor +
            SurfaceLocalDirectionToWorld(localOffset);
    }

    private void ApplyPlanetWeatherToFauna(PlanetWeatherState state)
    {
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes)
        {
            fauna.SetWeatherResponse(
                state.FaunaSpeedMultiplier,
                state.WindMetersPerSecond,
                state.WindDirectionDegrees);
        }
    }

    private string BuildPlanetWeatherHudLine()
    {
        if (_planetWeatherState is not { } state)
        {
            return L("ui.hud.weather.unavailable");
        }
        return LF(
            "ui.hud.weather.summary",
            ("day", state.DayIndex),
            ("time", state.LocalHour.ToString("00.00", CultureInfo.InvariantCulture)),
            ("weather", LocalizeWeather(state.Kind)),
            ("wind", state.WindMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture)),
            ("visibility", (state.Visibility * 100.0).ToString("0", CultureInfo.InvariantCulture)),
            ("temperature", (PlanetSurfaceContentProfile.Environment.MeanTemperatureC + state.TemperatureOffsetC)
                .ToString("0", CultureInfo.InvariantCulture)));
    }

    private string LocalizeWeather(PlanetWeatherKind kind) => kind switch
    {
        PlanetWeatherKind.Wind => L("ui.weather.wind"),
        PlanetWeatherKind.Storm => L("ui.weather.storm"),
        PlanetWeatherKind.Toxic => L("ui.weather.toxic"),
        _ => L("ui.weather.clear")
    };

    private void RunPlanetWeatherAcceptance()
    {
        if (_planetEnvironmentRuntime is null || _galaxyNavigationRuntime is null)
        {
            _planetWeatherAcceptanceHud = "FAIL unavailable";
            GD.PushError("TASK-166 planetary weather acceptance FAIL: runtime unavailable");
            return;
        }
        PlanetEnvironmentProfile[] environments = GalaxyNavigation.CurrentSystem.Planets
            .Select(planet => PlanetEnvironment.BuildProfile(
                planet,
                GalaxyNavigation.CurrentSystem.StarType))
            .Where(profile => profile.Landable)
            .ToArray();
        PlanetWeatherAcceptanceReport report = PlanetWeatherAcceptanceRunner.Run(environments);
        _planetWeatherAcceptanceHud = report.Passed ? "PASS" : "FAIL";
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
