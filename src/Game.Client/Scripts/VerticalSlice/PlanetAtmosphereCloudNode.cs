using System;
using Godot;

/// <summary>
/// Surface-view low-cost atmospheric shell and 0..2 spherical cloud shells.
/// The node follows the active surface observer so its finite geometry never
/// becomes a planet-scale resident asset; local Y is always radial Up.
/// </summary>
public partial class PlanetAtmosphereCloudNode : Node3D
{
    public const string CloudNoiseAPath =
        "res://Assets/Textures/Environment/cloud_noise_1.png";
    public const string CloudNoiseBPath =
        "res://Assets/Textures/Environment/cloud_noise_2.png";

    public const string AtmosphereShaderSource = """
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, fog_disabled, blend_mix;

uniform vec3 zenith_color : source_color = vec3(0.12, 0.28, 0.60);
uniform vec3 horizon_color : source_color = vec3(0.48, 0.66, 0.86);
uniform vec3 sunset_color : source_color = vec3(0.96, 0.32, 0.12);
uniform vec3 star_direction = vec3(0.0, 0.7, -0.7);
uniform float atmosphere_opacity : hint_range(0.0, 0.8) = 0.32;
uniform float horizon_amplification : hint_range(1.0, 4.0) = 2.0;
uniform float sunset_factor : hint_range(0.0, 1.0) = 0.0;
uniform float daylight : hint_range(0.0, 1.0) = 1.0;

varying vec3 shell_dir;

void vertex() {
    shell_dir = normalize(VERTEX);
}

void fragment() {
    vec3 direction = normalize(shell_dir);
    float vertical = abs(direction.y);
    float horizon = pow(clamp(1.0 - vertical, 0.0, 1.0), horizon_amplification);
    vec3 star_dir = normalize(star_direction);
    float star_facing = pow(max(dot(direction, star_dir), 0.0), 3.0);
    float dusk_band = sunset_factor * horizon * (0.30 + 0.70 * star_facing);
    vec3 day_color = mix(zenith_color, horizon_color, horizon);
    vec3 color = mix(day_color, sunset_color, clamp(dusk_band, 0.0, 0.72));
    float night_floor = mix(0.20, 1.0, daylight);
    color *= night_floor;
    ALBEDO = color;
    EMISSION = color * (0.16 + 0.42 * horizon);
    ALPHA = atmosphere_opacity * (0.18 + 0.82 * horizon);
}
""";

    public const string CloudShaderSource = """
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, fog_disabled, blend_mix;

uniform sampler2D noise_a : repeat_enable, filter_linear_mipmap;
uniform sampler2D noise_b : repeat_enable, filter_linear_mipmap;
uniform vec2 scroll_a = vec2(0.0025, 0.0008);
uniform vec2 scroll_b = vec2(-0.0011, 0.0018);
uniform vec3 cloud_color : source_color = vec3(0.92, 0.95, 1.0);
uniform vec3 shadow_color : source_color = vec3(0.36, 0.42, 0.52);
uniform vec3 star_direction = vec3(0.0, 0.7, -0.7);
uniform float density : hint_range(0.0, 1.0) = 0.55;
uniform float opacity : hint_range(0.0, 1.0) = 0.70;
uniform float layer_phase = 0.0;

varying vec3 shell_dir;

void vertex() {
    shell_dir = normalize(VERTEX);
}

void fragment() {
    vec2 uv_a = UV * vec2(3.2, 1.8) + scroll_a * TIME + vec2(layer_phase, 0.0);
    vec2 uv_b = UV * vec2(6.4, 3.6) + scroll_b * TIME - vec2(0.0, layer_phase * 0.7);
    float n1 = texture(noise_a, uv_a).r;
    float n2 = texture(noise_b, uv_b).r;
    float noise_value = n1 * 0.68 + n2 * 0.32;
    float threshold = mix(0.72, 0.38, density);
    float mask = smoothstep(threshold, threshold + 0.14, noise_value);
    vec3 star_dir = normalize(star_direction);
    float lit = 0.38 + 0.62 * max(dot(normalize(shell_dir), star_dir), 0.0);
    vec3 color = mix(shadow_color, cloud_color, lit);
    ALBEDO = color;
    EMISSION = color * 0.08;
    ALPHA = mask * opacity;
}
""";

