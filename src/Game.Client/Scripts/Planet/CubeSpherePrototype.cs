using System;
using System.Collections.Generic;
using Godot;

public enum CubeSphereDebugMode
{
    FaceIds = 0,
    LodLevels = 1,
    RadialNormals = 2
}

public enum CubeSphereCameraMode
{
    PlanetaryPlayer = 0,
    OverviewOrbit = 1
}

public enum CubeSphereLodTestState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class CubeSpherePrototype : Node3D
{
    private sealed class PatchRuntime
    {
        public PatchRuntime(
            CubeSpherePatchData data,
            MeshInstance3D meshInstance)
        {
            Data = data;
            MeshInstance = meshInstance;
        }

        public CubeSpherePatchData Data { get; }

        public MeshInstance3D MeshInstance { get; }
    }

    private static readonly Vector3[] LodAcceptanceRoute =
    {
        Vector3.Right,
        new Vector3(1.0f, 0.0f, 1.0f).Normalized(),
        Vector3.Back,
        new Vector3(-1.0f, 0.0f, 1.0f).Normalized(),
        Vector3.Left,
        new Vector3(-1.0f, 0.0f, -1.0f).Normalized(),
        Vector3.Forward,
        new Vector3(1.0f, 0.0f, -1.0f).Normalized(),
        Vector3.Right
    };

    [Export(PropertyHint.Range, "3,257,2")]
    public int FaceResolution { get; set; } = 33;

    [Export(PropertyHint.Range, "8.0,100000.0,1.0")]
    public float PlanetRadius { get; set; } = 96.0f;

    [Export(PropertyHint.Range, "0.0,1000.0,0.1")]
    public float HeightAmplitude { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0.0001,1.0,0.0001")]
    public float NoiseFrequency { get; set; } = 0.0125f;

    [Export]
    public int NoiseSeed { get; set; } = 20260801;

    [Export]
    public bool GenerateCollision { get; set; } = true;

    [Export(PropertyHint.Range, "0.0,45.0,0.1")]
    public float OrbitDegreesPerSecond { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "0,6,1")]
    public int LodBaseLevel { get; set; } = 1;

    [Export(PropertyHint.Range, "10.0,85.0,0.5")]
    public float LodSplitAngleDegrees { get; set; } = 48.0f;

    [Export(PropertyHint.Range, "15.0,89.0,0.5")]
    public float LodMergeAngleDegrees { get; set; } = 62.0f;

