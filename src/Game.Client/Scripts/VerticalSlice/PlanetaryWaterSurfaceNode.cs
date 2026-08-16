using System;
using Godot;

public partial class PlanetaryWaterSurfaceNode : Node3D
{
    public const double OceanPatchHalfExtentMeters = 340.0;
    public const int OceanPatchCells = 32;
    public const double OceanPatchSnapMeters = 128.0;

    public const string WaterShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back;
uniform vec4 shallow_color : source_color = vec4(0.05, 0.38, 0.62, 0.64);
uniform vec4 deep_color : source_color = vec4(0.015, 0.08, 0.16, 0.92);
uniform float wave_height = 0.12;
uniform float wave_scale = 0.085;
uniform float wave_speed = 0.75;
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;
void vertex() {
    float w1 = sin((VERTEX.x + VERTEX.z * 0.55) * wave_scale + TIME * wave_speed);
    float w2 = sin((VERTEX.z - VERTEX.x * 0.35) * wave_scale * 1.63 - TIME * wave_speed * 0.71);
    VERTEX.y += (w1 * 0.68 + w2 * 0.32) * wave_height;
}
void fragment() {
    float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
    float fresnel = pow(1.0 - facing, 4.0);
    float raw_depth = texture(depth_texture, SCREEN_UV).r;
    vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
    vec4 scene_view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    scene_view.xyz /= max(scene_view.w, 0.00001);
    float scene_depth = max(0.0, -scene_view.z);
    float water_depth = max(0.0, -VERTEX.z);
    float depth_below_surface = clamp((scene_depth - water_depth) / 12.0, 0.0, 1.0);
    ALBEDO = mix(shallow_color.rgb, deep_color.rgb, 0.18 + depth_below_surface * 0.68);
    ROUGHNESS = mix(0.10, 0.26, depth_below_surface);
    METALLIC = 0.0;
    SPECULAR = 0.92;
    ALPHA = clamp(shallow_color.a + fresnel * 0.20 + depth_below_surface * 0.16, 0.0, 0.96);
}";

    public const string UnderwaterPostShaderSource = @"
shader_type canvas_item;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform vec4 underwater_tint : source_color = vec4(0.02, 0.18, 0.28, 1.0);
uniform float intensity : hint_range(0.0, 1.0) = 0.6;
void fragment() {
    vec2 uv = SCREEN_UV;
    float wobble = sin((uv.y + TIME * 0.11) * 42.0) * 0.0018 * intensity;
    uv.x = clamp(uv.x + wobble, 0.001, 0.999);
    vec3 scene = texture(screen_texture, uv).rgb;
    float radial = length(SCREEN_UV - vec2(0.5));
    float vignette = smoothstep(0.78, 0.28, radial);
    scene = mix(scene, underwater_tint.rgb, 0.34 * intensity);
    scene *= mix(0.82, 1.0, vignette * (1.0 - 0.18 * intensity));
    COLOR = vec4(scene, 1.0);
}";

    private MeshInstance3D? _ocean;
    private Node3D? _lakes;
    private ShaderMaterial? _material;
    private string _geometrySignature = string.Empty;

    public bool OceanGeometryReady => _ocean?.Mesh is not null;
    public bool LocalLakeGeometryReady =>
        _lakes is not null && _lakes.GetChildCount() > 0;
    public int VisibleSurfaceCount =>
        (_ocean?.Visible == true ? 1 : 0) +
        (_lakes?.Visible == true ? _lakes.GetChildCount() : 0);

    public void Configure(
        PlanetaryWaterProfile profile,
        PlanetSurfaceCurvedPatchDescriptor patch,
        PlanetEnvironmentColor waterColor,
        double centerEastMeters,
        double centerNorthMeters,
        bool active)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(patch);
        EnsureMaterial(waterColor);

        double snappedEast = Math.Round(
            centerEastMeters / OceanPatchSnapMeters) * OceanPatchSnapMeters;
        double snappedNorth = Math.Round(
            centerNorthMeters / OceanPatchSnapMeters) * OceanPatchSnapMeters;
        string signature = string.Join(
            "|",
            profile.PlanetId,
            profile.OceanEnabled ? "ocean" : "lakes",
            patch.OriginEastMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            patch.OriginNorthMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            snappedEast.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            snappedNorth.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        if (!string.Equals(_geometrySignature, signature, StringComparison.Ordinal))
        {
            RebuildGeometry(profile, patch, snappedEast, snappedNorth);
            _geometrySignature = signature;
        }

        Visible = active && profile.HasWater;
        if (_ocean is not null)
        {
            _ocean.Visible = Visible && profile.OceanEnabled;
        }
        if (_lakes is not null)
        {
            _lakes.Visible = Visible && !profile.OceanEnabled;
        }
    }

    private void EnsureMaterial(PlanetEnvironmentColor waterColor)
    {
        if (_material is null)
        {
            Shader shader = new() { Code = WaterShaderSource };
            _material = new ShaderMaterial { Shader = shader };
        }
        Color baseColor = new(
            (float)waterColor.R,
            (float)waterColor.G,
            (float)waterColor.B,
            0.64f);
        Color deep = new(
            Math.Max(0.005f, baseColor.R * 0.18f),
            Math.Max(0.012f, baseColor.G * 0.25f),
            Math.Max(0.020f, baseColor.B * 0.32f),
            0.92f);
        _material.SetShaderParameter("shallow_color", baseColor);
        _material.SetShaderParameter("deep_color", deep);
    }

