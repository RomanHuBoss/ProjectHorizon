using System;
using System.Collections.Generic;
using Godot;

[Flags]
public enum TerrainEdgeStitchMask
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
    All = North | East | South | West
}

public enum TerrainDebugViewMode
{
    HeightAndSlope = 0,
    Lod = 1,
    Normals = 2
}

public partial class TerrainChunk : StaticBody3D
{
    [Export(PropertyHint.Range, "3,257,2")]
    public int GridResolution { get; set; } = 33;

    [Export(PropertyHint.Range, "3,257,2")]
    public int CollisionResolution { get; set; } = 33;

    [Export(PropertyHint.Range, "4.0,512.0,1.0")]
    public float ChunkSize { get; set; } = 32.0f;

    [Export(PropertyHint.Range, "0.0,64.0,0.1")]
    public float HeightScale { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.001,1.0,0.001")]
    public float NoiseFrequency { get; set; } = 0.035f;

    [Export(PropertyHint.Range, "0.0,32.0,0.1")]
    public float SkirtDepth { get; set; } = 4.0f;

    [Export]
    public int NoiseSeed { get; set; } = 20260801;

    [Export]
    public int ChunkX { get; set; }

    [Export]
    public int ChunkZ { get; set; }

    [Export]
    public int LodLevel { get; set; }

    [Export]
    public bool GenerateCollision { get; set; } = true;

    [Export]
    public TerrainDebugViewMode DebugViewMode { get; set; } =
        TerrainDebugViewMode.HeightAndSlope;

    [Export]
    public bool ShowWorldGrid { get; set; } = true;

    [Export]
    public bool ShowWireframe { get; set; } = true;

    [Export]
    public bool ShowChunkBorders { get; set; } = true;

    [Export(PropertyHint.Range, "1.0,32.0,1.0")]
    public float DebugGridSpacing { get; set; } = 4.0f;

    public int EffectiveResolution { get; private set; } = 33;

    public TerrainEdgeStitchMask StitchMask { get; private set; }

    public TerrainEdgeStitchMask SkirtMask { get; private set; } =
        TerrainEdgeStitchMask.All;

    private MeshInstance3D? _meshInstance;
    private MeshInstance3D? _debugOverlay;
    private CollisionShape3D? _collisionShape;
    private TerrainMeshData? _lastTopSurface;

    public override void _Ready()
    {
        _meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        _debugOverlay = GetNodeOrNull<MeshInstance3D>("DebugOverlay");
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");

        if (_debugOverlay is null)
        {
            _debugOverlay = new MeshInstance3D
            {
                Name = "DebugOverlay"
            };
            AddChild(_debugOverlay);
        }

    }

    public void Configure(
        int chunkX,
        int chunkZ,
        int lodLevel,
        int visualResolution,
        int collisionResolution,
        float chunkSize,
        float heightScale,
        float noiseFrequency,
        int noiseSeed,
        float skirtDepth,
        bool generateCollision,
        TerrainEdgeStitchMask stitchMask,
        TerrainEdgeStitchMask skirtMask,
        TerrainDebugViewMode debugViewMode,
        bool showWorldGrid,
        bool showWireframe,
        bool showChunkBorders,
        float debugGridSpacing)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        LodLevel = Math.Max(0, lodLevel);
        GridResolution = NormalizeResolution(visualResolution);
        CollisionResolution = NormalizeResolution(collisionResolution);
        ChunkSize = chunkSize;
        HeightScale = heightScale;
        NoiseFrequency = noiseFrequency;
        NoiseSeed = noiseSeed;
        SkirtDepth = Math.Max(0.0f, skirtDepth);
        GenerateCollision = generateCollision;
        StitchMask = stitchMask;
        SkirtMask = skirtMask;
        DebugViewMode = debugViewMode;
        ShowWorldGrid = showWorldGrid;
        ShowWireframe = showWireframe;
        ShowChunkBorders = showChunkBorders;
        DebugGridSpacing = Math.Max(1.0f, debugGridSpacing);
    }