    [Export(PropertyHint.Range, "0.05,2.0,0.05")]
    public float LodUpdateIntervalSeconds { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float LodSkirtDepth { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.25,3.0,0.05")]
    public float LodTestStepSeconds { get; set; } = 0.65f;

    private readonly Dictionary<CubeSpherePatchKey, PatchRuntime> _patches = new();
    private readonly HashSet<CubeSpherePatchKey> _splitParents = new();
    private readonly List<CollisionShape3D> _collisionShapes = new();
    private Node3D? _planetRoot;
    private Node3D? _facesRoot;
    private StaticBody3D? _collisionBody;
    private Node3D? _cameraRig;
    private Camera3D? _overviewCamera;
    private PlanetaryPlayerController? _planetaryPlayer;
    private FloatingOriginController? _floatingOrigin;
    private Label? _hudLabel;
    private CubeSphereBuildData? _buildData;
    private CubeSphereLodValidation? _lodValidation;
    private CubeSphereDebugMode _debugMode = CubeSphereDebugMode.LodLevels;
    private CubeSphereCameraMode _cameraMode =
        CubeSphereCameraMode.PlanetaryPlayer;
    private CubeSphereLodTestState _lodTestState =
        CubeSphereLodTestState.Ready;
    private bool _orbitPaused;
    private double _hudRefreshAccumulator;
    private double _lodUpdateAccumulator;
    private int _lodLevelBasePatchCount;
    private int _lodLevelFinePatchCount;
    private int _lodSkirtTriangles;
    private int _collisionResolution;
    private double _lastLodUpdateMilliseconds;
    private int _lodTopologyRevision;
    private Vector3 _lodTestFocusDirection = Vector3.Right;
    private int _lodTestRouteIndex;
    private float _lodTestStepElapsed;
    private int _lodTestSplitEvents;
    private int _lodTestMergeEvents;
    private int _lodTestTopologyChanges;
    private int _lodTestMinimumPatches;
    private int _lodTestMaximumPatches;
    private int _lodTestMaximumOpenSegments;
    private int _lodTestMaximumNonManifoldSegments;
    private int _lodTestMaximumNeighborDelta;
    private float _lodTestMaximumSeamError;
    private string _lodTestResult = "готов";

    public bool LodTestRunning =>
        _lodTestState == CubeSphereLodTestState.Running;

    public override void _Ready()
    {
        _planetRoot = GetNode<Node3D>("Planet");
        _facesRoot = GetNode<Node3D>("Planet/Faces");
        _collisionBody = GetNode<StaticBody3D>("Planet/CollisionBody");
        _cameraRig = GetNode<Node3D>("CameraRig");
        _overviewCamera = GetNode<Camera3D>("CameraRig/Camera3D");
        _planetaryPlayer = GetNode<PlanetaryPlayerController>(
            "PlanetaryPlayer");
        _floatingOrigin = GetNode<FloatingOriginController>(
            "FloatingOriginController");
        _hudLabel = GetNode<Label>(
            "Hud/MarginContainer/PanelContainer/Label");

        BuildPlanet();
        ApplyCameraMode();
        UpdateHud();
    }

    public override void _Process(double delta)
    {
        if (_cameraMode == CubeSphereCameraMode.OverviewOrbit &&
            !_orbitPaused &&
            _cameraRig is not null)
        {
            _cameraRig.RotateY(
                Mathf.DegToRad(OrbitDegreesPerSecond) * (float)delta);
        }

        if (LodTestRunning)
        {
            UpdateLodAcceptanceTest((float)delta);
        }

        _lodUpdateAccumulator += delta;
        if (_lodUpdateAccumulator >= Math.Max(0.05f, LodUpdateIntervalSeconds))
        {
            _lodUpdateAccumulator = 0.0;
            UpdateQuadtreeLod(GetCurrentLodFocusDirection(), false);
        }

        _hudRefreshAccumulator += delta;
        if (_hudRefreshAccumulator >= 0.1)
        {
            _hudRefreshAccumulator = 0.0;
            UpdateHud();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode == Key.F1)
        {
            _debugMode = (CubeSphereDebugMode)(((int)_debugMode + 1) % 3);
            RebuildVisualMeshes();
            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.F2)
        {
            CancelAllAcceptanceTests();
            _cameraMode = _cameraMode == CubeSphereCameraMode.PlanetaryPlayer
                ? CubeSphereCameraMode.OverviewOrbit
                : CubeSphereCameraMode.PlanetaryPlayer;
            ApplyCameraMode();
            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.T ||
            keyEvent.PhysicalKeycode == Key.T)
        {
            if (LodTestRunning)
            {
                CancelLodAcceptanceTest();
            }

            if (_floatingOrigin?.TestRunning == true)
            {
                _floatingOrigin.CancelAcceptanceTest(true);
                UpdateHud();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_planetaryPlayer is not null &&
                _cameraMode == CubeSphereCameraMode.PlanetaryPlayer)
            {
                if (_planetaryPlayer.SeamTestRunning)
                {
                    _planetaryPlayer.CancelSeamTraversalTest(true);
                }
                else
                {
                    _planetaryPlayer.BeginSeamTraversalTest();
                }

                UpdateHud();
            }
            else
            {
                GD.Print(
                    "TASK-030 seam traversal requires planetary player camera mode.");
            }

            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.Y ||
            keyEvent.PhysicalKeycode == Key.Y)
        {
            if (LodTestRunning)
            {
                CancelLodAcceptanceTest();
            }

            if (_floatingOrigin is not null &&
                _cameraMode == CubeSphereCameraMode.PlanetaryPlayer)
            {
                if (_floatingOrigin.TestRunning)
                {
                    _floatingOrigin.CancelAcceptanceTest(true);
                }
                else
                {
                    _planetaryPlayer?.CancelSeamTraversalTest(true);
                    _floatingOrigin.BeginAcceptanceTest();
                }

                UpdateHud();
            }
            else
            {
                GD.Print(
                    "TASK-032 floating-origin acceptance requires planetary player camera mode.");
            }

            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.U ||
            keyEvent.PhysicalKeycode == Key.U)
        {
            if (LodTestRunning)
            {
                CancelLodAcceptanceTest();
            }
            else
            {
                _floatingOrigin?.CancelAcceptanceTest(true);
                _planetaryPlayer?.CancelSeamTraversalTest(true);
                BeginLodAcceptanceTest();
            }

            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.R ||
            keyEvent.PhysicalKeycode == Key.R)
        {
            if (LodTestRunning)
            {
                CancelLodAcceptanceTest();
                UpdateHud();
            }

            if (_floatingOrigin?.TestRunning == true)
            {
                _floatingOrigin.CancelAcceptanceTest(true);
                UpdateHud();
            }
        }
        else if (keyEvent.Keycode == Key.Space &&
            _cameraMode == CubeSphereCameraMode.OverviewOrbit)
        {
            _orbitPaused = !_orbitPaused;
            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildPlanet()
    {
        if (_facesRoot is null || _collisionBody is null)
        {
            throw new InvalidOperationException(
                "CubeSpherePrototype scene is missing Planet/Faces or CollisionBody.");
        }

        ClearGeneratedChildren();
        ulong startedAtMicroseconds = Time.GetTicksUsec();
        CubeSphereBuildData foundationData = CubeSphereMeshBuilder.Build(
            FaceResolution,
            PlanetRadius,
            HeightAmplitude,
            NoiseFrequency,
            NoiseSeed);
        _buildData = foundationData;

        if (GenerateCollision)
        {
            _collisionResolution = Math.Min(
                257,
                ((foundationData.Resolution - 1) * (1 << GetFineLevel())) + 1);
            CubeSphereBuildData collisionBuildData = CubeSphereMeshBuilder.Build(
                _collisionResolution,
                PlanetRadius,
                HeightAmplitude,
                NoiseFrequency,
                NoiseSeed);
            _collisionResolution = collisionBuildData.Resolution;

            foreach (CubeSphereFaceData faceData in collisionBuildData.Faces)
            {
                ArrayMesh collisionMesh = CreateFullFaceMesh(faceData);
                ConcavePolygonShape3D shape = collisionMesh.CreateTrimeshShape();
                shape.BackfaceCollision = true;
                CollisionShape3D collisionShape = new()
                {
                    Name = $"Collision_{SanitizeName(faceData.DisplayName)}",
                    Shape = shape
                };
                _collisionBody.AddChild(collisionShape);
                _collisionShapes.Add(collisionShape);
            }
        }

        UpdateQuadtreeLod(GetPlayerFocusDirection(), true);

        double elapsedMilliseconds =
            (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;
        GD.Print(
            "CubeSphere quadtree foundation: " +
            $"patches={_patches.Count}; " +
            $"baseLevel={GetBaseLevel()}; fineLevel={GetFineLevel()}; " +
            $"resolution={foundationData.Resolution}x{foundationData.Resolution}; " +
            $"collision={_collisionShapes.Count}@{_collisionResolution}x{_collisionResolution}; " +
            $"faceSeams={foundationData.SeamComparisons}/" +
            $"{foundationData.ExpectedSeamComparisons}; " +
            $"lodOpen={_lodValidation?.OpenSegments ?? -1}; " +
            $"lodDelta={_lodValidation?.MaximumNeighborLevelDelta ?? -1}; " +
            $"build={elapsedMilliseconds:F2} ms");
    }

    private void UpdateQuadtreeLod(Vector3 focusDirection, bool forceValidation)
    {
        if (_facesRoot is null || focusDirection.LengthSquared() <= 0.000001f)
        {
            return;
        }

        Vector3 normalizedFocus = focusDirection.Normalized();
        HashSet<CubeSpherePatchKey> desiredSplits =
            BuildDesiredSplitParents(normalizedFocus);
        bool topologyChanged = !_splitParents.SetEquals(desiredSplits);

        int splitEvents = 0;
        int mergeEvents = 0;
        foreach (CubeSpherePatchKey parent in desiredSplits)
        {
            if (!_splitParents.Contains(parent))
            {
                splitEvents++;
            }
        }

        foreach (CubeSpherePatchKey parent in _splitParents)
        {
            if (!desiredSplits.Contains(parent))
            {
                mergeEvents++;
            }
        }

        if (topologyChanged)
        {
            _splitParents.Clear();
            _splitParents.UnionWith(desiredSplits);
            HashSet<CubeSpherePatchKey> desiredLeaves =
                BuildDesiredLeaves(_splitParents);
            ApplyPatchSet(desiredLeaves);
            _lodTopologyRevision++;

            if (LodTestRunning)
            {
                _lodTestSplitEvents += splitEvents;
                _lodTestMergeEvents += mergeEvents;
                _lodTestTopologyChanges++;
            }
        }

        if (topologyChanged || forceValidation || _lodValidation is null)
        {
            ulong startedAtMicroseconds = Time.GetTicksUsec();
            _lodValidation = CubeSpherePatchBuilder.ValidateTopology(
                _patches.Keys,
                GetFineLevel(),
                PlanetRadius,
                HeightAmplitude,
                NoiseFrequency,
                NoiseSeed);
            _lastLodUpdateMilliseconds =
                (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;
            UpdateLodCounters();
            RecordLodTestMetrics();

            GD.Print(
                "CubeSphere LOD revision: " +
                $"revision={_lodTopologyRevision}; patches={_patches.Count}; " +
                $"L{GetBaseLevel()}={_lodLevelBasePatchCount}; " +
                $"L{GetFineLevel()}={_lodLevelFinePatchCount}; " +
                $"split+={splitEvents}; merge+={mergeEvents}; " +
                $"atomic={_lodValidation.AtomicSegments}; " +
                $"open={_lodValidation.OpenSegments}; " +
                $"nonManifold={_lodValidation.NonManifoldSegments}; " +
                $"maxDelta={_lodValidation.MaximumNeighborLevelDelta}; " +
                $"seam={_lodValidation.MaximumSeamPositionError:E3}; " +
                $"validate={_lastLodUpdateMilliseconds:F2} ms");
        }
    }

    private HashSet<CubeSpherePatchKey> BuildDesiredSplitParents(
        Vector3 focusDirection)
    {
        HashSet<CubeSpherePatchKey> result = new();
        int baseLevel = GetBaseLevel();
        int divisions = 1 << baseLevel;
        float splitDot = Mathf.Cos(Mathf.DegToRad(
            Math.Clamp(LodSplitAngleDegrees, 1.0f, 88.0f)));
        float mergeAngle = Math.Max(
            LodSplitAngleDegrees + 1.0f,
            Math.Clamp(LodMergeAngleDegrees, 2.0f, 89.0f));
        float mergeDot = Mathf.Cos(Mathf.DegToRad(mergeAngle));

        foreach (CubeSphereFaceId faceId in CubeSpherePatchBuilder.FaceIds)
        {
            for (int y = 0; y < divisions; y++)
            {
                for (int x = 0; x < divisions; x++)
                {
                    CubeSpherePatchKey parent = new(faceId, baseLevel, x, y);
                    float alignment = CubeSpherePatchBuilder
                        .GetPatchCenterDirection(parent)
                        .Dot(focusDirection);
                    bool wasSplit = _splitParents.Contains(parent);
                    bool shouldSplit = wasSplit
                        ? alignment >= mergeDot
                        : alignment >= splitDot;
                    if (shouldSplit)
                    {
                        result.Add(parent);
                    }
                }
            }
        }

        return result;
    }

    private HashSet<CubeSpherePatchKey> BuildDesiredLeaves(
        IReadOnlyCollection<CubeSpherePatchKey> splitParents)
    {
        HashSet<CubeSpherePatchKey> leaves = new();
        HashSet<CubeSpherePatchKey> splitLookup = new(splitParents);
        int baseLevel = GetBaseLevel();
        int fineLevel = GetFineLevel();
        int divisions = 1 << baseLevel;

        foreach (CubeSphereFaceId faceId in CubeSpherePatchBuilder.FaceIds)
        {
            for (int y = 0; y < divisions; y++)
            {
                for (int x = 0; x < divisions; x++)
                {
                    CubeSpherePatchKey parent = new(faceId, baseLevel, x, y);
                    if (!splitLookup.Contains(parent))
                    {
                        leaves.Add(parent);
                        continue;
                    }

                    int childX = x * 2;
                    int childY = y * 2;
                    leaves.Add(new CubeSpherePatchKey(faceId, fineLevel, childX, childY));
                    leaves.Add(new CubeSpherePatchKey(faceId, fineLevel, childX + 1, childY));
                    leaves.Add(new CubeSpherePatchKey(faceId, fineLevel, childX, childY + 1));
                    leaves.Add(new CubeSpherePatchKey(faceId, fineLevel, childX + 1, childY + 1));
                }
            }
        }

        return leaves;
    }

    private void ApplyPatchSet(HashSet<CubeSpherePatchKey> desiredLeaves)
    {
        List<CubeSpherePatchKey> keysToRemove = new();
        foreach (CubeSpherePatchKey existingKey in _patches.Keys)
        {
            if (!desiredLeaves.Contains(existingKey))
            {
                keysToRemove.Add(existingKey);
            }
        }

        foreach (CubeSpherePatchKey key in keysToRemove)
        {
            PatchRuntime runtime = _patches[key];
            runtime.MeshInstance.QueueFree();
            _patches.Remove(key);
        }

        foreach (CubeSpherePatchKey desiredKey in desiredLeaves)
        {
            if (_patches.ContainsKey(desiredKey))
            {
                continue;
            }

            AddPatch(desiredKey);
        }
    }

    private void AddPatch(CubeSpherePatchKey key)
    {
        if (_facesRoot is null)
        {
            return;
        }

        CubeSpherePatchData patchData = CubeSpherePatchBuilder.BuildPatch(
            key,
            FaceResolution,
            PlanetRadius,
            HeightAmplitude,
            NoiseFrequency,
            NoiseSeed,
            LodSkirtDepth);
        MeshInstance3D meshInstance = new()
        {
            Name = $"Patch_{SanitizeName(patchData.FaceDisplayName)}_" +
                $"L{key.Level}_{key.X}_{key.Y}",
            Mesh = CreatePatchMesh(patchData)
        };
        _facesRoot.AddChild(meshInstance);
        _patches.Add(key, new PatchRuntime(patchData, meshInstance));
    }

    private void ApplyCameraMode()
    {
        if (_overviewCamera is null || _planetaryPlayer is null)
        {
            return;
        }

        bool playerMode =
            _cameraMode == CubeSphereCameraMode.PlanetaryPlayer;
        _overviewCamera.Current = !playerMode;
        _planetaryPlayer.SetControlEnabled(playerMode);

        GD.Print(
            "CubeSphere camera mode: " +
            (playerMode ? "planetary player" : "overview orbit"));
    }

    private void RebuildVisualMeshes()
    {
        foreach (PatchRuntime runtime in _patches.Values)
        {
            runtime.MeshInstance.Mesh = CreatePatchMesh(runtime.Data);
        }
    }

    private ArrayMesh CreatePatchMesh(CubeSpherePatchData patchData)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        Color patchColor = GetPatchColor(patchData);

        for (int i = 0; i < patchData.Vertices.Count; i++)
        {
            Vector3 normal = patchData.Normals[i];
            surfaceTool.SetNormal(normal);
            surfaceTool.SetUV(patchData.Uvs[i]);
            surfaceTool.SetColor(_debugMode == CubeSphereDebugMode.RadialNormals
                ? new Color(
                    (normal.X * 0.5f) + 0.5f,
                    (normal.Y * 0.5f) + 0.5f,
                    (normal.Z * 0.5f) + 0.5f,
                    1.0f)
                : patchColor);
            surfaceTool.AddVertex(patchData.Vertices[i]);
        }

        foreach (int index in patchData.Indices)
        {
            surfaceTool.AddIndex(index);
        }

        ArrayMesh mesh = surfaceTool.Commit();
        mesh.SurfaceSetMaterial(0, CreatePlanetMaterial());
        return mesh;
    }

    private ArrayMesh CreateFullFaceMesh(CubeSphereFaceData faceData)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < faceData.Vertices.Count; i++)
        {
            surfaceTool.SetNormal(faceData.Normals[i]);
            surfaceTool.SetUV(faceData.Uvs[i]);
            surfaceTool.AddVertex(faceData.Vertices[i]);
        }

        foreach (int index in faceData.Indices)
        {
            surfaceTool.AddIndex(index);
        }

        return surfaceTool.Commit();
    }

    private Color GetPatchColor(CubeSpherePatchData patchData)
    {
        if (_debugMode == CubeSphereDebugMode.FaceIds)
        {
            return patchData.DebugColor;
        }

        if (_debugMode == CubeSphereDebugMode.LodLevels)
        {
            return patchData.Key.Level == GetFineLevel()
                ? new Color(1.0f, 0.58f, 0.18f, 1.0f)
                : new Color(0.18f, 0.62f, 0.96f, 1.0f);
        }

        return Colors.White;
    }

    private StandardMaterial3D CreatePlanetMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Roughness = 0.88f,
            MetallicSpecular = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = false,
            ShadingMode = _debugMode == CubeSphereDebugMode.RadialNormals
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel
        };
    }

    private void BeginLodAcceptanceTest()
    {
        _lodTestState = CubeSphereLodTestState.Running;
        _lodTestRouteIndex = 0;
        _lodTestStepElapsed = 0.0f;
        _lodTestFocusDirection = LodAcceptanceRoute[0];
        _lodTestSplitEvents = 0;
        _lodTestMergeEvents = 0;
        _lodTestTopologyChanges = 0;
        _lodTestMinimumPatches = int.MaxValue;
        _lodTestMaximumPatches = 0;
        _lodTestMaximumOpenSegments = 0;
        _lodTestMaximumNonManifoldSegments = 0;
        _lodTestMaximumNeighborDelta = 0;
        _lodTestMaximumSeamError = 0.0f;
        _lodTestResult = "выполняется";

        UpdateQuadtreeLod(_lodTestFocusDirection, true);
        RecordLodTestMetrics();
        GD.Print(
            "TASK-033 quadtree LOD acceptance started: " +
            $"route={LodAcceptanceRoute.Length}; " +
            $"base=L{GetBaseLevel()}; fine=L{GetFineLevel()}; " +
            $"resolution={FaceResolution}x{FaceResolution}");
    }

    private void UpdateLodAcceptanceTest(float deltaSeconds)
    {
        _lodTestStepElapsed += deltaSeconds;
        if (_lodTestStepElapsed < Math.Max(0.25f, LodTestStepSeconds))
        {
            return;
        }

        _lodTestStepElapsed = 0.0f;
        _lodTestRouteIndex++;
        if (_lodTestRouteIndex < LodAcceptanceRoute.Length)
        {
            _lodTestFocusDirection = LodAcceptanceRoute[_lodTestRouteIndex];
            UpdateQuadtreeLod(_lodTestFocusDirection, true);
            return;
        }

        bool passed =
            _lodTestSplitEvents > 0 &&
            _lodTestMergeEvents > 0 &&
            _lodTestTopologyChanges >= 4 &&
            _lodTestMaximumOpenSegments == 0 &&
            _lodTestMaximumNonManifoldSegments == 0 &&
            _lodTestMaximumNeighborDelta <= 1 &&
            _lodTestMaximumSeamError <= 0.001f &&
            _lodTestMinimumPatches >= GetMinimumPatchCount() &&
            _lodTestMaximumPatches <= GetMaximumPatchCount();

        FinishLodAcceptanceTest(
            passed ? CubeSphereLodTestState.Passed : CubeSphereLodTestState.Failed,
            passed ? "критерии выполнены" : BuildLodTestFailureReason());
    }

    private void CancelLodAcceptanceTest()
    {
        if (!LodTestRunning)
        {
            return;
        }

        FinishLodAcceptanceTest(
            CubeSphereLodTestState.Cancelled,
            "остановлен пользователем");
    }

    private void FinishLodAcceptanceTest(
        CubeSphereLodTestState finalState,
        string result)
    {
        _lodTestState = finalState;
        _lodTestResult = result;
        string resultLabel = finalState switch
        {
            CubeSphereLodTestState.Passed => "PASS",
            CubeSphereLodTestState.Failed => "FAIL",
            _ => "CANCELLED"
        };

        GD.Print(
            $"TASK-033 quadtree LOD acceptance {resultLabel}: " +
            $"split={_lodTestSplitEvents}; merge={_lodTestMergeEvents}; " +
            $"changes={_lodTestTopologyChanges}; " +
            $"patches={_lodTestMinimumPatches}-{_lodTestMaximumPatches}; " +
            $"open={_lodTestMaximumOpenSegments}; " +
            $"nonManifold={_lodTestMaximumNonManifoldSegments}; " +
            $"maxDelta={_lodTestMaximumNeighborDelta}; " +
            $"seam={_lodTestMaximumSeamError:E3}; result={result}");

        UpdateQuadtreeLod(GetPlayerFocusDirection(), true);
        UpdateHud();
    }

    private string BuildLodTestFailureReason()
    {
        if (_lodTestSplitEvents <= 0 || _lodTestMergeEvents <= 0)
        {
            return "нет подтверждённых split/merge событий";
        }

        if (_lodTestTopologyChanges < 4)
        {
            return $"недостаточно topology changes: {_lodTestTopologyChanges}";
        }

        if (_lodTestMaximumOpenSegments > 0)
        {
            return $"обнаружены открытые сегменты: {_lodTestMaximumOpenSegments}";
        }

        if (_lodTestMaximumNonManifoldSegments > 0)
        {
            return $"non-manifold сегменты: {_lodTestMaximumNonManifoldSegments}";
        }

        if (_lodTestMaximumNeighborDelta > 1)
        {
            return $"разница соседних LOD: {_lodTestMaximumNeighborDelta}";
        }

        if (_lodTestMaximumSeamError > 0.001f)
        {
            return $"ошибка общей границы: {_lodTestMaximumSeamError:E3}";
        }

        return "число активных патчей вне допустимого диапазона";
    }

    private void RecordLodTestMetrics()
    {
        if (!LodTestRunning || _lodValidation is null)
        {
            return;
        }

        _lodTestMinimumPatches = Math.Min(
            _lodTestMinimumPatches,
            _patches.Count);
        _lodTestMaximumPatches = Math.Max(
            _lodTestMaximumPatches,
            _patches.Count);
        _lodTestMaximumOpenSegments = Math.Max(
            _lodTestMaximumOpenSegments,
            _lodValidation.OpenSegments);
        _lodTestMaximumNonManifoldSegments = Math.Max(
            _lodTestMaximumNonManifoldSegments,
            _lodValidation.NonManifoldSegments);
        _lodTestMaximumNeighborDelta = Math.Max(
            _lodTestMaximumNeighborDelta,
            _lodValidation.MaximumNeighborLevelDelta);
        _lodTestMaximumSeamError = Math.Max(
            _lodTestMaximumSeamError,
            _lodValidation.MaximumSeamPositionError);
    }

    private void CancelAllAcceptanceTests()
    {
        if (LodTestRunning)
        {
            CancelLodAcceptanceTest();
        }

        if (_floatingOrigin?.TestRunning == true)
        {
            _floatingOrigin.CancelAcceptanceTest(true);
        }

        if (_planetaryPlayer?.SeamTestRunning == true)
        {
            _planetaryPlayer.CancelSeamTraversalTest(true);
        }
    }

    private Vector3 GetCurrentLodFocusDirection()
    {
        return LodTestRunning
            ? _lodTestFocusDirection
            : GetPlayerFocusDirection();
    }

    private Vector3 GetPlayerFocusDirection()
    {
        if (_planetRoot is null || _planetaryPlayer is null)
        {
            return Vector3.Right;
        }

        Vector3 offset =
            _planetaryPlayer.GlobalPosition - _planetRoot.GlobalPosition;
        return offset.LengthSquared() <= 0.000001f
            ? Vector3.Right
            : offset.Normalized();
    }

    private int GetBaseLevel()
    {
        return Math.Clamp(LodBaseLevel, 0, 6);
    }

    private int GetFineLevel()
    {
        return Math.Min(7, GetBaseLevel() + 1);
    }

    private int GetMinimumPatchCount()
    {
        int baseDivisions = 1 << GetBaseLevel();
        return 6 * baseDivisions * baseDivisions;
    }

    private int GetMaximumPatchCount()
    {
        return GetMinimumPatchCount() * 4;
    }

    private void UpdateLodCounters()
    {
        _lodLevelBasePatchCount = 0;
        _lodLevelFinePatchCount = 0;
        _lodSkirtTriangles = 0;

        foreach (PatchRuntime runtime in _patches.Values)
        {
            if (runtime.Data.Key.Level == GetBaseLevel())
            {
                _lodLevelBasePatchCount++;
            }
            else if (runtime.Data.Key.Level == GetFineLevel())
            {
                _lodLevelFinePatchCount++;
            }

            _lodSkirtTriangles += runtime.Data.SkirtTriangleCount;
        }
    }

    private void ClearGeneratedChildren()
    {
        foreach (PatchRuntime runtime in _patches.Values)
        {
            runtime.MeshInstance.QueueFree();
        }

        foreach (CollisionShape3D collisionShape in _collisionShapes)
        {
            collisionShape.QueueFree();
        }

        _patches.Clear();
        _splitParents.Clear();
        _collisionShapes.Clear();
    }

    private string LodTestStatusText
    {
        get
        {
            return _lodTestState switch
            {
                CubeSphereLodTestState.Running =>
                    $"TASK-033 LOD (U): RUNNING {_lodTestRouteIndex + 1}/" +
                    $"{LodAcceptanceRoute.Length}, split={_lodTestSplitEvents}, " +
                    $"merge={_lodTestMergeEvents}",
                CubeSphereLodTestState.Passed =>
                    $"TASK-033 LOD (U): PASS split={_lodTestSplitEvents}, " +
                    $"merge={_lodTestMergeEvents}, Δlod={_lodTestMaximumNeighborDelta}, " +
                    $"open={_lodTestMaximumOpenSegments}, " +
                    $"seam={_lodTestMaximumSeamError:E2}",
                CubeSphereLodTestState.Failed =>
                    $"TASK-033 LOD (U): FAIL — {_lodTestResult}",
                CubeSphereLodTestState.Cancelled =>
                    "TASK-033 LOD (U): CANCELLED",
                _ => "TASK-033 LOD (U): READY"
            };
        }
    }

    private static string SanitizeName(string name)
    {
        return name.Replace('+', 'P').Replace('-', 'N');
    }

    private void UpdateHud()
    {
        if (_hudLabel is null)
        {
            return;
        }

        string orbitState = _orbitPaused ? "пауза" : "вращение";
        string debugMode = _debugMode switch
        {
            CubeSphereDebugMode.FaceIds => "цвета граней",
            CubeSphereDebugMode.LodLevels => "уровни LOD",
            _ => "радиальные нормали"
        };

        if (_buildData is null)
        {
            _hudLabel.Text =
                "ПРОТОТИП C — QUADTREE LOD\n" +
                "Построение геометрии...";
            return;
        }

        bool faceSeamPass =
            _buildData.SeamComparisons == _buildData.ExpectedSeamComparisons &&
            _buildData.MaximumSeamPositionError <= 0.001f &&
            _buildData.MaximumSeamNormalError <= 0.0001f;
        string faceSeamStatus = faceSeamPass ? "PASS" : "FAIL";
        string lodSeamStatus = _lodValidation?.Passed == true ? "PASS" : "FAIL";

        string playerStatus = "игрок не найден";
        string radialStatus = "N/A";
        string contactStatus = "контакт: N/A";
        string seamTestStatus = "TASK-030 seam (T): N/A";
        if (_planetaryPlayer is not null)
        {
            bool radialPass =
                _planetaryPlayer.UpAlignmentErrorDegrees <= 1.0f &&
                _planetaryPlayer.GravityDirection.Dot(
                    -_planetaryPlayer.RadialUp) >= 0.9999f;
            radialStatus = radialPass ? "PASS" : "ALIGNING";
            playerStatus =
                $"r={_planetaryPlayer.RadialDistance:F1} м  •  " +
                $"ground={(_planetaryPlayer.IsGrounded ? "да" : "нет")}  •  " +
                $"vₜ={_planetaryPlayer.TangentialSpeed:F1} м/с  •  " +
                $"Δup={_planetaryPlayer.UpAlignmentErrorDegrees:F2}°";
            contactStatus =
                $"Грань: {_planetaryPlayer.CurrentFaceName}  •  " +
                $"floor={(_planetaryPlayer.IsOnFloor() ? "да" : "нет")}  •  " +
                $"probe={(_planetaryPlayer.ProbeGrounded ? "да" : "нет")}  •  " +
                $"переходы={_planetaryPlayer.LifetimeSeamCrossings}";
            seamTestStatus = _planetaryPlayer.SeamTestStatusText;
        }

        string originStatus = "TASK-032 origin (Y): N/A";
        string coordinateStatus = "Floating origin: N/A";
        if (_floatingOrigin is not null)
        {
            Vector3 local = _floatingOrigin.LocalPosition;
            coordinateStatus =
                $"Floating origin: cell=({_floatingOrigin.CellX}," +
                $"{_floatingOrigin.CellY},{_floatingOrigin.CellZ})  •  " +
                $"local=({local.X:F1},{local.Y:F1},{local.Z:F1}) м  •  " +
                $"shifts={_floatingOrigin.ShiftEvents}";
            originStatus = _floatingOrigin.TestStatusText;
        }

        bool playerCamera =
            _cameraMode == CubeSphereCameraMode.PlanetaryPlayer;
        string cameraState = playerCamera
            ? "игрок"
            : $"обзор ({orbitState})";
        string contextualSpace = playerCamera
            ? "Space — прыжок"
            : "Space — пауза обзора";

        _hudLabel.Text =
            "ПРОТОТИП C — QUADTREE LOD\n" +
            $"Патчи: {_patches.Count}  •  L{GetBaseLevel()}: {_lodLevelBasePatchCount}  •  " +
            $"L{GetFineLevel()}: {_lodLevelFinePatchCount}  •  split: {_splitParents.Count}  •  " +
            $"collision: {_collisionShapes.Count}/{(GenerateCollision ? 6 : 0)} " +
            $"({_collisionResolution}×{_collisionResolution})\n" +
            $"LOD-швы: {lodSeamStatus}  •  atomic: {_lodValidation?.AtomicSegments ?? 0}  •  " +
            $"open: {_lodValidation?.OpenSegments ?? -1}  •  " +
            $"nonManifold: {_lodValidation?.NonManifoldSegments ?? -1}  •  " +
            $"Δlod: {_lodValidation?.MaximumNeighborLevelDelta ?? -1}  •  " +
            $"Δpos: {(_lodValidation?.MaximumSeamPositionError ?? -1.0f):E2}\n" +
            $"Skirts: {_lodSkirtTriangles} треуг.  •  revision: {_lodTopologyRevision}  •  " +
            $"validation: {_lastLodUpdateMilliseconds:F2} мс  •  " +
            $"грани: {faceSeamStatus} ({_buildData.SeamComparisons}/" +
            $"{_buildData.ExpectedSeamComparisons})\n" +
            $"Игрок: {playerStatus}\n" +
            $"{contactStatus}\n" +
            $"Радиальная система: {radialStatus}  •  камера: {cameraState}  •  " +
            $"режим: {debugMode}\n" +
            $"{coordinateStatus}\n" +
            $"{originStatus}\n" +
            $"{seamTestStatus}\n" +
            $"{LodTestStatusText}\n" +
            $"Радиус: {PlanetRadius:F1} м  •  рельеф: ±{HeightAmplitude:F1} м  •  " +
            $"seed: {NoiseSeed}  •  patch: {FaceResolution}×{FaceResolution}\n" +
            "WASD — касательное движение  •  мышь — обзор  •  " +
            $"{contextualSpace}  •  R — сброс\n" +
            "F1 — грань/LOD/нормали  •  F2 — игрок/обзор  •  " +
            "T — seam-test  •  Y — origin-test  •  U — LOD-test";
    }
}
