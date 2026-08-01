using System;
using System.Collections.Generic;
using CancellationToken = System.Threading.CancellationToken;
using Godot;

public enum CubeSpherePatchEdge
{
    Left = 0,
    Right = 1,
    Bottom = 2,
    Top = 3
}

public readonly record struct CubeSpherePatchKey(
    CubeSphereFaceId FaceId,
    int Level,
    int X,
    int Y)
{
    public string DisplayName => $"{FaceId}_L{Level}_{X}_{Y}";
}

public sealed class CubeSpherePatchData
{
    public CubeSpherePatchData(
        CubeSpherePatchKey key,
        string faceDisplayName,
        Color debugColor,
        int capacity)
    {
        Key = key;
        FaceDisplayName = faceDisplayName;
        DebugColor = debugColor;
        Vertices = new List<Vector3>(capacity);
        Normals = new List<Vector3>(capacity);
        Uvs = new List<Vector2>(capacity);
        Indices = new List<int>(capacity * 6);
    }

    public CubeSpherePatchKey Key { get; }

    public string FaceDisplayName { get; }

    public Color DebugColor { get; }

    public List<Vector3> Vertices { get; }

    public List<Vector3> Normals { get; }

    public List<Vector2> Uvs { get; }

    public List<int> Indices { get; }

    public int TopVertexCount { get; internal set; }

    public int TopTriangleCount { get; internal set; }

    public int SkirtTriangleCount { get; internal set; }
}

public sealed class CubeSphereLodValidation
{
    public CubeSphereLodValidation(
        int atomicSegments,
        int openSegments,
        int nonManifoldSegments,
        int maximumNeighborLevelDelta,
        float maximumSeamPositionError)
    {
        AtomicSegments = atomicSegments;
        OpenSegments = openSegments;
        NonManifoldSegments = nonManifoldSegments;
        MaximumNeighborLevelDelta = maximumNeighborLevelDelta;
        MaximumSeamPositionError = maximumSeamPositionError;
    }

    public int AtomicSegments { get; }

    public int OpenSegments { get; }

    public int NonManifoldSegments { get; }

    public int MaximumNeighborLevelDelta { get; }

    public float MaximumSeamPositionError { get; }

    public bool Passed =>
        OpenSegments == 0 &&
        NonManifoldSegments == 0 &&
        MaximumNeighborLevelDelta <= 1 &&
        MaximumSeamPositionError <= 0.001f;
}

public static class CubeSpherePatchBuilder
{
    private const float DirectionQuantization = 1_000_000.0f;

    private readonly record struct FaceBasis(
        CubeSphereFaceId Id,
        string DisplayName,
        Vector3 Normal,
        Vector3 AxisU,
        Vector3 AxisV,
        Color DebugColor);

