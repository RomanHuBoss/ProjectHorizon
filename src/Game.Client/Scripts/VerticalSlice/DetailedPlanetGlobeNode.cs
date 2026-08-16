using System;
using System.Collections.Generic;
using Godot;

public sealed record DetailedPlanetGlobeDiagnostics(
    string PlanetId,
    int FaceCount,
    int Vertices,
    int Triangles,
    int SeamComparisons,
    int ExpectedSeamComparisons,
    float MaximumSeamPositionError,
    float MaximumSeamNormalError,
    bool AtmosphereShell,
    bool WaterShell,
    bool CloudShell,
    float DisplayRadius);

/// <summary>
/// Bounded detailed representation of the currently focused planet for Orbit and
/// InterplanetaryTransit. It promotes the already verified Prototype-C cube sphere
/// mesh builder into the live Stage-2 star-system scene without keeping surface
/// collision/navigation resident.
/// </summary>
public partial class DetailedPlanetGlobeNode : Node3D
{
    public const int FaceResolution = 17;
    public const int ProductionTerrainMaterialVariants = 6;
    private readonly List<MeshInstance3D> _terrainFaces = new();
    private MeshInstance3D? _atmosphere;
    private MeshInstance3D? _water;
    private MeshInstance3D? _clouds;
    private DetailedPlanetGlobeDiagnostics? _diagnostics;

    public string PlanetId { get; private set; } = string.Empty;

    public int TerrainMaterialInstanceCount => _terrainFaces.Count;

    public DetailedPlanetGlobeDiagnostics Diagnostics =>
        _diagnostics ?? new DetailedPlanetGlobeDiagnostics(
            string.Empty, 0, 0, 0, 0, 0, 0.0f, 0.0f,
            false, false, false, 0.0f);

    public void Configure(StarSystemBodyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Kind != StarSystemBodyKind.Planet)
        {
            throw new InvalidOperationException(
                "Detailed planet globe requires a planet body definition.");
        }

        ClearVisuals();
        PlanetId = definition.BodyId;
        float radius = (float)Math.Max(420.0, definition.VisualRadius * 1.12);
        float relief = Math.Clamp(radius * ResolveReliefFraction(definition.Archetype),
            0.10f, 0.55f);
        int seed = unchecked((int)(definition.Seed ^ (definition.Seed >> 32)));
        CubeSphereBuildData build = CubeSphereMeshBuilder.Build(
            FaceResolution,
            radius,
            relief,
            0.21f,
            seed);
        for (int faceIndex = 0; faceIndex < build.Faces.Count; faceIndex++)
        {
            CubeSphereFaceData face = build.Faces[faceIndex];
            SurfaceTool surface = new();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetMaterial(BuildTerrainMaterial(definition.Archetype, faceIndex));
            for (int index = 0; index < face.Vertices.Count; index++)
            {
                surface.SetNormal(face.Normals[index]);
                surface.SetUV(face.Uvs[index]);
                surface.SetColor(BuildTerrainVertexColor(
                    definition.Archetype,
                    face.Vertices[index],
                    radius));
                surface.AddVertex(face.Vertices[index]);
            }
            foreach (int index in face.Indices)
            {
                surface.AddIndex(index);
            }

            MeshInstance3D meshNode = new()
            {
                Name = $"Terrain_{face.DisplayName.Replace('+', 'P').Replace('-', 'N')}",
                Mesh = surface.Commit()
            };
            AddChild(meshNode);
            _terrainFaces.Add(meshNode);
        }

        bool hasAtmosphere = !string.Equals(
            definition.Archetype,
            "barren",
            StringComparison.Ordinal);
        bool hasWater = definition.Archetype is "temperate" or "oceanic" or "frozen";
        bool hasClouds = definition.Archetype is
            "temperate" or "oceanic" or "frozen" or "toxic" or "volcanic";

        if (hasWater)
        {
            _water = BuildShell(
                "WaterShell",
                radius * 1.012f,
                ResolveWaterColor(definition.Archetype),
                unshaded: false,
                emissionEnergy: 0.05f,
                roughness: 0.28f);
            AddChild(_water);
        }

        if (hasAtmosphere)
        {
            _atmosphere = BuildShell(
                "AtmosphereShell",
                radius * 1.075f,
                ResolveAtmosphereColor(definition.Archetype),
                unshaded: false,
                emissionEnergy: 0.055f,
                roughness: 0.90f);
            AddChild(_atmosphere);
        }

        if (hasClouds)
        {
            _clouds = BuildShell(
                "CloudShell",
                radius * 1.035f,
                ResolveCloudColor(definition.Archetype),
                unshaded: false,
                emissionEnergy: 0.025f,
                roughness: 0.84f);
            AddChild(_clouds);
        }

