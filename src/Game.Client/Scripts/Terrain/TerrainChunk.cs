using System;
using System.Collections.Generic;
using Godot;

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
        bool generateCollision)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        LodLevel = Math.Max(0, lodLevel);
        GridResolution = visualResolution;
        CollisionResolution = collisionResolution;
        ChunkSize = chunkSize;
        HeightScale = heightScale;
        NoiseFrequency = noiseFrequency;
        NoiseSeed = noiseSeed;
        SkirtDepth = Math.Max(0.0f, skirtDepth);
        GenerateCollision = generateCollision;
    }

    public bool SetDetailLevel(
        int lodLevel,
        int visualResolution,
        bool generateCollision)
    {
        int normalizedResolution = NormalizeResolution(visualResolution);
        bool changed =
            LodLevel != lodLevel ||
            EffectiveResolution != normalizedResolution ||
            GenerateCollision != generateCollision;

        LodLevel = Math.Max(0, lodLevel);
        GridResolution = normalizedResolution;
        GenerateCollision = generateCollision;

        if (changed && _meshInstance is not null && _collisionShape is not null)
        {
            GenerateChunk();
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
        EffectiveResolution = visualResolution;

        ulong startedAtMicroseconds = Time.GetTicksUsec();
        FastNoiseLite noise = CreateNoise();

        MeshData topData = BuildTopSurface(visualResolution, noise);
        MeshData visualData = BuildVisualSurfaceWithSkirts(topData, visualResolution);
        ArrayMesh visualMesh = BuildMesh(visualData);
        visualMesh.SurfaceSetMaterial(0, CreateTerrainMaterial());
        _meshInstance.Mesh = visualMesh;

        if (GenerateCollision)
        {
            MeshData collisionData = collisionResolution == visualResolution
                ? topData
                : BuildTopSurface(collisionResolution, noise);

            ArrayMesh collisionMesh = BuildMesh(collisionData);
            ConcavePolygonShape3D collisionShape = collisionMesh.CreateTrimeshShape();
            collisionShape.BackfaceCollision = true;
            _collisionShape.Shape = collisionShape;
            _collisionShape.Disabled = false;
        }
        else
        {
            _collisionShape.Shape = null;
            _collisionShape.Disabled = true;
        }

        int topVertexCount = visualResolution * visualResolution;
        int topTriangleCount =
            (visualResolution - 1) * (visualResolution - 1) * 2;
        int skirtTriangleCount = (visualResolution - 1) * 4 * 2;
        double elapsedMilliseconds =
            (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;

        string collisionInfo = GenerateCollision
            ? $"{collisionResolution}x{collisionResolution}"
            : "none";

        GD.Print(
            $"TerrainChunk: generated chunk ({ChunkX}, {ChunkZ}); " +
            $"lod={LodLevel}; seed={NoiseSeed}; " +
            $"vertices={topVertexCount}; triangles={topTriangleCount}; " +
            $"skirts={skirtTriangleCount}; collision={collisionInfo}; " +
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

    private MeshData BuildTopSurface(int resolution, FastNoiseLite noise)
    {
        float cellSize = ChunkSize / (resolution - 1);
        float halfSize = ChunkSize * 0.5f;
        MeshData data = new(resolution * resolution);

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float localX = (x * cellSize) - halfSize;
                float localZ = (z * cellSize) - halfSize;
                float sampleX = (ChunkX * ChunkSize) + (x * cellSize);
                float sampleZ = (ChunkZ * ChunkSize) + (z * cellSize);
                float height = noise.GetNoise2D(sampleX, sampleZ) * HeightScale;

                data.Vertices.Add(new Vector3(localX, height, localZ));
                data.Uvs.Add(new Vector2(
                    x / (float)(resolution - 1),
                    z / (float)(resolution - 1)));
            }
        }

        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int topLeft = (z * resolution) + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + resolution;
                int bottomRight = bottomLeft + 1;

                // В Godot лицевой стороной считается обход по часовой стрелке.
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

    private MeshData BuildVisualSurfaceWithSkirts(
        MeshData topData,
        int resolution)
    {
        MeshData visualData = new(topData.Vertices.Count + (resolution * 16));
        visualData.Vertices.AddRange(topData.Vertices);
        visualData.Uvs.AddRange(topData.Uvs);
        visualData.Indices.AddRange(topData.Indices);

        if (SkirtDepth <= 0.0f)
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

        AppendSkirt(visualData, northEdge);
        AppendSkirt(visualData, southEdge);
        AppendSkirt(visualData, westEdge);
        AppendSkirt(visualData, eastEdge);

        return visualData;
    }

    private void AppendSkirt(MeshData data, IReadOnlyList<int> edgeIndices)
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
            surfaceTool.SetUV(data.Uvs[i]);
            surfaceTool.AddVertex(data.Vertices[i]);
        }

        foreach (int index in data.Indices)
        {
            surfaceTool.AddIndex(index);
        }

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
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
            Uvs = new List<Vector2>(vertexCapacity);
            Indices = new List<int>();
        }

        public List<Vector3> Vertices { get; }

        public List<Vector2> Uvs { get; }

        public List<int> Indices { get; }
    }
}
