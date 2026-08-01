using System;
using System.Collections.Generic;
using Godot;

public enum CubeSphereFaceId
{
    PositiveX = 0,
    NegativeX = 1,
    PositiveY = 2,
    NegativeY = 3,
    PositiveZ = 4,
    NegativeZ = 5
}

public sealed class CubeSphereFaceData
{
    public CubeSphereFaceData(
        CubeSphereFaceId faceId,
        string displayName,
        Color debugColor,
        int capacity)
    {
        FaceId = faceId;
        DisplayName = displayName;
        DebugColor = debugColor;
        Vertices = new List<Vector3>(capacity);
        Normals = new List<Vector3>(capacity);
        Uvs = new List<Vector2>(capacity);
        Indices = new List<int>(capacity * 6);
    }

    public CubeSphereFaceId FaceId { get; }

    public string DisplayName { get; }

    public Color DebugColor { get; }

    public List<Vector3> Vertices { get; }

    public List<Vector3> Normals { get; }

    public List<Vector2> Uvs { get; }

    public List<int> Indices { get; }
}

public sealed class CubeSphereBuildData
{
    public CubeSphereBuildData(
        IReadOnlyList<CubeSphereFaceData> faces,
        int resolution,
        int totalVertices,
        int totalTriangles,
        int seamComparisons,
        int expectedSeamComparisons,
        float maximumSeamPositionError,
        float maximumSeamNormalError)
    {
        Faces = faces;
        Resolution = resolution;
        TotalVertices = totalVertices;
        TotalTriangles = totalTriangles;
        SeamComparisons = seamComparisons;
        ExpectedSeamComparisons = expectedSeamComparisons;
        MaximumSeamPositionError = maximumSeamPositionError;
        MaximumSeamNormalError = maximumSeamNormalError;
    }

    public IReadOnlyList<CubeSphereFaceData> Faces { get; }

    public int Resolution { get; }

    public int TotalVertices { get; }

    public int TotalTriangles { get; }

    public int SeamComparisons { get; }

    public int ExpectedSeamComparisons { get; }

    public float MaximumSeamPositionError { get; }

    public float MaximumSeamNormalError { get; }
}

public static class CubeSphereMeshBuilder
{
    private const float SeamQuantization = 1_000_000.0f;

    private readonly record struct FaceBasis(
        CubeSphereFaceId Id,
        string DisplayName,
        Vector3 Normal,
        Vector3 AxisU,
        Vector3 AxisV,
        Color DebugColor);

    private readonly record struct SeamKey(int X, int Y, int Z);

    private readonly record struct SeamSample(Vector3 Position, Vector3 Normal);

    private static readonly FaceBasis[] FaceBases =
    {
        new(
            CubeSphereFaceId.PositiveX,
            "+X",
            Vector3.Right,
            Vector3.Forward,
            Vector3.Up,
            new Color(0.92f, 0.32f, 0.28f)),
        new(
            CubeSphereFaceId.NegativeX,
            "-X",
            Vector3.Left,
            Vector3.Back,
            Vector3.Up,
            new Color(0.25f, 0.73f, 0.96f)),
        new(
            CubeSphereFaceId.PositiveY,
            "+Y",
            Vector3.Up,
            Vector3.Right,
            Vector3.Forward,
            new Color(0.35f, 0.86f, 0.48f)),
        new(
            CubeSphereFaceId.NegativeY,
            "-Y",
            Vector3.Down,
            Vector3.Right,
            Vector3.Back,
            new Color(0.95f, 0.72f, 0.22f)),
        new(
            CubeSphereFaceId.PositiveZ,
            "+Z",
            Vector3.Back,
            Vector3.Right,
            Vector3.Up,
            new Color(0.72f, 0.38f, 0.94f)),
        new(
            CubeSphereFaceId.NegativeZ,
            "-Z",
            Vector3.Forward,
            Vector3.Left,
            Vector3.Up,
            new Color(0.96f, 0.48f, 0.73f))
    };

