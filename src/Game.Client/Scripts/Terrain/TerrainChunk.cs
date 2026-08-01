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

    public int EffectiveResolution { get; private set; } = 33;

    public TerrainEdgeStitchMask StitchMask { get; private set; }

    public TerrainEdgeStitchMask SkirtMask { get; private set; } =
        TerrainEdgeStitchMask.All;

    private MeshInstance3D? _meshInstance;
    private CollisionShape3D? _collisionShape;

    public override void _Ready()
    {
        _meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");

        GenerateChunk();
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
        TerrainEdgeStitchMask skirtMask)
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

    public bool SetDetailLevel(
        int lodLevel,
        int visualResolution,
        bool generateCollision,
        TerrainEdgeStitchMask stitchMask,
        TerrainEdgeStitchMask skirtMask)
    {
        int normalizedLodLevel = Math.Max(0, lodLevel);
        int normalizedResolution = NormalizeResolution(visualResolution);
        bool visualChanged =
            LodLevel != normalizedLodLevel ||
            EffectiveResolution != normalizedResolution ||
            StitchMask != stitchMask ||
            SkirtMask != skirtMask;
        bool collisionChanged = GenerateCollision != generateCollision;
        bool changed = visualChanged || collisionChanged;

        LodLevel = normalizedLodLevel;
        GridResolution = normalizedResolution;
        GenerateCollision = generateCollision;
        StitchMask = stitchMask;
        SkirtMask = skirtMask;

        if (changed && _meshInstance is not null && _collisionShape is not null)
        {
            ulong startedAtMicroseconds = Time.GetTicksUsec();
            FastNoiseLite noise = CreateNoise();
            int collisionResolution = NormalizeResolution(CollisionResolution);

            if (visualChanged)
            {
                GenerateVisualMesh(noise, normalizedResolution);
            }

            if (collisionChanged)
            {
                UpdateCollisionShape(noise, collisionResolution);
            }

            LogGeneration(
                "updated",
                normalizedResolution,
                collisionResolution,
                visualChanged,
                collisionChanged,
                startedAtMicroseconds);
        }

        return changed;
    }

    public void GenerateChunk()
    {
        if (_meshInstance is null || _collisionShape is null)
        {
            throw new InvalidOperationException(
                "TerrainChunk requires MeshInstance3D and CollisionShape3D children.");
        }

        int visualResolution = NormalizeResolution(GridResolution);
        int collisionResolution = NormalizeResolution(CollisionResolution);
        ulong startedAtMicroseconds = Time.GetTicksUsec();
        FastNoiseLite noise = CreateNoise();

        GenerateVisualMesh(noise, visualResolution);
        UpdateCollisionShape(noise, collisionResolution);
        LogGeneration(
            "generated",
            visualResolution,
            collisionResolution,
            true,
            true,
            startedAtMicroseconds);
    }

    private void GenerateVisualMesh(
        FastNoiseLite noise,
        int visualResolution)
    {
        if (_meshInstance is null)
        {
            throw new InvalidOperationException(
                "TerrainChunk requires a MeshInstance3D child.");
        }

        EffectiveResolution = visualResolution;
        MeshData topData = BuildTopSurface(
            visualResolution,
            noise,
            StitchMask);
        MeshData visualData = BuildVisualSurfaceWithSkirts(
            topData,
            visualResolution,
            SkirtMask);
        ArrayMesh visualMesh = BuildMesh(visualData);
        visualMesh.SurfaceSetMaterial(0, CreateTerrainMaterial());
        _meshInstance.Mesh = visualMesh;
    }

    private void UpdateCollisionShape(
        FastNoiseLite noise,
        int collisionResolution)
    {
        if (_collisionShape is null)
        {
            throw new InvalidOperationException(
                "TerrainChunk requires a CollisionShape3D child.");
        }

        if (!GenerateCollision)
        {
            _collisionShape.Shape = null;
            _collisionShape.Disabled = true;
            return;
        }

        // Collision remains full and unstitched. Adjacent collision chunks use
        // identical samples on their common border and therefore remain exact.
        MeshData collisionData = BuildTopSurface(
            collisionResolution,
            noise,
            TerrainEdgeStitchMask.None);
        ArrayMesh collisionMesh = BuildMesh(collisionData);
        ConcavePolygonShape3D collisionShape = collisionMesh.CreateTrimeshShape();
        collisionShape.BackfaceCollision = true;
        _collisionShape.Shape = collisionShape;
        _collisionShape.Disabled = false;
    }

    private void LogGeneration(
        string operation,
        int visualResolution,
        int collisionResolution,
        bool visualChanged,
        bool collisionChanged,
        ulong startedAtMicroseconds)
    {
        int topVertexCount = visualResolution * visualResolution;
        int topTriangleCount =
            (visualResolution - 1) * (visualResolution - 1) * 2;
        int skirtTriangleCount = SkirtDepth > 0.0f
            ? (visualResolution - 1) * CountEdges(SkirtMask) * 2
            : 0;
        double elapsedMilliseconds =
            (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;
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
            $"time={elapsedMilliseconds:F2} ms");
    }

    public void ReleaseGeneratedResources()
    {
        if (_meshInstance is not null)
        {
            _meshInstance.Mesh = null;
        }

        if (_collisionShape is not null)
        {
            _collisionShape.Shape = null;
            _collisionShape.Disabled = true;
        }
    }

    private MeshData BuildTopSurface(
        int resolution,
        FastNoiseLite noise,
        TerrainEdgeStitchMask stitchMask)
    {
        float cellSize = ChunkSize / (resolution - 1);
        float halfSize = ChunkSize * 0.5f;
        float normalSampleStep = ChunkSize /
            Math.Max(2, NormalizeResolution(CollisionResolution) - 1);
        MeshData data = new(resolution * resolution);

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float localX = (x * cellSize) - halfSize;
                float localZ = (z * cellSize) - halfSize;
                float sampleX = (ChunkX * ChunkSize) + (x * cellSize);
                float sampleZ = (ChunkZ * ChunkSize) + (z * cellSize);
                float height = SampleHeight(noise, sampleX, sampleZ);

                data.Vertices.Add(new Vector3(localX, height, localZ));
                data.Normals.Add(CalculateGlobalNormal(
                    noise,
                    sampleX,
                    sampleZ,
                    normalSampleStep));
                data.Uvs.Add(new Vector2(
                    x / (float)(resolution - 1),
                    z / (float)(resolution - 1)));
            }
        }

        ApplyEdgeStitching(data, resolution, stitchMask);

        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int topLeft = (z * resolution) + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + resolution;
                int bottomRight = bottomLeft + 1;

                // Godot treats clockwise winding as the front face.
                data.Indices.Add(topLeft);
                data.Indices.Add(bottomLeft);
                data.Indices.Add(topRight);

                data.Indices.Add(topRight);
                data.Indices.Add(bottomLeft);
                data.Indices.Add(bottomRight);
            }
        }

        return data;
    }

    private static void ApplyEdgeStitching(
        MeshData data,
        int resolution,
        TerrainEdgeStitchMask stitchMask)
    {
        if (stitchMask == TerrainEdgeStitchMask.None || resolution < 5)
        {
            return;
        }

        // LOD0 has one extra midpoint between each pair of LOD1 edge vertices.
        // Snapping every odd edge vertex to the linear LOD1 segment removes the
        // geometric T-junction while retaining the high-detail interior.
        if ((stitchMask & TerrainEdgeStitchMask.North) != 0)
        {
            for (int x = 1; x < resolution - 1; x += 2)
            {
                StitchMidpoint(data, x, x - 1, x + 1);
            }
        }

        if ((stitchMask & TerrainEdgeStitchMask.South) != 0)
        {
            int rowStart = (resolution - 1) * resolution;

            for (int x = 1; x < resolution - 1; x += 2)
            {
                StitchMidpoint(
                    data,
                    rowStart + x,
                    rowStart + x - 1,
                    rowStart + x + 1);
            }
        }

        if ((stitchMask & TerrainEdgeStitchMask.West) != 0)
        {
            for (int z = 1; z < resolution - 1; z += 2)
            {
                StitchMidpoint(
                    data,
                    z * resolution,
                    (z - 1) * resolution,
                    (z + 1) * resolution);
            }
        }

        if ((stitchMask & TerrainEdgeStitchMask.East) != 0)
        {
            int column = resolution - 1;

            for (int z = 1; z < resolution - 1; z += 2)
            {
                StitchMidpoint(
                    data,
                    (z * resolution) + column,
                    ((z - 1) * resolution) + column,
                    ((z + 1) * resolution) + column);
            }
        }
    }

    private static void StitchMidpoint(
        MeshData data,
        int midpointIndex,
        int firstIndex,
        int secondIndex)
    {
        Vector3 midpoint = data.Vertices[midpointIndex];
        midpoint.Y = (data.Vertices[firstIndex].Y + data.Vertices[secondIndex].Y) * 0.5f;
        data.Vertices[midpointIndex] = midpoint;

        Vector3 blendedNormal =
            data.Normals[firstIndex] + data.Normals[secondIndex];
        data.Normals[midpointIndex] = blendedNormal.LengthSquared() > 0.000001f
            ? blendedNormal.Normalized()
            : Vector3.Up;
    }

    private MeshData BuildVisualSurfaceWithSkirts(
        MeshData topData,
        int resolution,
        TerrainEdgeStitchMask skirtMask)
    {
        MeshData visualData = new(topData.Vertices.Count + (resolution * 16));
        visualData.Vertices.AddRange(topData.Vertices);
        visualData.Normals.AddRange(topData.Normals);
        visualData.Uvs.AddRange(topData.Uvs);
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
        MeshData data,
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

            data.Indices.Add(baseIndex);
            data.Indices.Add(baseIndex + 2);
            data.Indices.Add(baseIndex + 1);

            data.Indices.Add(baseIndex + 1);
            data.Indices.Add(baseIndex + 2);
            data.Indices.Add(baseIndex + 3);
        }
    }

    private static ArrayMesh BuildMesh(MeshData data)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < data.Vertices.Count; i++)
        {
            surfaceTool.SetNormal(data.Normals[i]);
            surfaceTool.SetUV(data.Uvs[i]);
            surfaceTool.AddVertex(data.Vertices[i]);
        }

        foreach (int index in data.Indices)
        {
            surfaceTool.AddIndex(index);
        }

        return surfaceTool.Commit();
    }

    private float SampleHeight(
        FastNoiseLite noise,
        float sampleX,
        float sampleZ)
    {
        return noise.GetNoise2D(sampleX, sampleZ) * HeightScale;
    }

    private Vector3 CalculateGlobalNormal(
        FastNoiseLite noise,
        float sampleX,
        float sampleZ,
        float sampleStep)
    {
        float left = SampleHeight(noise, sampleX - sampleStep, sampleZ);
        float right = SampleHeight(noise, sampleX + sampleStep, sampleZ);
        float north = SampleHeight(noise, sampleX, sampleZ - sampleStep);
        float south = SampleHeight(noise, sampleX, sampleZ + sampleStep);

        Vector3 normal = new(
            left - right,
            sampleStep * 2.0f,
            north - south);

        return normal.LengthSquared() > 0.000001f
            ? normal.Normalized()
            : Vector3.Up;
    }

    private FastNoiseLite CreateNoise()
    {
        return new FastNoiseLite
        {
            Seed = NoiseSeed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            Frequency = NoiseFrequency,
            FractalOctaves = 5,
            FractalGain = 0.5f,
            FractalLacunarity = 2.0f
        };
    }

    private static StandardMaterial3D CreateTerrainMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(0.28f, 0.56f, 0.22f),
            Roughness = 0.92f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
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

    private sealed class MeshData
    {
        public MeshData(int vertexCapacity)
        {
            Vertices = new List<Vector3>(vertexCapacity);
            Normals = new List<Vector3>(vertexCapacity);
            Uvs = new List<Vector2>(vertexCapacity);
            Indices = new List<int>();
        }

        public List<Vector3> Vertices { get; }

        public List<Vector3> Normals { get; }

        public List<Vector2> Uvs { get; }

        public List<int> Indices { get; }
    }
}