    private readonly record struct DirectionKey(int X, int Y, int Z) :
        IComparable<DirectionKey>
    {
        public int CompareTo(DirectionKey other)
        {
            int xComparison = X.CompareTo(other.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int yComparison = Y.CompareTo(other.Y);
            return yComparison != 0
                ? yComparison
                : Z.CompareTo(other.Z);
        }
    }

    private readonly record struct EdgeSegmentKey(
        DirectionKey Start,
        DirectionKey End);

    private readonly record struct EdgeOwner(
        CubeSpherePatchKey Key,
        int Level,
        Vector3 StartPosition,
        Vector3 EndPosition);

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

    public static IReadOnlyList<CubeSphereFaceId> FaceIds { get; } =
        new[]
        {
            CubeSphereFaceId.PositiveX,
            CubeSphereFaceId.NegativeX,
            CubeSphereFaceId.PositiveY,
            CubeSphereFaceId.NegativeY,
            CubeSphereFaceId.PositiveZ,
            CubeSphereFaceId.NegativeZ
        };

    public static CubeSpherePatchData BuildPatch(
        CubeSpherePatchKey key,
        int requestedResolution,
        float radius,
        float heightAmplitude,
        float noiseFrequency,
        int noiseSeed,
        float skirtDepth,
        CancellationToken cancellationToken = default)
    {
        int resolution = NormalizeResolution(requestedResolution);
        float normalizedRadius = Math.Max(1.0f, radius);
        float normalizedHeightAmplitude = Math.Max(0.0f, heightAmplitude);
        float normalizedNoiseFrequency = Math.Max(0.0001f, noiseFrequency);
        float normalizedSkirtDepth = Math.Max(0.05f, skirtDepth);
        FaceBasis basis = GetFaceBasis(key.FaceId);
        GetPatchBounds(key, out float uMin, out float uMax, out float vMin, out float vMax);

        FastNoiseLite noise = CreateNoise(normalizedNoiseFrequency, noiseSeed);
        int topVertexCapacity = resolution * resolution;
        CubeSpherePatchData patch = new(
            key,
            basis.DisplayName,
            basis.DebugColor,
            topVertexCapacity + (resolution * 4));

        for (int y = 0; y < resolution; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float v01 = y / (float)(resolution - 1);
            float v = Mathf.Lerp(vMin, vMax, v01);

            for (int x = 0; x < resolution; x++)
            {
                float u01 = x / (float)(resolution - 1);
                float u = Mathf.Lerp(uMin, uMax, u01);
                Vector3 radialNormal = FaceUvToDirection(basis, u, v);
                Vector3 position = SampleSurfacePosition(
                    radialNormal,
                    normalizedRadius,
                    normalizedHeightAmplitude,
                    noise);

                patch.Vertices.Add(position);
                patch.Normals.Add(radialNormal);
                patch.Uvs.Add(new Vector2(u01, v01));
            }
        }

        patch.TopVertexCount = patch.Vertices.Count;

        for (int y = 0; y < resolution - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < resolution - 1; x++)
            {
                int topLeft = (y * resolution) + x;
                int topRight = topLeft + 1;
                int bottomLeft = ((y + 1) * resolution) + x;
                int bottomRight = bottomLeft + 1;

                patch.Indices.Add(topLeft);
                patch.Indices.Add(topRight);
                patch.Indices.Add(bottomLeft);

                patch.Indices.Add(topRight);
                patch.Indices.Add(bottomRight);
                patch.Indices.Add(bottomLeft);
            }
        }

        patch.TopTriangleCount =
            (resolution - 1) * (resolution - 1) * 2;

        cancellationToken.ThrowIfCancellationRequested();
        AddSkirt(patch, BuildEdgeIndices(CubeSpherePatchEdge.Left, resolution), normalizedSkirtDepth);
        AddSkirt(patch, BuildEdgeIndices(CubeSpherePatchEdge.Right, resolution), normalizedSkirtDepth);
        AddSkirt(patch, BuildEdgeIndices(CubeSpherePatchEdge.Bottom, resolution), normalizedSkirtDepth);
        AddSkirt(patch, BuildEdgeIndices(CubeSpherePatchEdge.Top, resolution), normalizedSkirtDepth);

        return patch;
    }

    public static Vector3 GetPatchCenterDirection(CubeSpherePatchKey key)
    {
        FaceBasis basis = GetFaceBasis(key.FaceId);
        GetPatchBounds(key, out float uMin, out float uMax, out float vMin, out float vMax);
        return FaceUvToDirection(
            basis,
            (uMin + uMax) * 0.5f,
            (vMin + vMax) * 0.5f);
    }