    private MeshInstance3D? _atmosphere;
    private readonly MeshInstance3D?[] _clouds = new MeshInstance3D?[2];
    private ShaderMaterial? _atmosphereMaterial;
    private readonly ShaderMaterial?[] _cloudMaterials = new ShaderMaterial?[2];
    private PlanetAtmosphereCloudProfile? _profile;
    private Texture2D? _noiseA;
    private Texture2D? _noiseB;

    public int ActiveCloudLayerCount { get; private set; }
    public bool AtmosphereShellActive =>
        _atmosphere is not null && GodotObject.IsInstanceValid(_atmosphere) && _atmosphere.Visible;
    public bool NoiseTexturesReady => _noiseA is not null && _noiseB is not null;

    public override void _Ready()
    {
        EnsureTextures();
    }

    public void Configure(PlanetAtmosphereCloudProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
        EnsureTextures();
        EnsureGeometry();
        ApplyProfile(profile);
    }

    public void UpdateFrame(
        PlanetAtmosphereCloudFrame frame,
        Basis surfaceBasis,
        Vector3 observerPosition,
        bool visible)
    {
        if (_profile is null)
        {
            return;
        }

        Visible = visible && (_profile.AtmosphereEnabled || _profile.CloudLayers > 0);
        if (!Visible)
        {
            return;
        }

        GlobalTransform = new Transform3D(surfaceBasis.Orthonormalized(), observerPosition);
        Vector3 starDirection = BuildLocalStarDirection(
            frame.SunAzimuthDegrees,
            frame.SunElevationDegrees);

        if (_atmosphereMaterial is not null)
        {
            _atmosphereMaterial.SetShaderParameter("star_direction", starDirection);
            _atmosphereMaterial.SetShaderParameter("atmosphere_opacity", (float)frame.AtmosphereOpacity);
            _atmosphereMaterial.SetShaderParameter("sunset_factor", (float)frame.SunsetFactor);
            _atmosphereMaterial.SetShaderParameter("daylight", (float)frame.Daylight);
        }

        float windAngle = Mathf.DegToRad((float)frame.WindDirectionDegrees);
        float windScale = (float)Math.Clamp(0.00055 + frame.WindMetersPerSecond * 0.000045, 0.00055, 0.0022);
        Vector2 wind = new(Mathf.Sin(windAngle), Mathf.Cos(windAngle));
        for (int index = 0; index < _cloudMaterials.Length; index++)
        {
            ShaderMaterial? material = _cloudMaterials[index];
            MeshInstance3D? cloud = _clouds[index];
            bool active = index < _profile.CloudLayers;
            if (cloud is not null)
            {
                cloud.Visible = active;
            }
            if (!active || material is null)
            {
                continue;
            }
            float layer = index + 1.0f;
            material.SetShaderParameter("star_direction", starDirection);
            material.SetShaderParameter("density", (float)frame.CloudDensity);
            material.SetShaderParameter("opacity", (float)(frame.CloudOpacity * (index == 0 ? 1.0 : 0.78)));
            material.SetShaderParameter("scroll_a", wind * (windScale * layer));
            material.SetShaderParameter("scroll_b", new Vector2(-wind.Y, wind.X) * (windScale * 0.61f * layer));
        }
        ActiveCloudLayerCount = _profile.CloudLayers;
    }