    public bool SetDebugVisualization(
        TerrainDebugViewMode debugViewMode,
        bool showWorldGrid,
        bool showWireframe,
        bool showChunkBorders,
        float debugGridSpacing)
    {
        float normalizedGridSpacing = Math.Max(1.0f, debugGridSpacing);
        bool changed =
            DebugViewMode != debugViewMode ||
            ShowWorldGrid != showWorldGrid ||
            ShowWireframe != showWireframe ||
            ShowChunkBorders != showChunkBorders ||
            !Mathf.IsEqualApprox(DebugGridSpacing, normalizedGridSpacing);

        DebugViewMode = debugViewMode;
        ShowWorldGrid = showWorldGrid;
        ShowWireframe = showWireframe;
        ShowChunkBorders = showChunkBorders;
        DebugGridSpacing = normalizedGridSpacing;

        if (changed && _lastTopSurface is not null)
        {
            RebuildVisualFromCachedData();
        }

        return changed;
    }

    public bool RequiresDetailLevel(
        int lodLevel,
        int visualResolution,
        bool generateCollision,
        TerrainEdgeStitchMask stitchMask,
        TerrainEdgeStitchMask skirtMask)
    {
        int normalizedResolution = NormalizeResolution(visualResolution);

        return LodLevel != Math.Max(0, lodLevel) ||
            EffectiveResolution != normalizedResolution ||
            GenerateCollision != generateCollision ||
            StitchMask != stitchMask ||
            SkirtMask != skirtMask;
    }

    public void ApplyGeneratedData(
        TerrainChunkBuildResult result,
        string operation)
    {
        if (_meshInstance is null || _collisionShape is null)
        {
            throw new InvalidOperationException(
                "TerrainChunk requires MeshInstance3D and CollisionShape3D children.");
        }

        TerrainChunkBuildRequest request = result.Request;

        if (request.ChunkX != ChunkX || request.ChunkZ != ChunkZ)
        {
            throw new InvalidOperationException(
                "Terrain build result does not match the configured chunk.");
        }

        ulong applyStartedAtMicroseconds = Time.GetTicksUsec();
        EffectiveResolution = request.VisualResolution;
        _lastTopSurface = result.VisualTopSurface;
        RebuildVisualFromCachedData();

        if (request.RebuildCollision)
        {
            ApplyCollisionData(result.CollisionTopSurface);
        }
        double mainThreadElapsedMilliseconds =
            (Time.GetTicksUsec() - applyStartedAtMicroseconds) / 1000.0;

        LogGeneration(
            operation,
            request.VisualResolution,
            request.CollisionResolution,
            true,
            request.RebuildCollision,
            result.WorkerElapsedMilliseconds,
            mainThreadElapsedMilliseconds);
    }

    private void RebuildVisualFromCachedData()
    {
        if (_meshInstance is null || _lastTopSurface is null)
        {
            return;
        }

        PopulateDiagnosticColors(_lastTopSurface);
        TerrainMeshData visualData = BuildVisualSurfaceWithSkirts(
            _lastTopSurface,
            EffectiveResolution,
            SkirtMask);
        ArrayMesh visualMesh = BuildMesh(visualData);
        visualMesh.SurfaceSetMaterial(0, CreateTerrainMaterial());
        _meshInstance.Mesh = visualMesh;
        GenerateDebugOverlay(_lastTopSurface, EffectiveResolution);
    }

    private void ApplyCollisionData(TerrainMeshData? collisionData)
    {
        if (_collisionShape is null)
        {
            throw new InvalidOperationException(
                "TerrainChunk requires a CollisionShape3D child.");
        }

        if (!GenerateCollision || collisionData is null)
        {
            _collisionShape.Shape = null;
            _collisionShape.Disabled = true;
            return;
        }

        // Collision remains full and unstitched. Adjacent collision chunks use
        // identical samples on their common border and therefore remain exact.
        ArrayMesh collisionMesh = BuildMesh(collisionData);
        ConcavePolygonShape3D collisionShape = collisionMesh.CreateTrimeshShape();
        collisionShape.BackfaceCollision = true;
        _collisionShape.Shape = collisionShape;
        _collisionShape.Disabled = false;
    }