    public static float GetPatchAngularRadiusRadians(CubeSpherePatchKey key)
    {
        FaceBasis basis = GetFaceBasis(key.FaceId);
        GetPatchBounds(
            key,
            out float uMin,
            out float uMax,
            out float vMin,
            out float vMax);
        Vector3 centerDirection = FaceUvToDirection(
            basis,
            (uMin + uMax) * 0.5f,
            (vMin + vMax) * 0.5f);
        float maximumAngle = 0.0f;
        maximumAngle = Math.Max(
            maximumAngle,
            GetAngularDistance(centerDirection, FaceUvToDirection(basis, uMin, vMin)));
        maximumAngle = Math.Max(
            maximumAngle,
            GetAngularDistance(centerDirection, FaceUvToDirection(basis, uMax, vMin)));
        maximumAngle = Math.Max(
            maximumAngle,
            GetAngularDistance(centerDirection, FaceUvToDirection(basis, uMin, vMax)));
        maximumAngle = Math.Max(
            maximumAngle,
            GetAngularDistance(centerDirection, FaceUvToDirection(basis, uMax, vMax)));
        return maximumAngle;
    }

    public static CubeSphereLodValidation ValidateTopology(
        IReadOnlyCollection<CubeSpherePatchKey> leaves,
        int maximumLevel,
        float radius,
        float heightAmplitude,
        float noiseFrequency,
        int noiseSeed)
    {
        int normalizedMaximumLevel = Math.Clamp(maximumLevel, 0, 12);
        FastNoiseLite noise = CreateNoise(
            Math.Max(0.0001f, noiseFrequency),
            noiseSeed);
        Dictionary<EdgeSegmentKey, List<EdgeOwner>> ownersBySegment = new();

        foreach (CubeSpherePatchKey leaf in leaves)
        {
            foreach (CubeSpherePatchEdge edge in Enum.GetValues<CubeSpherePatchEdge>())
            {
                AddAtomicEdgeOwners(
                    ownersBySegment,
                    leaf,
                    edge,
                    normalizedMaximumLevel,
                    radius,
                    heightAmplitude,
                    noise);
            }
        }

        int openSegments = 0;
        int nonManifoldSegments = 0;
        int maximumNeighborDelta = 0;
        float maximumSeamPositionError = 0.0f;

        foreach (List<EdgeOwner> owners in ownersBySegment.Values)
        {
            if (owners.Count < 2)
            {
                openSegments++;
                continue;
            }

            if (owners.Count > 2)
            {
                nonManifoldSegments++;
            }

            EdgeOwner reference = owners[0];
            for (int i = 1; i < owners.Count; i++)
            {
                EdgeOwner candidate = owners[i];
                maximumNeighborDelta = Math.Max(
                    maximumNeighborDelta,
                    Math.Abs(reference.Level - candidate.Level));
                maximumSeamPositionError = Math.Max(
                    maximumSeamPositionError,
                    reference.StartPosition.DistanceTo(candidate.StartPosition));
                maximumSeamPositionError = Math.Max(
                    maximumSeamPositionError,
                    reference.EndPosition.DistanceTo(candidate.EndPosition));
            }
        }

        return new CubeSphereLodValidation(
            ownersBySegment.Count,
            openSegments,
            nonManifoldSegments,
            maximumNeighborDelta,
            maximumSeamPositionError);
    }

    public static IReadOnlyCollection<CubeSpherePatchKey>
        FindLeavesRequiringBalance(
            IReadOnlyCollection<CubeSpherePatchKey> leaves,
            int maximumLevel)
    {
        int normalizedMaximumLevel = Math.Clamp(maximumLevel, 0, 12);
        FastNoiseLite noise = CreateNoise(0.01f, 0);
        Dictionary<EdgeSegmentKey, List<EdgeOwner>> ownersBySegment = new();

        foreach (CubeSpherePatchKey leaf in leaves)
        {
            foreach (CubeSpherePatchEdge edge in Enum.GetValues<CubeSpherePatchEdge>())
            {
                AddAtomicEdgeOwners(
                    ownersBySegment,
                    leaf,
                    edge,
                    normalizedMaximumLevel,
                    1.0f,
                    0.0f,
                    noise);
            }
        }

        HashSet<CubeSpherePatchKey> leavesToSplit = new();
        foreach (List<EdgeOwner> owners in ownersBySegment.Values)
        {
            if (owners.Count < 2)
            {
                continue;
            }

            int maximumOwnerLevel = 0;
            foreach (EdgeOwner owner in owners)
            {
                maximumOwnerLevel = Math.Max(maximumOwnerLevel, owner.Level);
            }

            foreach (EdgeOwner owner in owners)
            {
                if (maximumOwnerLevel - owner.Level > 1 &&
                    owner.Level < normalizedMaximumLevel)
                {
                    leavesToSplit.Add(owner.Key);
                }
            }
        }

        return leavesToSplit;
    }