        _diagnostics = new DetailedPlanetGlobeDiagnostics(
            definition.BodyId,
            build.Faces.Count,
            build.TotalVertices,
            build.TotalTriangles,
            build.SeamComparisons,
            build.ExpectedSeamComparisons,
            build.MaximumSeamPositionError,
            build.MaximumSeamNormalError,
            hasAtmosphere,
            hasWater,
            hasClouds,
            radius);
    }

    public override void _Process(double delta)
    {
        if (_clouds is not null && GodotObject.IsInstanceValid(_clouds))
        {
            _clouds.RotateY((float)(delta * 0.018));
        }
        RotateY((float)(delta * 0.0045));
    }

    private void ClearVisuals()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _terrainFaces.Clear();
        _atmosphere = null;
        _water = null;
        _clouds = null;
        _diagnostics = null;
    }

    private static MeshInstance3D BuildShell(
        string name,
        float radius,
        Color color,
        bool unshaded,
        float emissionEnergy,
        float roughness)
    {
        StandardMaterial3D material = new()
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = unshaded
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = roughness,
            MetallicSpecular = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = emissionEnergy > 0.0f,
            Emission = new Color(color.R, color.G, color.B, 1.0f),
            EmissionEnergyMultiplier = emissionEnergy
        };
        SphereMesh mesh = new()
        {
            Radius = radius,
            Height = radius * 2.0f,
            RadialSegments = 32,
            Rings = 18,
            Material = material
        };
        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    private static StandardMaterial3D BuildTerrainMaterial(
        string archetype,
        int faceIndex)
    {
        float roughness = archetype is "oceanic" or "frozen" ? 0.76f : 0.88f;
        // One bounded material instance per cube face keeps the face lifecycle
        // explicit while vertex colours carry seam-safe terrain breakup. Do not
        // tint by face index: that would make cube-sphere seams visible.
        _ = faceIndex;
        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = roughness,
            MetallicSpecular = 0.06f
        };
    }

    private static Color BuildTerrainVertexColor(
        string archetype,
        Vector3 position,
        float nominalRadius)
    {
        Color baseColor = archetype switch
        {
            "temperate" => new Color(0.18f, 0.44f, 0.22f),
            "desert" => new Color(0.62f, 0.39f, 0.17f),
            "frozen" => new Color(0.56f, 0.70f, 0.80f),
            "volcanic" => new Color(0.30f, 0.12f, 0.08f),
            "toxic" => new Color(0.31f, 0.48f, 0.12f),
            "radioactive" => new Color(0.42f, 0.45f, 0.17f),
            "oceanic" => new Color(0.10f, 0.24f, 0.34f),
            _ => new Color(0.34f, 0.31f, 0.28f)
        };
        Vector3 direction = position.Normalized();
        float broad = MathF.Sin(
            (direction.X * 13.7f) +
            (direction.Y * 9.1f) +
            (direction.Z * 17.3f));
        float fine = MathF.Sin(
            (direction.X * 31.0f) -
            (direction.Y * 23.0f) +
            (direction.Z * 27.0f));
        float relief = nominalRadius <= 0.0f
            ? 0.0f
            : Math.Clamp(
                (position.Length() - nominalRadius) /
                    Math.Max(0.01f, nominalRadius * 0.018f),
                -1.0f,
                1.0f);
        float shade = Math.Clamp(
            0.90f + (broad * 0.075f) + (fine * 0.035f) + (relief * 0.07f),
            0.72f,
            1.08f);
        return new Color(
            Math.Clamp(baseColor.R * shade, 0.0f, 1.0f),
            Math.Clamp(baseColor.G * shade, 0.0f, 1.0f),
            Math.Clamp(baseColor.B * shade, 0.0f, 1.0f),
            1.0f);
    }

    private static float ResolveReliefFraction(string archetype) => archetype switch
    {
        "volcanic" => 0.075f,
        "barren" => 0.065f,
        "frozen" => 0.055f,
        "desert" => 0.048f,
        _ => 0.042f
    };

    private static Color ResolveAtmosphereColor(string archetype) => archetype switch
    {
        "toxic" => new Color(0.55f, 0.82f, 0.20f, 0.12f),
        "volcanic" => new Color(0.95f, 0.35f, 0.16f, 0.09f),
        "frozen" => new Color(0.62f, 0.78f, 1.0f, 0.10f),
        _ => new Color(0.32f, 0.62f, 1.0f, 0.10f)
    };

    private static Color ResolveWaterColor(string archetype) => archetype switch
    {
        "frozen" => new Color(0.50f, 0.75f, 0.92f, 0.30f),
        "oceanic" => new Color(0.03f, 0.26f, 0.58f, 0.46f),
        _ => new Color(0.04f, 0.30f, 0.55f, 0.34f)
    };

    private static Color ResolveCloudColor(string archetype) => archetype switch
    {
        "toxic" => new Color(0.74f, 0.86f, 0.36f, 0.16f),
        "volcanic" => new Color(0.56f, 0.34f, 0.28f, 0.14f),
        "frozen" => new Color(0.86f, 0.92f, 1.0f, 0.15f),
        _ => new Color(0.92f, 0.94f, 1.0f, 0.13f)
    };
}