    private void PopulateDiagnosticColors(TerrainMeshData data)
    {
        data.Colors.Clear();

        for (int i = 0; i < data.Vertices.Count; i++)
        {
            data.Colors.Add(CalculateDiagnosticColor(
                data.Vertices[i],
                data.Normals[i]));
        }
    }

    private void LogGeneration(
        string operation,
        int visualResolution,
        int collisionResolution,
        bool visualChanged,
        bool collisionChanged,
        double workerElapsedMilliseconds,
        double mainThreadElapsedMilliseconds)
    {
        int topVertexCount = visualResolution * visualResolution;
        int topTriangleCount =
            (visualResolution - 1) * (visualResolution - 1) * 2;
        int skirtTriangleCount = SkirtDepth > 0.0f
            ? (visualResolution - 1) * CountEdges(SkirtMask) * 2
            : 0;
        string collisionInfo = GenerateCollision
            ? $"{collisionResolution}x{collisionResolution}"
            : "none";

        GD.Print(
            $"TerrainChunk: {operation} chunk ({ChunkX}, {ChunkZ}); " +
            $"lod={LodLevel}; seed={NoiseSeed}; " +
            $"vertices={topVertexCount}; triangles={topTriangleCount}; " +
            $"stitch={StitchMask}; skirtEdges={SkirtMask}; " +
            $"skirts={skirtTriangleCount}; collision={collisionInfo}; " +
            $"visualChanged={visualChanged}; " +
            $"collisionChanged={collisionChanged}; " +
            $"worker={workerElapsedMilliseconds:F2} ms; " +
            $"main={mainThreadElapsedMilliseconds:F2} ms");
    }

    public void ReleaseGeneratedResources()
    {
        _lastTopSurface = null;
        if (_meshInstance is not null)
        {
            _meshInstance.Mesh = null;
        }

        if (_debugOverlay is not null)
        {
            _debugOverlay.Mesh = null;
            _debugOverlay.Visible = false;
        }

        if (_collisionShape is not null)
        {
            _collisionShape.Shape = null;
            _collisionShape.Disabled = true;
        }
    }

    private TerrainMeshData BuildVisualSurfaceWithSkirts(
        TerrainMeshData topData,
        int resolution,
        TerrainEdgeStitchMask skirtMask)
    {
        TerrainMeshData visualData = new(topData.Vertices.Count + (resolution * 16));
        visualData.Vertices.AddRange(topData.Vertices);
        visualData.Normals.AddRange(topData.Normals);
        visualData.Uvs.AddRange(topData.Uvs);
        visualData.Colors.AddRange(topData.Colors);
        visualData.Indices.AddRange(topData.Indices);

        if (SkirtDepth <= 0.0f ||
            skirtMask == TerrainEdgeStitchMask.None)
        {
            return visualData;
        }

        List<int> northEdge = new(resolution);
        List<int> southEdge = new(resolution);
        List<int> westEdge = new(resolution);
        List<int> eastEdge = new(resolution);

        for (int i = 0; i < resolution; i++)
        {
            northEdge.Add(i);
            southEdge.Add(((resolution - 1) * resolution) + i);
            westEdge.Add(i * resolution);
            eastEdge.Add((i * resolution) + resolution - 1);
        }

        if ((skirtMask & TerrainEdgeStitchMask.North) != 0)
        {
            AppendSkirt(visualData, northEdge, Vector3.Forward);
        }

        if ((skirtMask & TerrainEdgeStitchMask.South) != 0)
        {
            AppendSkirt(visualData, southEdge, Vector3.Back);
        }

        if ((skirtMask & TerrainEdgeStitchMask.West) != 0)
        {
            AppendSkirt(visualData, westEdge, Vector3.Left);
        }

        if ((skirtMask & TerrainEdgeStitchMask.East) != 0)
        {
            AppendSkirt(visualData, eastEdge, Vector3.Right);
        }

        return visualData;
    }