    private void EnsureTextures()
    {
        _noiseA ??= GD.Load<Texture2D>(CloudNoiseAPath);
        _noiseB ??= GD.Load<Texture2D>(CloudNoiseBPath);
    }
    private void EnsureGeometry()
    {
        if (_profile is null)
        {
            return;
        }

        if (_atmosphere is null || !GodotObject.IsInstanceValid(_atmosphere))
        {
            Shader shader = new() { Code = AtmosphereShaderSource };
            _atmosphereMaterial = new ShaderMaterial { Shader = shader };
            SphereMesh mesh = new()
            {
                Radius = (float)_profile.ShellRadiusMeters,
                Height = (float)_profile.ShellRadiusMeters * 2.0f,
                RadialSegments = 48,
                Rings = 24,
                Material = _atmosphereMaterial
            };
            _atmosphere = new MeshInstance3D
            {
                Name = "AtmosphereScatteringShell",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(_atmosphere);
        }

        for (int index = 0; index < _clouds.Length; index++)
        {
            if (_clouds[index] is not null && GodotObject.IsInstanceValid(_clouds[index]))
            {
                continue;
            }
            Shader shader = new() { Code = CloudShaderSource };
            ShaderMaterial material = new() { Shader = shader };
            material.SetShaderParameter("noise_a", _noiseA);
            material.SetShaderParameter("noise_b", _noiseB);
            material.SetShaderParameter("layer_phase", index * 0.173f);
            _cloudMaterials[index] = material;
            float radius = (float)(_profile.CloudBaseRadiusMeters +
                index * _profile.CloudLayerSpacingMeters);
            SphereMesh mesh = new()
            {
                Radius = radius,
                Height = radius * 2.0f,
                RadialSegments = index == 0 ? 48 : 40,
                Rings = index == 0 ? 24 : 20,
                Material = material
            };
            MeshInstance3D cloud = new()
            {
                Name = $"SphericalCloudLayer{index + 1}",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            _clouds[index] = cloud;
            AddChild(cloud);
        }
    }

    private void ApplyProfile(PlanetAtmosphereCloudProfile profile)
    {
        Color zenith = ToColor(profile.ZenithColor);
        Color horizon = ToColor(profile.HorizonColor);
        Color sunset = ToColor(profile.SunsetColor);
        _atmosphereMaterial?.SetShaderParameter("zenith_color", zenith);
        _atmosphereMaterial?.SetShaderParameter("horizon_color", horizon);
        _atmosphereMaterial?.SetShaderParameter("sunset_color", sunset);
        _atmosphereMaterial?.SetShaderParameter("horizon_amplification", (float)profile.HorizonAmplification);
        if (_atmosphere is not null)
        {
            _atmosphere.Visible = profile.AtmosphereEnabled;
        }

        Color cloud = horizon.Lightened(0.54f);
        Color shadow = horizon.Darkened(0.55f);
        for (int index = 0; index < _cloudMaterials.Length; index++)
        {
            _cloudMaterials[index]?.SetShaderParameter("cloud_color", cloud);
            _cloudMaterials[index]?.SetShaderParameter("shadow_color", shadow);
            if (_clouds[index] is not null)
            {
                _clouds[index]!.Visible = index < profile.CloudLayers;
            }
        }
        ActiveCloudLayerCount = profile.CloudLayers;
    }

    private static Vector3 BuildLocalStarDirection(double azimuthDegrees, double elevationDegrees)
    {
        double azimuth = azimuthDegrees * Math.PI / 180.0;
        double elevation = elevationDegrees * Math.PI / 180.0;
        Vector3 direction = new(
            (float)(Math.Cos(elevation) * Math.Sin(azimuth)),
            (float)Math.Sin(elevation),
            (float)(Math.Cos(elevation) * Math.Cos(azimuth)));
        return direction.LengthSquared() <= 0.000001f ? Vector3.Up : direction.Normalized();
    }

    private static Color ToColor(PlanetEnvironmentColor color) => new(
        (float)Math.Clamp(color.R, 0.0, 1.0),
        (float)Math.Clamp(color.G, 0.0, 1.0),
        (float)Math.Clamp(color.B, 0.0, 1.0),
        1.0f);
}