    private static void AddAtomicEdgeOwners(
        Dictionary<EdgeSegmentKey, List<EdgeOwner>> ownersBySegment,
        CubeSpherePatchKey patch,
        CubeSpherePatchEdge edge,
        int maximumLevel,
        float radius,
        float heightAmplitude,
        FastNoiseLite noise)
    {
        int subdivisions = 1 << Math.Max(0, maximumLevel - patch.Level);
        for (int segment = 0; segment < subdivisions; segment++)
        {
            float t0 = segment / (float)subdivisions;
            float t1 = (segment + 1) / (float)subdivisions;
            Vector3 startDirection = GetEdgeDirection(patch, edge, t0);
            Vector3 endDirection = GetEdgeDirection(patch, edge, t1);
            DirectionKey startKey = QuantizeDirection(startDirection);
            DirectionKey endKey = QuantizeDirection(endDirection);
            Vector3 startPosition = SampleSurfacePosition(
                startDirection,
                radius,
                heightAmplitude,
                noise);
            Vector3 endPosition = SampleSurfacePosition(
                endDirection,
                radius,
                heightAmplitude,
                noise);

            EdgeSegmentKey key;
            EdgeOwner owner;
            if (startKey.CompareTo(endKey) <= 0)
            {
                key = new EdgeSegmentKey(startKey, endKey);
                owner = new EdgeOwner(patch, patch.Level, startPosition, endPosition);
            }
            else
            {
                key = new EdgeSegmentKey(endKey, startKey);
                owner = new EdgeOwner(patch, patch.Level, endPosition, startPosition);
            }

            if (!ownersBySegment.TryGetValue(key, out List<EdgeOwner>? owners) ||
                owners is null)
            {
                owners = new List<EdgeOwner>(2);
                ownersBySegment[key] = owners;
            }

            owners.Add(owner);
        }
    }