    public static CubeSphereBuildData Build(
        int requestedResolution,
        float radius,
        float heightAmplitude,
        float noiseFrequency,
        int noiseSeed)
    {
        int resolution = NormalizeResolution(requestedResolution);
        float normalizedRadius = Math.Max(1.0f, radius);
        float normalizedHeightAmplitude = Math.Max(0.0f, heightAmplitude);
        float normalizedNoiseFrequency = Math.Max(0.0001f, noiseFrequency);
        int verticesPerFace = resolution * resolution;
        int trianglesPerFace =
            (resolution - 1) * (resolution - 1) * 2;

        FastNoiseLite noise = new()
        {
            Seed = noiseSeed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
            Frequency = normalizedNoiseFrequency,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
            FractalLacunarity = 2.0f,
            FractalGain = 0.5f
        };

        List<CubeSphereFaceData> faces = new(FaceBases.Length);
        Dictionary<SeamKey, SeamSample> seamSamples = new();
        int seamComparisons = 0;
        float maximumSeamPositionError = 0.0f;
        float maximumSeamNormalError = 0.0f;

        foreach (FaceBasis basis in FaceBases)
        {
            CubeSphereFaceData face = BuildFace(
                basis,
                resolution,
                normalizedRadius,
                normalizedHeightAmplitude,
                noise,
                seamSamples,
                ref seamComparisons,
                ref maximumSeamPositionError,
                ref maximumSeamNormalError);
            faces.Add(face);
        }

        return new CubeSphereBuildData(
            faces,
            resolution,
            verticesPerFace * FaceBases.Length,
            trianglesPerFace * FaceBases.Length,
            seamComparisons,
            (12 * resolution) - 8,
            maximumSeamPositionError,
            maximumSeamNormalError);
    }

    private static CubeSphereFaceData BuildFace(
        FaceBasis basis,
        int resolution,
        float radius,
        float heightAmplitude,
        FastNoiseLite noise,
        Dictionary<SeamKey, SeamSample> seamSamples,
        ref int seamComparisons,
        ref float maximumSeamPositionError,
        ref float maximumSeamNormalError)
    {
        CubeSphereFaceData face = new(
            basis.Id,
            basis.DisplayName,
            basis.DebugColor,
            resolution * resolution);

        for (int y = 0; y < resolution; y++)
        {
            float v01 = y / (float)(resolution - 1);
            float v = (v01 * 2.0f) - 1.0f;

            for (int x = 0; x < resolution; x++)
            {
                float u01 = x / (float)(resolution - 1);
                float u = (u01 * 2.0f) - 1.0f;
                Vector3 cubePoint =
                    basis.Normal + (basis.AxisU * u) + (basis.AxisV * v);
                Vector3 radialNormal = cubePoint.Normalized();
                float sampledHeight = heightAmplitude <= 0.0f
                    ? 0.0f
                    : noise.GetNoise3D(
                        radialNormal.X * radius,
                        radialNormal.Y * radius,
                        radialNormal.Z * radius) * heightAmplitude;
                Vector3 position = radialNormal * (radius + sampledHeight);

                face.Vertices.Add(position);
                face.Normals.Add(radialNormal);
                face.Uvs.Add(new Vector2(u01, v01));

                if (x == 0 || x == resolution - 1 ||
                    y == 0 || y == resolution - 1)
                {
                    ValidateSeamSample(
                        radialNormal,
                        position,
                        seamSamples,
                        ref seamComparisons,
                        ref maximumSeamPositionError,
                        ref maximumSeamNormalError);
                }
            }
        }

        for (int y = 0; y < resolution - 1; y++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int topLeft = (y * resolution) + x;
                int topRight = topLeft + 1;
                int bottomLeft = ((y + 1) * resolution) + x;
                int bottomRight = bottomLeft + 1;

                face.Indices.Add(topLeft);
                face.Indices.Add(topRight);
                face.Indices.Add(bottomLeft);

                face.Indices.Add(topRight);
                face.Indices.Add(bottomRight);
                face.Indices.Add(bottomLeft);
            }
        }

        return face;
    }

    private static void ValidateSeamSample(
        Vector3 radialNormal,
        Vector3 position,
        Dictionary<SeamKey, SeamSample> seamSamples,
        ref int seamComparisons,
        ref float maximumSeamPositionError,
        ref float maximumSeamNormalError)
    {
        SeamKey key = new(
            (int)MathF.Round(radialNormal.X * SeamQuantization),
            (int)MathF.Round(radialNormal.Y * SeamQuantization),
            (int)MathF.Round(radialNormal.Z * SeamQuantization));

        if (!seamSamples.TryGetValue(key, out SeamSample existing))
        {
            seamSamples.Add(key, new SeamSample(position, radialNormal));
            return;
        }

        seamComparisons++;
        maximumSeamPositionError = Math.Max(
            maximumSeamPositionError,
            existing.Position.DistanceTo(position));
        maximumSeamNormalError = Math.Max(
            maximumSeamNormalError,
            existing.Normal.DistanceTo(radialNormal));
    }

    private static int NormalizeResolution(int requestedResolution)
    {
        int resolution = Math.Clamp(requestedResolution, 3, 257);
        return resolution % 2 == 0 ? resolution + 1 : resolution;
    }
}
