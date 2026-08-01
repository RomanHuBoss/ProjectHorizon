using System;
using Godot;

public partial class TerrainChunk : StaticBody3D
{
    [Export(PropertyHint.Range, "3,257,2")]
    public int GridResolution { get; set; } = 33;

    [Export(PropertyHint.Range, "4.0,512.0,1.0")]
    public float ChunkSize { get; set; } = 32.0f;

    [Export(PropertyHint.Range, "0.0,64.0,0.1")]
    public float HeightScale { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.001,1.0,0.001")]
    public float NoiseFrequency { get; set; } = 0.035f;

    [Export]
    public int NoiseSeed { get; set; } = 20260801;

    [Export]
    public int ChunkX { get; set; }

    [Export]
    public int ChunkZ { get; set; }

    private MeshInstance3D _meshInstance = null!;
    private CollisionShape3D _collisionShape = null!;

    public override void _Ready()
    {
        _meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");

        GenerateChunk();
    }

    public void GenerateChunk()
    {
        int resolution = NormalizeResolution(GridResolution);
        float cellSize = ChunkSize / (resolution - 1);
        float halfSize = ChunkSize * 0.5f;
        ulong startedAtMicroseconds = Time.GetTicksUsec();

        FastNoiseLite noise = CreateNoise();
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float localX = (x * cellSize) - halfSize;
                float localZ = (z * cellSize) - halfSize;
                float sampleX = (ChunkX * ChunkSize) + (x * cellSize);
                float sampleZ = (ChunkZ * ChunkSize) + (z * cellSize);
                float height = noise.GetNoise2D(sampleX, sampleZ) * HeightScale;

                surfaceTool.SetUV(new Vector2(
                    x / (float)(resolution - 1),
                    z / (float)(resolution - 1)));
                surfaceTool.AddVertex(new Vector3(localX, height, localZ));
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
                // Такой порядок даёт нормаль вверх для поверхности в плоскости XZ.
                surfaceTool.AddIndex(topLeft);
                surfaceTool.AddIndex(bottomLeft);
                surfaceTool.AddIndex(topRight);

                surfaceTool.AddIndex(topRight);
                surfaceTool.AddIndex(bottomLeft);
                surfaceTool.AddIndex(bottomRight);
            }
        }

        surfaceTool.GenerateNormals();
        ArrayMesh mesh = surfaceTool.Commit();
        mesh.SurfaceSetMaterial(0, CreateTerrainMaterial());

        _meshInstance.Mesh = mesh;

        ConcavePolygonShape3D collisionShape = mesh.CreateTrimeshShape();
        collisionShape.BackfaceCollision = true;
        _collisionShape.Shape = collisionShape;

        int vertexCount = resolution * resolution;
        int triangleCount = (resolution - 1) * (resolution - 1) * 2;
        double elapsedMilliseconds =
            (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;

        GD.Print(
            $"TerrainChunk: generated chunk ({ChunkX}, {ChunkZ}); " +
            $"seed={NoiseSeed}; vertices={vertexCount}; " +
            $"triangles={triangleCount}; time={elapsedMilliseconds:F2} ms");
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
            Roughness = 0.92f
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
}