    private static Vector3 GetEdgeDirection(
        CubeSpherePatchKey patch,
        CubeSpherePatchEdge edge,
        float t)
    {
        FaceBasis basis = GetFaceBasis(patch.FaceId);
        GetPatchBounds(patch, out float uMin, out float uMax, out float vMin, out float vMax);
        float u;
        float v;

        switch (edge)
        {
            case CubeSpherePatchEdge.Left:
                u = uMin;
                v = Mathf.Lerp(vMin, vMax, t);
                break;
            case CubeSpherePatchEdge.Right:
                u = uMax;
                v = Mathf.Lerp(vMin, vMax, t);
                break;
            case CubeSpherePatchEdge.Bottom:
                u = Mathf.Lerp(uMin, uMax, t);
                v = vMin;
                break;
            case CubeSpherePatchEdge.Top:
                u = Mathf.Lerp(uMin, uMax, t);
                v = vMax;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edge), edge, null);
        }

        return FaceUvToDirection(basis, u, v);
    }

    private static float GetAngularDistance(
        Vector3 firstDirection,
        Vector3 secondDirection)
    {
        float alignment = Math.Clamp(
            firstDirection.Dot(secondDirection),
            -1.0f,
            1.0f);
        return MathF.Acos(alignment);
    }

    private static void AddSkirt(
        CubeSpherePatchData patch,
        IReadOnlyList<int> edgeIndices,
        float skirtDepth)
    {
        int[] loweredIndices = new int[edgeIndices.Count];
        for (int i = 0; i < edgeIndices.Count; i++)
        {
            int topIndex = edgeIndices[i];
            Vector3 radialNormal = patch.Normals[topIndex];
            Vector3 topPosition = patch.Vertices[topIndex];
            float loweredRadius = Math.Max(0.05f, topPosition.Length() - skirtDepth);
            loweredIndices[i] = patch.Vertices.Count;
            patch.Vertices.Add(radialNormal * loweredRadius);
            patch.Normals.Add(radialNormal);
            patch.Uvs.Add(patch.Uvs[topIndex]);
        }

        for (int i = 0; i < edgeIndices.Count - 1; i++)
        {
            int topA = edgeIndices[i];
            int topB = edgeIndices[i + 1];
            int bottomA = loweredIndices[i];
            int bottomB = loweredIndices[i + 1];

            patch.Indices.Add(topA);
            patch.Indices.Add(topB);
            patch.Indices.Add(bottomA);

            patch.Indices.Add(topB);
            patch.Indices.Add(bottomB);
            patch.Indices.Add(bottomA);
            patch.SkirtTriangleCount += 2;
        }
    }

    private static IReadOnlyList<int> BuildEdgeIndices(
        CubeSpherePatchEdge edge,
        int resolution)
    {
        int[] indices = new int[resolution];
        for (int i = 0; i < resolution; i++)
        {
            indices[i] = edge switch
            {
                CubeSpherePatchEdge.Left => i * resolution,
                CubeSpherePatchEdge.Right => (i * resolution) + resolution - 1,
                CubeSpherePatchEdge.Bottom => i,
                CubeSpherePatchEdge.Top => ((resolution - 1) * resolution) + i,
                _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null)
            };
        }

        return indices;
    }

    private static void GetPatchBounds(
        CubeSpherePatchKey key,
        out float uMin,
        out float uMax,
        out float vMin,
        out float vMax)
    {
        int level = Math.Clamp(key.Level, 0, 12);
        int divisions = 1 << level;
        int x = Math.Clamp(key.X, 0, divisions - 1);
        int y = Math.Clamp(key.Y, 0, divisions - 1);
        float patchSize = 2.0f / divisions;
        uMin = -1.0f + (x * patchSize);
        uMax = uMin + patchSize;
        vMin = -1.0f + (y * patchSize);
        vMax = vMin + patchSize;
    }

    private static Vector3 FaceUvToDirection(
        FaceBasis basis,
        float u,
        float v)
    {
        return (basis.Normal + (basis.AxisU * u) + (basis.AxisV * v)).Normalized();
    }

    private static Vector3 SampleSurfacePosition(
        Vector3 radialNormal,
        float radius,
        float heightAmplitude,
        FastNoiseLite noise)
    {
        float normalizedRadius = Math.Max(1.0f, radius);
        float normalizedHeightAmplitude = Math.Max(0.0f, heightAmplitude);
        float sampledHeight = normalizedHeightAmplitude <= 0.0f
            ? 0.0f
            : noise.GetNoise3D(
                radialNormal.X * normalizedRadius,
                radialNormal.Y * normalizedRadius,
                radialNormal.Z * normalizedRadius) * normalizedHeightAmplitude;
        return radialNormal * (normalizedRadius + sampledHeight);
    }

    private static FastNoiseLite CreateNoise(float frequency, int seed)
    {
        return new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
            Frequency = frequency,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
            FractalLacunarity = 2.0f,
            FractalGain = 0.5f
        };
    }

    private static DirectionKey QuantizeDirection(Vector3 direction)
    {
        return new DirectionKey(
            (int)MathF.Round(direction.X * DirectionQuantization),
            (int)MathF.Round(direction.Y * DirectionQuantization),
            (int)MathF.Round(direction.Z * DirectionQuantization));
    }

    private static FaceBasis GetFaceBasis(CubeSphereFaceId faceId)
    {
        return FaceBases[(int)faceId];
    }

    private static int NormalizeResolution(int requestedResolution)
    {
        int resolution = Math.Clamp(requestedResolution, 3, 257);
        return resolution % 2 == 0 ? resolution + 1 : resolution;
    }
}