    private void AppendSkirt(
        TerrainMeshData data,
        IReadOnlyList<int> edgeIndices,
        Vector3 outwardNormal)
    {
        for (int i = 0; i < edgeIndices.Count - 1; i++)
        {
            int firstTopIndex = edgeIndices[i];
            int secondTopIndex = edgeIndices[i + 1];
            Vector3 firstTop = data.Vertices[firstTopIndex];
            Vector3 secondTop = data.Vertices[secondTopIndex];
            Vector3 firstBottom = firstTop - (Vector3.Up * SkirtDepth);
            Vector3 secondBottom = secondTop - (Vector3.Up * SkirtDepth);
            Vector2 firstUv = data.Uvs[firstTopIndex];
            Vector2 secondUv = data.Uvs[secondTopIndex];
            Color firstColor = data.Colors[firstTopIndex];
            Color secondColor = data.Colors[secondTopIndex];
            Color firstBottomColor = firstColor.Darkened(0.45f);
            Color secondBottomColor = secondColor.Darkened(0.45f);

            int baseIndex = data.Vertices.Count;
            data.Vertices.Add(firstTop);
            data.Vertices.Add(secondTop);
            data.Vertices.Add(firstBottom);
            data.Vertices.Add(secondBottom);

            data.Normals.Add(outwardNormal);
            data.Normals.Add(outwardNormal);
            data.Normals.Add(outwardNormal);
            data.Normals.Add(outwardNormal);

            data.Uvs.Add(firstUv);
            data.Uvs.Add(secondUv);
            data.Uvs.Add(firstUv);
            data.Uvs.Add(secondUv);

            data.Colors.Add(firstColor);
            data.Colors.Add(secondColor);
            data.Colors.Add(firstBottomColor);
            data.Colors.Add(secondBottomColor);

            data.Indices.Add(baseIndex);
            data.Indices.Add(baseIndex + 2);
            data.Indices.Add(baseIndex + 1);

            data.Indices.Add(baseIndex + 1);
            data.Indices.Add(baseIndex + 2);
            data.Indices.Add(baseIndex + 3);
        }
    }

