using System;
using System.Collections.Generic;
using System.Diagnostics;
using CancellationToken = System.Threading.CancellationToken;
using Godot;

public sealed class TerrainChunkBuildRequest
{
    public TerrainChunkBuildRequest(
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
        bool rebuildCollision,
        TerrainEdgeStitchMask stitchMask,
        TerrainEdgeStitchMask skirtMask,
        PlanetSurfaceTerrainProfile? planetSurfaceProfile = null,
        PlanetSurfaceCurvedPatchDescriptor? curvedPatch = null,
        int curvatureRevision = 0)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        LodLevel = Math.Max(0, lodLevel);
        VisualResolution = NormalizeResolution(visualResolution);
        CollisionResolution = NormalizeResolution(collisionResolution);
        ChunkSize = chunkSize;
        HeightScale = heightScale;
        NoiseFrequency = noiseFrequency;
        NoiseSeed = noiseSeed;
        SkirtDepth = Math.Max(0.0f, skirtDepth);
        GenerateCollision = generateCollision;
        RebuildCollision = rebuildCollision;
        StitchMask = stitchMask;
        SkirtMask = skirtMask;
        PlanetSurfaceProfile = planetSurfaceProfile;
        CurvedPatch = curvedPatch;
        CurvatureRevision = Math.Max(0, curvatureRevision);
    }

    public int ChunkX { get; }

    public int ChunkZ { get; }

    public int LodLevel { get; }

    public int VisualResolution { get; }

    public int CollisionResolution { get; }

    public float ChunkSize { get; }

    public float HeightScale { get; }

    public float NoiseFrequency { get; }

    public int NoiseSeed { get; }

    public float SkirtDepth { get; }

    public bool GenerateCollision { get; }

    public bool RebuildCollision { get; }

    public TerrainEdgeStitchMask StitchMask { get; }

    public TerrainEdgeStitchMask SkirtMask { get; }

    public PlanetSurfaceTerrainProfile? PlanetSurfaceProfile { get; }

    public PlanetSurfaceCurvedPatchDescriptor? CurvedPatch { get; }

    public int CurvatureRevision { get; }

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

public sealed class TerrainChunkBuildResult
{
    public TerrainChunkBuildResult(
        TerrainChunkBuildRequest request,
        TerrainMeshData visualTopSurface,
        TerrainMeshData? collisionTopSurface,
        double workerElapsedMilliseconds)
    {
        Request = request;
        VisualTopSurface = visualTopSurface;
        CollisionTopSurface = collisionTopSurface;
        WorkerElapsedMilliseconds = workerElapsedMilliseconds;
    }

    public TerrainChunkBuildRequest Request { get; }

    public TerrainMeshData VisualTopSurface { get; }

    public TerrainMeshData? CollisionTopSurface { get; }

    public double WorkerElapsedMilliseconds { get; }
}

public sealed class TerrainMeshData
{
    public TerrainMeshData(int vertexCapacity)
    {
        Vertices = new List<Vector3>(vertexCapacity);
        Normals = new List<Vector3>(vertexCapacity);
        Uvs = new List<Vector2>(vertexCapacity);
        Colors = new List<Color>(vertexCapacity);
        Indices = new List<int>();
    }

    public List<Vector3> Vertices { get; }

    public List<Vector3> Normals { get; }

    public List<Vector2> Uvs { get; }

    public List<Color> Colors { get; }

    public List<int> Indices { get; }
}