    private void RebuildGeometry(
        PlanetaryWaterProfile profile,
        PlanetSurfaceCurvedPatchDescriptor patch,
        double centerEastMeters,
        double centerNorthMeters)
    {
        if (_ocean is not null && GodotObject.IsInstanceValid(_ocean))
        {
            _ocean.QueueFree();
        }
        if (_lakes is not null && GodotObject.IsInstanceValid(_lakes))
        {
            _lakes.QueueFree();
        }
        _ocean = null;
        _lakes = null;

        if (profile.OceanEnabled)
        {
            _ocean = new MeshInstance3D
            {
                Name = "OceanSurface",
                Position = new Vector3((float)centerEastMeters, 0.0f, (float)centerNorthMeters),
                Mesh = BuildCurvedGrid(
                    patch,
                    centerEastMeters,
                    centerNorthMeters,
                    profile.OceanSurfaceHeightMeters,
                    OceanPatchHalfExtentMeters,
                    OceanPatchCells),
                MaterialOverride = _material
            };
            AddChild(_ocean);
        }
        else if (profile.Lakes.Count > 0)
        {
            _lakes = new Node3D { Name = "LocalLakes" };
            AddChild(_lakes);
            foreach (PlanetaryWaterLake lake in profile.Lakes)
            {
                MeshInstance3D instance = new()
                {
                    Name = lake.LakeId.Replace('.', '_'),
                    Position = new Vector3(
                        (float)lake.EastMeters,
                        0.0f,
                        (float)lake.NorthMeters),
                    Mesh = BuildCurvedDisc(patch, lake, segments: 40),
                    MaterialOverride = _material
                };
                _lakes.AddChild(instance);
            }
        }
    }

    private static ArrayMesh BuildCurvedGrid(
        PlanetSurfaceCurvedPatchDescriptor patch,
        double centerEastMeters,
        double centerNorthMeters,
        double semanticHeightMeters,
        double halfExtentMeters,
        int cells)
    {
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        double cellSize = (halfExtentMeters * 2.0) / cells;
        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                double x0 = -halfExtentMeters + x * cellSize;
                double x1 = x0 + cellSize;
                double z0 = -halfExtentMeters + z * cellSize;
                double z1 = z0 + cellSize;
                AddGridTriangle(tool, patch, centerEastMeters, centerNorthMeters,
                    semanticHeightMeters, x0, z0, x1, z0, x1, z1, halfExtentMeters);
                AddGridTriangle(tool, patch, centerEastMeters, centerNorthMeters,
                    semanticHeightMeters, x0, z0, x1, z1, x0, z1, halfExtentMeters);
            }
        }
        return tool.Commit();
    }

    private static void AddGridTriangle(
        SurfaceTool tool,
        PlanetSurfaceCurvedPatchDescriptor patch,
        double centerEastMeters,
        double centerNorthMeters,
        double semanticHeightMeters,
        double ax, double az,
        double bx, double bz,
        double cx, double cz,
        double halfExtentMeters)
    {
        AddGridVertex(tool, patch, centerEastMeters, centerNorthMeters,
            semanticHeightMeters, ax, az, halfExtentMeters);
        AddGridVertex(tool, patch, centerEastMeters, centerNorthMeters,
            semanticHeightMeters, bx, bz, halfExtentMeters);
        AddGridVertex(tool, patch, centerEastMeters, centerNorthMeters,
            semanticHeightMeters, cx, cz, halfExtentMeters);
    }

    private static void AddGridVertex(
        SurfaceTool tool,
        PlanetSurfaceCurvedPatchDescriptor patch,
        double centerEastMeters,
        double centerNorthMeters,
        double semanticHeightMeters,
        double x,
        double z,
        double halfExtentMeters)
    {
        double east = centerEastMeters + x;
        double north = centerNorthMeters + z;
        double localY = semanticHeightMeters - patch.TangentSagMeters(east, north);
        tool.SetNormal(patch.SurfaceUpLocal(east, north));
        tool.SetUV(new Vector2(
            (float)((x + halfExtentMeters) / (halfExtentMeters * 2.0)),
            (float)((z + halfExtentMeters) / (halfExtentMeters * 2.0))));
        tool.AddVertex(new Vector3((float)x, (float)localY, (float)z));
    }

    private static ArrayMesh BuildCurvedDisc(
        PlanetSurfaceCurvedPatchDescriptor patch,
        PlanetaryWaterLake lake,
        int segments)
    {
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int index = 0; index < segments; index++)
        {
            double a0 = Math.PI * 2.0 * index / segments;
            double a1 = Math.PI * 2.0 * (index + 1) / segments;
            AddLakeVertex(tool, patch, lake, 0.0, 0.0, new Vector2(0.5f, 0.5f));
            AddLakeVertex(tool, patch, lake,
                Math.Cos(a0) * lake.RadiusMeters,
                Math.Sin(a0) * lake.RadiusMeters,
                new Vector2((float)(0.5 + Math.Cos(a0) * 0.5), (float)(0.5 + Math.Sin(a0) * 0.5)));
            AddLakeVertex(tool, patch, lake,
                Math.Cos(a1) * lake.RadiusMeters,
                Math.Sin(a1) * lake.RadiusMeters,
                new Vector2((float)(0.5 + Math.Cos(a1) * 0.5), (float)(0.5 + Math.Sin(a1) * 0.5)));
        }
        return tool.Commit();
    }

    private static void AddLakeVertex(
        SurfaceTool tool,
        PlanetSurfaceCurvedPatchDescriptor patch,
        PlanetaryWaterLake lake,
        double localEast,
        double localNorth,
        Vector2 uv)
    {
        double east = lake.EastMeters + localEast;
        double north = lake.NorthMeters + localNorth;
        double localY = lake.SurfaceHeightMeters - patch.TangentSagMeters(east, north);
        tool.SetNormal(patch.SurfaceUpLocal(east, north));
        tool.SetUV(uv);
        tool.AddVertex(new Vector3((float)localEast, (float)localY, (float)localNorth));
    }
}