    private static ArrayMesh BuildMesh(TerrainMeshData data)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < data.Vertices.Count; i++)
        {
            surfaceTool.SetNormal(data.Normals[i]);
            surfaceTool.SetUV(data.Uvs[i]);
            surfaceTool.SetColor(data.Colors.Count > i
                ? data.Colors[i]
                : Colors.White);
            surfaceTool.AddVertex(data.Vertices[i]);
        }

        foreach (int index in data.Indices)
        {
            surfaceTool.AddIndex(index);
        }

        return surfaceTool.Commit();
    }

    private StandardMaterial3D CreateTerrainMaterial()
    {
        bool useUnshaded = DebugViewMode is
            TerrainDebugViewMode.Lod or TerrainDebugViewMode.Normals;

        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Roughness = 0.88f,
            MetallicSpecular = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = false,
            ShadingMode = useUnshaded
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel
        };
    }

    private static StandardMaterial3D CreateDebugLineMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Roughness = 1.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = false,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
    }

    private Color CalculateDiagnosticColor(
        Vector3 vertex,
        Vector3 normal)
    {
        return DebugViewMode switch
        {
            TerrainDebugViewMode.Lod => CalculateLodColor(vertex),
            TerrainDebugViewMode.Normals => new Color(
                (normal.X * 0.5f) + 0.5f,
                (normal.Y * 0.5f) + 0.5f,
                (normal.Z * 0.5f) + 0.5f,
                1.0f),
            _ => CalculateHeightSlopeColor(vertex.Y, normal)
        };
    }

    private Color CalculateHeightSlopeColor(
        float height,
        Vector3 normal)
    {
        float heightRange = Math.Max(0.001f, HeightScale);
        float normalizedHeight = Mathf.Clamp(
            (height / (heightRange * 2.0f)) + 0.5f,
            0.0f,
            1.0f);
        float steepness = Mathf.Clamp(1.0f - normal.Y, 0.0f, 1.0f);
        Color low = new(0.12f, 0.32f, 0.46f);
        Color middle = new(0.22f, 0.62f, 0.28f);
        Color high = new(0.72f, 0.68f, 0.46f);
        Color rock = new(0.46f, 0.43f, 0.42f);
        Color heightColor = normalizedHeight < 0.5f
            ? low.Lerp(middle, normalizedHeight * 2.0f)
            : middle.Lerp(high, (normalizedHeight - 0.5f) * 2.0f);
        float rockBlend = Mathf.SmoothStep(0.18f, 0.62f, steepness);
        Color result = heightColor.Lerp(rock, rockBlend);
        Color lodTint = LodLevel == 0
            ? new Color(0.15f, 0.68f, 0.82f)
            : new Color(0.92f, 0.49f, 0.16f);

        return result.Lerp(lodTint, LodLevel == 0 ? 0.08f : 0.13f);
    }

    private Color CalculateLodColor(Vector3 vertex)
    {
        bool alternate = ((ChunkX + ChunkZ) & 1) != 0;
        Color baseColor = LodLevel == 0
            ? new Color(0.08f, 0.76f, 0.94f)
            : new Color(1.0f, 0.48f, 0.10f);

        if (alternate)
        {
            baseColor = baseColor.Darkened(0.16f);
        }

        float heightFactor = Mathf.Clamp(
            (vertex.Y / Math.Max(0.001f, HeightScale) + 1.0f) * 0.5f,
            0.0f,
            1.0f);
        return baseColor.Lightened(heightFactor * 0.12f);
    }

    private void GenerateDebugOverlay(
        TerrainMeshData topData,
        int resolution)
    {
        if (_debugOverlay is null)
        {
            return;
        }

        bool showAnyOverlay =
            ShowWorldGrid || ShowWireframe || ShowChunkBorders;

        if (!showAnyOverlay)
        {
            _debugOverlay.Mesh = null;
            _debugOverlay.Visible = false;
            return;
        }

        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Lines);
        float elevation = Math.Max(0.025f, ChunkSize * 0.0012f);
        Color wireColor = LodLevel == 0
            ? new Color(0.05f, 0.24f, 0.30f)
            : new Color(0.34f, 0.14f, 0.03f);
        Color gridColor = new(0.92f, 0.94f, 0.98f);
        Color borderColor = LodLevel == 0
            ? new Color(0.0f, 0.95f, 1.0f)
            : new Color(1.0f, 0.70f, 0.05f);

        if (ShowWireframe)
        {
            AppendWireframeLines(
                surfaceTool,
                topData,
                resolution,
                wireColor,
                elevation);
        }

        if (ShowWorldGrid)
        {
            AppendWorldGridLines(
                surfaceTool,
                topData,
                resolution,
                gridColor,
                elevation * 1.35f);
        }

        if (ShowChunkBorders)
        {
            AppendChunkBorderLines(
                surfaceTool,
                topData,
                resolution,
                borderColor,
                elevation * 1.8f);
        }

        ArrayMesh overlayMesh = surfaceTool.Commit();
        overlayMesh.SurfaceSetMaterial(0, CreateDebugLineMaterial());
        _debugOverlay.Mesh = overlayMesh;
        _debugOverlay.Visible = true;
    }

    private static void AppendWireframeLines(
        SurfaceTool surfaceTool,
        TerrainMeshData data,
        int resolution,
        Color color,
        float elevation)
    {
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int first = (z * resolution) + x;
                AppendDebugLine(
                    surfaceTool,
                    data,
                    first,
                    first + 1,
                    color,
                    elevation);
            }
        }

        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution - 1; z++)
            {
                int first = (z * resolution) + x;
                AppendDebugLine(
                    surfaceTool,
                    data,
                    first,
                    first + resolution,
                    color,
                    elevation);
            }
        }

        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int topRight = (z * resolution) + x + 1;
                int bottomLeft = ((z + 1) * resolution) + x;
                AppendDebugLine(
                    surfaceTool,
                    data,
                    topRight,
                    bottomLeft,
                    color,
                    elevation);
            }
        }
    }

    private void AppendWorldGridLines(
        SurfaceTool surfaceTool,
        TerrainMeshData data,
        int resolution,
        Color color,
        float elevation)
    {
        float spacing = Math.Max(1.0f, DebugGridSpacing);
        float cellSize = ChunkSize / (resolution - 1);
        float tolerance = Math.Max(0.001f, cellSize * 0.10f);

        for (int x = 0; x < resolution; x++)
        {
            float globalX = (ChunkX * ChunkSize) + (x * cellSize);

            if (!IsGridCoordinate(globalX, spacing, tolerance))
            {
                continue;
            }

            for (int z = 0; z < resolution - 1; z++)
            {
                int first = (z * resolution) + x;
                AppendDebugLine(
                    surfaceTool,
                    data,
                    first,
                    first + resolution,
                    color,
                    elevation);
            }
        }

        for (int z = 0; z < resolution; z++)
        {
            float globalZ = (ChunkZ * ChunkSize) + (z * cellSize);

            if (!IsGridCoordinate(globalZ, spacing, tolerance))
            {
                continue;
            }

            for (int x = 0; x < resolution - 1; x++)
            {
                int first = (z * resolution) + x;
                AppendDebugLine(
                    surfaceTool,
                    data,
                    first,
                    first + 1,
                    color,
                    elevation);
            }
        }
    }

    private static void AppendChunkBorderLines(
        SurfaceTool surfaceTool,
        TerrainMeshData data,
        int resolution,
        Color color,
        float elevation)
    {
        for (int i = 0; i < resolution - 1; i++)
        {
            AppendDebugLine(
                surfaceTool,
                data,
                i,
                i + 1,
                color,
                elevation);

            int southStart = (resolution - 1) * resolution;
            AppendDebugLine(
                surfaceTool,
                data,
                southStart + i,
                southStart + i + 1,
                color,
                elevation);

            AppendDebugLine(
                surfaceTool,
                data,
                i * resolution,
                (i + 1) * resolution,
                color,
                elevation);

            int eastColumn = resolution - 1;
            AppendDebugLine(
                surfaceTool,
                data,
                (i * resolution) + eastColumn,
                ((i + 1) * resolution) + eastColumn,
                color,
                elevation);
        }
    }

    private static void AppendDebugLine(
        SurfaceTool surfaceTool,
        TerrainMeshData data,
        int firstIndex,
        int secondIndex,
        Color color,
        float elevation)
    {
        Vector3 first = data.Vertices[firstIndex] +
            (data.Normals[firstIndex] * elevation);
        Vector3 second = data.Vertices[secondIndex] +
            (data.Normals[secondIndex] * elevation);

        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(first);
        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(second);
    }

    private static bool IsGridCoordinate(
        float value,
        float spacing,
        float tolerance)
    {
        float nearest = Mathf.Round(value / spacing) * spacing;
        return Mathf.Abs(value - nearest) <= tolerance;
    }

    private static int CountEdges(TerrainEdgeStitchMask mask)
    {
        int count = 0;

        if ((mask & TerrainEdgeStitchMask.North) != 0)
        {
            count++;
        }

        if ((mask & TerrainEdgeStitchMask.East) != 0)
        {
            count++;
        }

        if ((mask & TerrainEdgeStitchMask.South) != 0)
        {
            count++;
        }

        if ((mask & TerrainEdgeStitchMask.West) != 0)
        {
            count++;
        }

        return count;
    }

    private static int NormalizeResolution(int requestedResolution)
    {
        int resolution = Math.Clamp(requestedResolution, 3, 257);

        if (resolution % 2 == 0)
        {
            resolution = resolution < 257
                ? resolution + 1
                : resolution - 1;
        }

        return resolution;
    }

}