public static class TerrainChunkDataBuilder
{
    public static TerrainChunkBuildResult Build(
        TerrainChunkBuildRequest request,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        FastNoiseLite? noise = request.PlanetSurfaceProfile is null
            ? CreateNoise(request)
            : null;
        TerrainMeshData visualTopSurface = BuildTopSurface(
            request,
            request.VisualResolution,
            noise,
            request.StitchMask,
            cancellationToken);
        TerrainMeshData? collisionTopSurface = null;

        if (request.RebuildCollision && request.GenerateCollision)
        {
            cancellationToken.ThrowIfCancellationRequested();
            collisionTopSurface = BuildTopSurface(
                request,
                request.CollisionResolution,
                noise,
                TerrainEdgeStitchMask.None,
                cancellationToken);
        }

        stopwatch.Stop();
        return new TerrainChunkBuildResult(
            request,
            visualTopSurface,
            collisionTopSurface,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private static TerrainMeshData BuildTopSurface(
        TerrainChunkBuildRequest request,
        int resolution,
        FastNoiseLite? noise,
        TerrainEdgeStitchMask stitchMask,
        CancellationToken cancellationToken)
    {
        float cellSize = request.ChunkSize / (resolution - 1);
        float halfSize = request.ChunkSize * 0.5f;
        float normalSampleStep = request.ChunkSize /
            Math.Max(2, request.CollisionResolution - 1);
        TerrainMeshData data = new(resolution * resolution);

        for (int z = 0; z < resolution; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (int x = 0; x < resolution; x++)
            {
                float localX = (x * cellSize) - halfSize;
                float localZ = (z * cellSize) - halfSize;
                // Prototype B historically samples from the chunk minimum using
                // x/z cell coordinates. Planet-surface mode must sample at the
                // vertex's actual world position so TASK-156 grounding, streamed
                // collision and the fallback mesh describe the same terrain.
                float sampleX = request.PlanetSurfaceProfile is null
                    ? (request.ChunkX * request.ChunkSize) + (x * cellSize)
                    : (request.ChunkX * request.ChunkSize) + localX;
                float sampleZ = request.PlanetSurfaceProfile is null
                    ? (request.ChunkZ * request.ChunkSize) + (z * cellSize)
                    : (request.ChunkZ * request.ChunkSize) + localZ;
                float height = SampleSurfaceHeight(
                    request,
                    noise,
                    sampleX,
                    sampleZ);

                data.Vertices.Add(new Vector3(localX, height, localZ));
                data.Normals.Add(CalculateGlobalNormal(
                    request,
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
            cancellationToken.ThrowIfCancellationRequested();

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
        TerrainMeshData data,
        int resolution,
        TerrainEdgeStitchMask stitchMask)
    {
        if (stitchMask == TerrainEdgeStitchMask.None || resolution < 5)
        {
            return;
        }

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
        TerrainMeshData data,
        int midpointIndex,
        int firstIndex,
        int secondIndex)
    {
        Vector3 midpoint = data.Vertices[midpointIndex];
        midpoint.Y =
            (data.Vertices[firstIndex].Y + data.Vertices[secondIndex].Y) *
            0.5f;
        data.Vertices[midpointIndex] = midpoint;

        Vector3 blendedNormal =
            data.Normals[firstIndex] + data.Normals[secondIndex];
        data.Normals[midpointIndex] =
            blendedNormal.LengthSquared() > 0.000001f
                ? blendedNormal.Normalized()
                : Vector3.Up;
    }

    private static float SampleHeight(
        TerrainChunkBuildRequest request,
        FastNoiseLite? noise,
        float sampleX,
        float sampleZ)
    {
        if (request.PlanetSurfaceProfile is not null)
        {
            return (float)PlanetSurfaceTerrainRuntime.SampleHeight(
                request.PlanetSurfaceProfile,
                sampleX,
                sampleZ);
        }

        if (noise is null)
        {
            throw new InvalidOperationException(
                "Legacy terrain generation requires a FastNoiseLite instance.");
        }

        return noise.GetNoise2D(sampleX, sampleZ) * request.HeightScale;
    }


    private static float SampleSurfaceHeight(
        TerrainChunkBuildRequest request,
        FastNoiseLite? noise,
        float sampleX,
        float sampleZ)
    {
        float terrainHeight = SampleHeight(
            request,
            noise,
            sampleX,
            sampleZ);
        if (request.CurvedPatch is null)
        {
            return terrainHeight;
        }
        return terrainHeight - (float)request.CurvedPatch.TangentSagMeters(
            sampleX,
            sampleZ);
    }

    private static Vector3 CalculateGlobalNormal(
        TerrainChunkBuildRequest request,
        FastNoiseLite? noise,
        float sampleX,
        float sampleZ,
        float sampleStep)
    {
        float left = SampleSurfaceHeight(
            request,
            noise,
            sampleX - sampleStep,
            sampleZ);
        float right = SampleSurfaceHeight(
            request,
            noise,
            sampleX + sampleStep,
            sampleZ);
        float north = SampleSurfaceHeight(
            request,
            noise,
            sampleX,
            sampleZ - sampleStep);
        float south = SampleSurfaceHeight(
            request,
            noise,
            sampleX,
            sampleZ + sampleStep);

        Vector3 normal = new(
            left - right,
            sampleStep * 2.0f,
            north - south);

        return normal.LengthSquared() > 0.000001f
            ? normal.Normalized()
            : Vector3.Up;
    }

    private static FastNoiseLite CreateNoise(
        TerrainChunkBuildRequest request)
    {
        return new FastNoiseLite
        {
            Seed = request.NoiseSeed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            Frequency = request.NoiseFrequency,
            FractalOctaves = 5,
            FractalGain = 0.5f,
            FractalLacunarity = 2.0f
        };
    }
}
