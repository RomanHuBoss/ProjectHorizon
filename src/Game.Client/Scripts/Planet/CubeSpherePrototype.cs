using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CancellationToken = System.Threading.CancellationToken;
using CancellationTokenSource = System.Threading.CancellationTokenSource;
using System.Threading.Tasks;
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

public enum CubeSphereStreamingTestState
{
    Ready = 0,
    RunningRoute = 1,
    WaitingForSettle = 2,
    Passed = 3,
    Failed = 4,
    Cancelled = 5
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

    private sealed class ActivePatchJob
    {
        public ActivePatchJob(CubeSpherePatchBuildRequest request)
        {
            Request = request;
        }

        public CubeSpherePatchBuildRequest Request { get; }
    }

    private sealed class CompletedPatchJob
    {
        public CompletedPatchJob(
            CubeSpherePatchBuildRequest request,
            CubeSpherePatchBuildResult? result,
            bool cancelled,
            string? error)
        {
            Request = request;
            Result = result;
            Cancelled = cancelled;
            Error = error;
        }

        public CubeSpherePatchBuildRequest Request { get; }

        public CubeSpherePatchBuildResult? Result { get; }

        public bool Cancelled { get; }

        public string? Error { get; }
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

    private static readonly Vector3[] StreamingAcceptanceRoute =
    {
        Vector3.Right,
        Vector3.Up,
        Vector3.Back,
        Vector3.Left,
        Vector3.Down,
        Vector3.Forward,
        new Vector3(1.0f, 1.0f, 1.0f).Normalized(),
        new Vector3(-1.0f, 1.0f, -1.0f).Normalized(),
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

    [Export(PropertyHint.Range, "3,5,1")]
    public int LodLevelCount { get; set; } = 3;

    [Export(PropertyHint.Range, "5.0,60.0,0.5")]
    public float LodFineSplitAngleDegrees { get; set; } = 24.0f;

    [Export(PropertyHint.Range, "8.0,75.0,0.5")]
    public float LodFineMergeAngleDegrees { get; set; } = 34.0f;

    [Export(PropertyHint.Range, "91.0,150.0,1.0")]
    public float LodResidentAngleDegrees { get; set; } = 108.0f;

    [Export(PropertyHint.Range, "1,4,1")]
    public int MaxPatchWorkers { get; set; } = 4;

    [Export(PropertyHint.Range, "1,8,1")]
    public int MaxPatchAppliesPerFrame { get; set; } = 2;

    [Export(PropertyHint.Range, "0.03,0.50,0.01")]
    public float StreamingTestStepSeconds { get; set; } = 0.08f;

    [Export(PropertyHint.Range, "3.0,30.0,0.5")]
    public float StreamingTestSettleTimeoutSeconds { get; set; } = 12.0f;

    private readonly Dictionary<CubeSpherePatchKey, PatchRuntime> _patches = new();
    private readonly HashSet<CubeSpherePatchKey> _splitParents = new();
    private readonly HashSet<CubeSpherePatchKey> _logicalLeaves = new();
    private readonly HashSet<CubeSpherePatchKey> _targetResidentLeaves = new();
    private readonly List<CollisionShape3D> _collisionShapes = new();
    private readonly Queue<CubeSpherePatchKey> _pendingPatchBuilds = new();
    private readonly ConcurrentQueue<CompletedPatchJob> _completedPatchJobs = new();
    private readonly Dictionary<long, ActivePatchJob> _activePatchJobs = new();
    private readonly Dictionary<CubeSpherePatchKey, CubeSpherePatchBuildResult>
        _readyPatchResults = new();
    private readonly Queue<CubeSpherePatchKey> _readyApplyOrder = new();
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
    private CubeSphereStreamingTestState _streamingTestState =
        CubeSphereStreamingTestState.Ready;
    private bool _orbitPaused;
    private double _hudRefreshAccumulator;
    private double _lodUpdateAccumulator;
    private int _lodLevelBasePatchCount;
    private int _lodLevelMidPatchCount;
    private int _lodLevelFinePatchCount;
    private int _logicalBasePatchCount;
    private int _logicalMidPatchCount;
    private int _logicalFinePatchCount;
    private int _lodSkirtTriangles;
    private int _collisionResolution;
    private double _lastLodUpdateMilliseconds;
    private int _lodTopologyRevision;
    private int _patchPlanRevision;
    private CancellationTokenSource? _patchPlanCancellation;
    private long _nextPatchJobId;
    private int _patchWorkerLimit = 1;
    private int _patchJobsCancelled;
    private int _patchJobsStale;
    private int _patchJobsFailed;
    private int _patchesApplied;
    private int _patchesUnloaded;
    private double _lastPatchBuildMilliseconds;
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
    private Vector3 _streamingTestFocusDirection = Vector3.Right;
    private int _streamingTestRouteIndex;
    private float _streamingTestStepElapsed;
    private float _streamingTestSettleElapsed;
    private int _streamingTestStartingRevision;
    private int _streamingTestBaselineCancelled;
    private int _streamingTestBaselineStale;
    private int _streamingTestBaselineFailed;
    private int _streamingTestBaselineUnloaded;
    private int _streamingTestPeakQueue;
    private int _streamingTestPeakWorkers;
    private int _streamingTestPeakResident;
    private int _streamingTestMinimumLogicalFine;
    private string _streamingTestResult = "готов";

    public bool LodTestRunning =>
        _lodTestState == CubeSphereLodTestState.Running;

    public bool StreamingTestRunning =>
        _streamingTestState == CubeSphereStreamingTestState.RunningRoute ||
        _streamingTestState == CubeSphereStreamingTestState.WaitingForSettle;

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
        _patchWorkerLimit = Math.Max(
            1,
            Math.Min(
                Math.Clamp(MaxPatchWorkers, 1, 4),
                Math.Max(1, System.Environment.ProcessorCount - 2)));

        BuildPlanet();
        InitializeCollisionLod();
        ApplyCameraMode();
        UpdateHud();
    }

    public override void _ExitTree()
    {
        ShutdownCollisionLod();
        _patchPlanCancellation?.Cancel();
        _patchPlanCancellation?.Dispose();
        _patchPlanCancellation = null;
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

        if (StreamingTestRunning)
        {
            UpdateStreamingAcceptanceTest((float)delta);
        }

        if (CollisionTestRunning)
        {
            UpdateCollisionAcceptanceTest((float)delta);
        }

        _lodUpdateAccumulator += delta;
        if (_lodUpdateAccumulator >= Math.Max(0.05f, LodUpdateIntervalSeconds))
        {
            _lodUpdateAccumulator = 0.0;
            UpdateQuadtreeLod(GetCurrentLodFocusDirection(), false);
        }

        ProcessPatchStreamingPipeline();
        ProcessCollisionLod(delta);

        _hudRefreshAccumulator += delta;
        if (_hudRefreshAccumulator >= 0.1)
        {
            _hudRefreshAccumulator = 0.0;
            UpdateHud();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        ProcessCollisionPhysicsTransition();
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
            if (CollisionTestRunning)
            {
                CancelCollisionAcceptanceTest();
                UpdateHud();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (StreamingTestRunning)
            {
                CancelStreamingAcceptanceTest();
            }

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
            if (CollisionTestRunning)
            {
                CancelCollisionAcceptanceTest();
            }

            if (StreamingTestRunning)
            {
                CancelStreamingAcceptanceTest();
            }

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
            if (CollisionTestRunning)
            {
                CancelCollisionAcceptanceTest();
            }

            if (StreamingTestRunning)
            {
                CancelStreamingAcceptanceTest();
            }

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
        else if (keyEvent.Keycode == Key.I ||
            keyEvent.PhysicalKeycode == Key.I)
        {
            if (CollisionTestRunning)
            {
                CancelCollisionAcceptanceTest();
            }

            if (StreamingTestRunning)
            {
                CancelStreamingAcceptanceTest();
            }
            else
            {
                if (LodTestRunning)
                {
                    CancelLodAcceptanceTest();
                }

                _floatingOrigin?.CancelAcceptanceTest(true);
                _planetaryPlayer?.CancelSeamTraversalTest(true);
                BeginStreamingAcceptanceTest();
            }

            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.K ||
            keyEvent.PhysicalKeycode == Key.K)
        {
            if (CollisionTestRunning)
            {
                CancelCollisionAcceptanceTest();
            }
            else
            {
                CancelAllAcceptanceTests();
                BeginCollisionAcceptanceTest();
            }

            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.R ||
            keyEvent.PhysicalKeycode == Key.R)
        {
            if (CollisionTestRunning)
            {
                CancelCollisionAcceptanceTest();
                UpdateHud();
            }

            if (StreamingTestRunning)
            {
                CancelStreamingAcceptanceTest();
                UpdateHud();
            }

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
                ((foundationData.Resolution - 1) * (1 << GetMiddleLevel())) + 1);
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
            $"levels=L{GetBaseLevel()}-L{GetMaximumLevel()}; " +
            $"resolution={foundationData.Resolution}x{foundationData.Resolution}; " +
            $"collision={_collisionShapes.Count}@{_collisionResolution}x{_collisionResolution}; " +
            $"faceSeams={foundationData.SeamComparisons}/" +
            $"{foundationData.ExpectedSeamComparisons}; " +
            $"logical={_logicalLeaves.Count}; resident={_targetResidentLeaves.Count}; " +
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
        BuildDesiredTopology(
            normalizedFocus,
            out HashSet<CubeSpherePatchKey> desiredSplits,
            out HashSet<CubeSpherePatchKey> desiredLogicalLeaves);
        HashSet<CubeSpherePatchKey> desiredResidentLeaves =
            BuildResidentLeaves(desiredLogicalLeaves, normalizedFocus);
        bool topologyChanged =
            !_splitParents.SetEquals(desiredSplits) ||
            !_logicalLeaves.SetEquals(desiredLogicalLeaves);
        bool residentSetChanged =
            !_targetResidentLeaves.SetEquals(desiredResidentLeaves);

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
            _logicalLeaves.Clear();
            _logicalLeaves.UnionWith(desiredLogicalLeaves);
            _lodTopologyRevision++;

            if (LodTestRunning)
            {
                _lodTestSplitEvents += splitEvents;
                _lodTestMergeEvents += mergeEvents;
                _lodTestTopologyChanges++;
            }
        }

        if (topologyChanged || residentSetChanged)
        {
            _targetResidentLeaves.Clear();
            _targetResidentLeaves.UnionWith(desiredResidentLeaves);
            BeginPatchStreamingPlan();
        }

        if (topologyChanged || forceValidation || _lodValidation is null)
        {
            ulong startedAtMicroseconds = Time.GetTicksUsec();
            _lodValidation = CubeSpherePatchBuilder.ValidateTopology(
                _logicalLeaves,
                GetMaximumLevel(),
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
                $"topology={_lodTopologyRevision}; plan={_patchPlanRevision}; " +
                $"logical={_logicalLeaves.Count}; resident={_targetResidentLeaves.Count}; " +
                $"applied={_patches.Count}; L{GetBaseLevel()}={_logicalBasePatchCount}; " +
                $"L{GetMiddleLevel()}={_logicalMidPatchCount}; " +
                $"L{GetMaximumLevel()}={_logicalFinePatchCount}; " +
                $"split+={splitEvents}; merge+={mergeEvents}; " +
                $"atomic={_lodValidation.AtomicSegments}; " +
                $"open={_lodValidation.OpenSegments}; " +
                $"nonManifold={_lodValidation.NonManifoldSegments}; " +
                $"maxDelta={_lodValidation.MaximumNeighborLevelDelta}; " +
                $"seam={_lodValidation.MaximumSeamPositionError:E3}; " +
                $"validate={_lastLodUpdateMilliseconds:F2} ms");
        }
    }

    private void BuildDesiredTopology(
        Vector3 focusDirection,
        out HashSet<CubeSpherePatchKey> desiredSplits,
        out HashSet<CubeSpherePatchKey> desiredLeaves)
    {
        desiredSplits = new HashSet<CubeSpherePatchKey>();
        desiredLeaves = new HashSet<CubeSpherePatchKey>();
        int baseLevel = GetBaseLevel();
        int divisions = 1 << baseLevel;

        foreach (CubeSphereFaceId faceId in CubeSpherePatchBuilder.FaceIds)
        {
            for (int y = 0; y < divisions; y++)
            {
                for (int x = 0; x < divisions; x++)
                {
                    EvaluatePatchNode(
                        new CubeSpherePatchKey(faceId, baseLevel, x, y),
                        focusDirection,
                        desiredSplits,
                        desiredLeaves);
                }
            }
        }

        BalanceDesiredLeaves(desiredSplits, desiredLeaves);
    }

    private void EvaluatePatchNode(
        CubeSpherePatchKey key,
        Vector3 focusDirection,
        HashSet<CubeSpherePatchKey> desiredSplits,
        HashSet<CubeSpherePatchKey> desiredLeaves)
    {
        if (key.Level >= GetMaximumLevel() ||
            !ShouldSplitPatch(key, focusDirection))
        {
            desiredLeaves.Add(key);
            return;
        }

        desiredSplits.Add(key);
        foreach (CubeSpherePatchKey child in GetChildren(key))
        {
            EvaluatePatchNode(
                child,
                focusDirection,
                desiredSplits,
                desiredLeaves);
        }
    }

    private bool ShouldSplitPatch(
        CubeSpherePatchKey key,
        Vector3 focusDirection)
    {
        int depthFromBase = key.Level - GetBaseLevel();
        float splitAngle;
        float mergeAngle;

        if (depthFromBase <= 0)
        {
            splitAngle = Math.Clamp(LodSplitAngleDegrees, 1.0f, 88.0f);
            mergeAngle = Math.Max(
                splitAngle + 1.0f,
                Math.Clamp(LodMergeAngleDegrees, 2.0f, 89.0f));
        }
        else
        {
            float scale = MathF.Pow(0.58f, Math.Max(0, depthFromBase - 1));
            splitAngle = Math.Clamp(
                LodFineSplitAngleDegrees * scale,
                3.0f,
                70.0f);
            mergeAngle = Math.Clamp(
                Math.Max(splitAngle + 2.0f, LodFineMergeAngleDegrees * scale),
                splitAngle + 1.0f,
                80.0f);
        }

        float alignment = CubeSpherePatchBuilder
            .GetPatchCenterDirection(key)
            .Dot(focusDirection);
        bool wasSplit = _splitParents.Contains(key);
        float threshold = wasSplit
            ? Mathf.Cos(Mathf.DegToRad(mergeAngle))
            : Mathf.Cos(Mathf.DegToRad(splitAngle));
        return alignment >= threshold;
    }

    private void BalanceDesiredLeaves(
        HashSet<CubeSpherePatchKey> desiredSplits,
        HashSet<CubeSpherePatchKey> desiredLeaves)
    {
        int safetyCounter = 0;
        while (safetyCounter++ < 12)
        {
            IReadOnlyCollection<CubeSpherePatchKey> leavesToSplit =
                CubeSpherePatchBuilder.FindLeavesRequiringBalance(
                    desiredLeaves,
                    GetMaximumLevel());
            if (leavesToSplit.Count == 0)
            {
                return;
            }

            bool changed = false;
            foreach (CubeSpherePatchKey leaf in leavesToSplit)
            {
                if (!desiredLeaves.Remove(leaf) ||
                    leaf.Level >= GetMaximumLevel())
                {
                    continue;
                }

                desiredSplits.Add(leaf);
                foreach (CubeSpherePatchKey child in GetChildren(leaf))
                {
                    desiredLeaves.Add(child);
                }

                changed = true;
            }

            if (!changed)
            {
                return;
            }
        }
    }

    private HashSet<CubeSpherePatchKey> BuildResidentLeaves(
        IReadOnlyCollection<CubeSpherePatchKey> logicalLeaves,
        Vector3 focusDirection)
    {
        HashSet<CubeSpherePatchKey> residentLeaves = new();
        float residentAngleRadians = Mathf.DegToRad(Math.Clamp(
            LodResidentAngleDegrees,
            91.0f,
            150.0f));

        foreach (CubeSpherePatchKey leaf in logicalLeaves)
        {
            float alignment = Math.Clamp(
                CubeSpherePatchBuilder
                    .GetPatchCenterDirection(leaf)
                    .Dot(focusDirection),
                -1.0f,
                1.0f);
            float centerAngle = MathF.Acos(alignment);
            float patchAngularRadius =
                CubeSpherePatchBuilder.GetPatchAngularRadiusRadians(leaf);

            // Keep the whole patch resident while any part of its angular
            // bounding cap can still intersect the conservative view cap.
            if (centerAngle - patchAngularRadius <= residentAngleRadians)
            {
                residentLeaves.Add(leaf);
            }
        }

        return residentLeaves;
    }

    private static IReadOnlyList<CubeSpherePatchKey> GetChildren(
        CubeSpherePatchKey parent)
    {
        int level = parent.Level + 1;
        int childX = parent.X * 2;
        int childY = parent.Y * 2;
        return new[]
        {
            new CubeSpherePatchKey(parent.FaceId, level, childX, childY),
            new CubeSpherePatchKey(parent.FaceId, level, childX + 1, childY),
            new CubeSpherePatchKey(parent.FaceId, level, childX, childY + 1),
            new CubeSpherePatchKey(parent.FaceId, level, childX + 1, childY + 1)
        };
    }

    private void BeginPatchStreamingPlan()
    {
        _patchPlanCancellation?.Cancel();
        _patchPlanCancellation?.Dispose();
        _patchPlanCancellation = new CancellationTokenSource();
        _patchPlanRevision++;
        _pendingPatchBuilds.Clear();
        _readyPatchResults.Clear();
        _readyApplyOrder.Clear();

        List<CubeSpherePatchKey> missingKeys = new();
        foreach (CubeSpherePatchKey key in _targetResidentLeaves)
        {
            if (!_patches.ContainsKey(key))
            {
                missingKeys.Add(key);
            }
        }

        missingKeys.Sort(static (left, right) =>
        {
            int levelComparison = right.Level.CompareTo(left.Level);
            if (levelComparison != 0)
            {
                return levelComparison;
            }

            int faceComparison = left.FaceId.CompareTo(right.FaceId);
            if (faceComparison != 0)
            {
                return faceComparison;
            }

            int yComparison = left.Y.CompareTo(right.Y);
            return yComparison != 0 ? yComparison : left.X.CompareTo(right.X);
        });

        foreach (CubeSpherePatchKey key in missingKeys)
        {
            _pendingPatchBuilds.Enqueue(key);
        }
    }

    private void ProcessPatchStreamingPipeline()
    {
        DrainCompletedPatchJobs();
        StartPendingPatchJobs();
        ApplyReadyPatchResults();
        CommitPatchPlanIfReady();
        RecordStreamingTestMetrics();
    }

    private void DrainCompletedPatchJobs()
    {
        while (_completedPatchJobs.TryDequeue(out CompletedPatchJob? completed) &&
            completed is not null)
        {
            _activePatchJobs.Remove(completed.Request.JobId);

            if (completed.Cancelled)
            {
                _patchJobsCancelled++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(completed.Error))
            {
                _patchJobsFailed++;
                GD.PushError(
                    $"CubeSphere patch worker failed: job={completed.Request.JobId}; " +
                    $"key={completed.Request.Key.DisplayName}; {completed.Error}");
                continue;
            }

            CubeSpherePatchBuildResult? completedResult = completed.Result;
            if (completedResult is null ||
                completed.Request.PlanRevision != _patchPlanRevision ||
                !_targetResidentLeaves.Contains(completed.Request.Key))
            {
                _patchJobsStale++;
                continue;
            }

            _readyPatchResults[completed.Request.Key] = completedResult;
            _readyApplyOrder.Enqueue(completed.Request.Key);
        }
    }

    private void StartPendingPatchJobs()
    {
        if (_patchPlanCancellation is null)
        {
            return;
        }

        while (_activePatchJobs.Count < _patchWorkerLimit &&
            _pendingPatchBuilds.Count > 0)
        {
            CubeSpherePatchKey key = _pendingPatchBuilds.Dequeue();

            if (_patches.ContainsKey(key) ||
                !_targetResidentLeaves.Contains(key))
            {
                continue;
            }

            long jobId = ++_nextPatchJobId;
            CubeSpherePatchBuildRequest request = new(
                jobId,
                _patchPlanRevision,
                key,
                FaceResolution,
                PlanetRadius,
                HeightAmplitude,
                NoiseFrequency,
                NoiseSeed,
                LodSkirtDepth);
            _activePatchJobs[jobId] = new ActivePatchJob(request);
            CancellationToken cancellationToken =
                _patchPlanCancellation.Token;

            _ = Task.Run(() =>
            {
                try
                {
                    CubeSpherePatchBuildResult result =
                        CubeSpherePatchDataBuilder.Build(
                            request,
                            cancellationToken);
                    _completedPatchJobs.Enqueue(
                        new CompletedPatchJob(request, result, false, null));
                }
                catch (OperationCanceledException)
                {
                    _completedPatchJobs.Enqueue(
                        new CompletedPatchJob(request, null, true, null));
                }
                catch (Exception exception)
                {
                    _completedPatchJobs.Enqueue(
                        new CompletedPatchJob(
                            request,
                            null,
                            false,
                            exception.ToString()));
                }
            });
        }
    }

    private void ApplyReadyPatchResults()
    {
        int applyBudget = Math.Clamp(MaxPatchAppliesPerFrame, 1, 8);
        int applied = 0;

        while (applied < applyBudget && _readyApplyOrder.Count > 0)
        {
            CubeSpherePatchKey key = _readyApplyOrder.Dequeue();
            if (!_readyPatchResults.TryGetValue(
                    key,
                    out CubeSpherePatchBuildResult? result) ||
                result is null)
            {
                continue;
            }

            _readyPatchResults.Remove(key);

            if (result.Request.PlanRevision != _patchPlanRevision ||
                !_targetResidentLeaves.Contains(key))
            {
                _patchJobsStale++;
                continue;
            }

            if (!_patches.ContainsKey(key))
            {
                AddPatchFromData(result.PatchData);
                _patchesApplied++;
                _lastPatchBuildMilliseconds = result.BuildMilliseconds;
                applied++;
            }
        }
    }

    private void CommitPatchPlanIfReady()
    {
        foreach (CubeSpherePatchKey key in _targetResidentLeaves)
        {
            if (!_patches.ContainsKey(key))
            {
                return;
            }
        }

        foreach (ActivePatchJob activeJob in _activePatchJobs.Values)
        {
            if (activeJob.Request.PlanRevision == _patchPlanRevision)
            {
                return;
            }
        }

        if (_pendingPatchBuilds.Count > 0 ||
            _readyPatchResults.Count > 0)
        {
            return;
        }

        List<CubeSpherePatchKey> keysToRemove = new();
        foreach (CubeSpherePatchKey existingKey in _patches.Keys)
        {
            if (!_targetResidentLeaves.Contains(existingKey))
            {
                keysToRemove.Add(existingKey);
            }
        }

        foreach (CubeSpherePatchKey key in keysToRemove)
        {
            _patches[key].MeshInstance.Visible = false;
        }

        foreach (CubeSpherePatchKey key in _targetResidentLeaves)
        {
            _patches[key].MeshInstance.Visible = true;
        }

        foreach (CubeSpherePatchKey key in keysToRemove)
        {
            PatchRuntime runtime = _patches[key];
            runtime.MeshInstance.QueueFree();
            _patches.Remove(key);
            _patchesUnloaded++;
        }

        UpdateLodCounters();
    }

    private void AddPatchFromData(CubeSpherePatchData patchData)
    {
        if (_facesRoot is null)
        {
            return;
        }

        CubeSpherePatchKey key = patchData.Key;
        MeshInstance3D meshInstance = new()
        {
            Name = $"Patch_{SanitizeName(patchData.FaceDisplayName)}_" +
                $"L{key.Level}_{key.X}_{key.Y}",
            Mesh = CreatePatchMesh(patchData),
            Visible = false
        };
        _facesRoot.AddChild(meshInstance);
        _patches.Add(key, new PatchRuntime(patchData, meshInstance));
    }

    private bool IsPatchPlanSettled()
    {
        if (_activePatchJobs.Count > 0 ||
            _pendingPatchBuilds.Count > 0 ||
            _readyPatchResults.Count > 0 ||
            !_completedPatchJobs.IsEmpty)
        {
            return false;
        }

        if (_patches.Count != _targetResidentLeaves.Count)
        {
            return false;
        }

        foreach (CubeSpherePatchKey key in _targetResidentLeaves)
        {
            if (!_patches.ContainsKey(key))
            {
                return false;
            }
        }

        return true;
    }

    private int GetPatchQueueDepth()
    {
        return _pendingPatchBuilds.Count +
            _readyPatchResults.Count +
            _completedPatchJobs.Count;
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
            if (patchData.Key.Level >= GetMaximumLevel())
            {
                return new Color(0.95f, 0.25f, 0.72f, 1.0f);
            }

            return patchData.Key.Level == GetMiddleLevel()
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
            $"levels=L{GetBaseLevel()}-L{GetMaximumLevel()}; " +
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
            _logicalLeaves.Count);
        _lodTestMaximumPatches = Math.Max(
            _lodTestMaximumPatches,
            _logicalLeaves.Count);
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

    private void BeginStreamingAcceptanceTest()
    {
        _streamingTestState = CubeSphereStreamingTestState.RunningRoute;
        _streamingTestRouteIndex = 0;
        _streamingTestStepElapsed = 0.0f;
        _streamingTestSettleElapsed = 0.0f;
        _streamingTestFocusDirection = StreamingAcceptanceRoute[0];
        _streamingTestStartingRevision = _patchPlanRevision;
        _streamingTestBaselineCancelled = _patchJobsCancelled;
        _streamingTestBaselineStale = _patchJobsStale;
        _streamingTestBaselineFailed = _patchJobsFailed;
        _streamingTestBaselineUnloaded = _patchesUnloaded;
        _streamingTestPeakQueue = 0;
        _streamingTestPeakWorkers = 0;
        _streamingTestPeakResident = 0;
        _streamingTestMinimumLogicalFine = int.MaxValue;
        _streamingTestResult = "выполняется";

        UpdateQuadtreeLod(_streamingTestFocusDirection, true);
        RecordStreamingTestMetrics();
        GD.Print(
            "TASK-036 async patch streaming acceptance started: " +
            $"route={StreamingAcceptanceRoute.Length}; " +
            $"levels=L{GetBaseLevel()}-L{GetMaximumLevel()}; " +
            $"workers={_patchWorkerLimit}; residentAngle={LodResidentAngleDegrees:F1}");
    }

    private void UpdateStreamingAcceptanceTest(float deltaSeconds)
    {
        RecordStreamingTestMetrics();

        if (_streamingTestState == CubeSphereStreamingTestState.RunningRoute)
        {
            _streamingTestStepElapsed += deltaSeconds;
            if (_streamingTestStepElapsed <
                Math.Max(0.03f, StreamingTestStepSeconds))
            {
                return;
            }

            _streamingTestStepElapsed = 0.0f;
            _streamingTestRouteIndex++;
            if (_streamingTestRouteIndex < StreamingAcceptanceRoute.Length)
            {
                _streamingTestFocusDirection =
                    StreamingAcceptanceRoute[_streamingTestRouteIndex];
                UpdateQuadtreeLod(_streamingTestFocusDirection, true);
                return;
            }

            _streamingTestState =
                CubeSphereStreamingTestState.WaitingForSettle;
            _streamingTestFocusDirection = GetPlayerFocusDirection();
            _streamingTestSettleElapsed = 0.0f;
            UpdateQuadtreeLod(_streamingTestFocusDirection, true);
            return;
        }

        if (_streamingTestState !=
            CubeSphereStreamingTestState.WaitingForSettle)
        {
            return;
        }

        _streamingTestSettleElapsed += deltaSeconds;
        if (IsPatchPlanSettled())
        {
            UpdateLodCounters();
            bool passed =
                _patchPlanRevision - _streamingTestStartingRevision >= 4 &&
                _logicalFinePatchCount > 0 &&
                _streamingTestMinimumLogicalFine > 0 &&
                _lodLevelFinePatchCount > 0 &&
                _targetResidentLeaves.Count < _logicalLeaves.Count &&
                _patchesUnloaded > _streamingTestBaselineUnloaded &&
                _patches.Count == _targetResidentLeaves.Count &&
                _streamingTestPeakQueue > 0 &&
                _streamingTestPeakWorkers > 0 &&
                _patchJobsFailed == _streamingTestBaselineFailed &&
                _lodValidation?.Passed == true;

            FinishStreamingAcceptanceTest(
                passed
                    ? CubeSphereStreamingTestState.Passed
                    : CubeSphereStreamingTestState.Failed,
                passed
                    ? "критерии выполнены"
                    : BuildStreamingTestFailureReason());
            return;
        }

        if (_streamingTestSettleElapsed >=
            Math.Max(3.0f, StreamingTestSettleTimeoutSeconds))
        {
            FinishStreamingAcceptanceTest(
                CubeSphereStreamingTestState.Failed,
                "таймаут ожидания пустых очередей");
        }
    }

    private void CancelStreamingAcceptanceTest()
    {
        if (!StreamingTestRunning)
        {
            return;
        }

        FinishStreamingAcceptanceTest(
            CubeSphereStreamingTestState.Cancelled,
            "остановлен пользователем");
    }

    private void FinishStreamingAcceptanceTest(
        CubeSphereStreamingTestState finalState,
        string result)
    {
        _streamingTestState = finalState;
        int cancelledDelta =
            _patchJobsCancelled - _streamingTestBaselineCancelled;
        int staleDelta = _patchJobsStale - _streamingTestBaselineStale;
        int failedDelta = _patchJobsFailed - _streamingTestBaselineFailed;
        int unloadedDelta =
            _patchesUnloaded - _streamingTestBaselineUnloaded;
        string resultLabel = finalState switch
        {
            CubeSphereStreamingTestState.Passed => "PASS",
            CubeSphereStreamingTestState.Failed => "FAIL",
            _ => "CANCELLED"
        };
        string metrics =
            $"revisions={_patchPlanRevision - _streamingTestStartingRevision}, " +
            $"L{GetMaximumLevel()}={_lodLevelFinePatchCount}, " +
            $"resident={_targetResidentLeaves.Count}/{_logicalLeaves.Count}, " +
            $"unloaded={unloadedDelta}, queue=0, workers=0, " +
            $"cancel={cancelledDelta}, stale={staleDelta}, errors={failedDelta}";
        _streamingTestResult = finalState == CubeSphereStreamingTestState.Passed
            ? metrics
            : $"{result}; {metrics}";

        GD.Print(
            $"TASK-036 async patch streaming acceptance {resultLabel}: " +
            $"revisions={_patchPlanRevision - _streamingTestStartingRevision}; " +
            $"logical={_logicalLeaves.Count}; resident={_targetResidentLeaves.Count}; " +
            $"applied={_patches.Count}; L{GetMaximumLevel()}={_lodLevelFinePatchCount}; " +
            $"unloaded={unloadedDelta}; residentPeak={_streamingTestPeakResident}; " +
            $"fineMin={_streamingTestMinimumLogicalFine}; " +
            $"queuePeak={_streamingTestPeakQueue}; " +
            $"workersPeak={_streamingTestPeakWorkers}; cancelled={cancelledDelta}; " +
            $"stale={staleDelta}; errors={failedDelta}; " +
            $"open={_lodValidation?.OpenSegments ?? -1}; " +
            $"maxDelta={_lodValidation?.MaximumNeighborLevelDelta ?? -1}; " +
            $"result={result}");

        UpdateHud();
    }

    private string BuildStreamingTestFailureReason()
    {
        if (_patchJobsFailed != _streamingTestBaselineFailed)
        {
            return $"worker errors: {_patchJobsFailed - _streamingTestBaselineFailed}";
        }

        if (_lodValidation?.Passed != true)
        {
            return "логическая LOD-топология не прошла проверку";
        }

        if (_logicalFinePatchCount <= 0 || _lodLevelFinePatchCount <= 0)
        {
            return $"не подтверждён L{GetMaximumLevel()}";
        }

        if (_targetResidentLeaves.Count >= _logicalLeaves.Count)
        {
            return "невидимые patches не были исключены из resident-set";
        }

        if (_patchesUnloaded <= _streamingTestBaselineUnloaded)
        {
            return "не подтверждена выгрузка старых или невидимых patches";
        }

        if (_patches.Count != _targetResidentLeaves.Count)
        {
            return "applied-set не совпадает с resident-set";
        }

        if (_streamingTestPeakQueue <= 0 || _streamingTestPeakWorkers <= 0)
        {
            return "фоновые jobs не наблюдались";
        }

        return "недостаточно подтверждённых plan revisions";
    }

    private void RecordStreamingTestMetrics()
    {
        if (!StreamingTestRunning)
        {
            return;
        }

        _streamingTestPeakQueue = Math.Max(
            _streamingTestPeakQueue,
            GetPatchQueueDepth());
        _streamingTestPeakWorkers = Math.Max(
            _streamingTestPeakWorkers,
            _activePatchJobs.Count);
        _streamingTestPeakResident = Math.Max(
            _streamingTestPeakResident,
            _targetResidentLeaves.Count);
        _streamingTestMinimumLogicalFine = Math.Min(
            _streamingTestMinimumLogicalFine,
            _logicalFinePatchCount);
    }

    private void CancelAllAcceptanceTests()
    {
        if (CollisionTestRunning)
        {
            CancelCollisionAcceptanceTest();
        }

        if (StreamingTestRunning)
        {
            CancelStreamingAcceptanceTest();
        }

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
        if (StreamingTestRunning)
        {
            return _streamingTestFocusDirection;
        }

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

    private int GetMiddleLevel()
    {
        return Math.Min(10, GetBaseLevel() + 1);
    }

    private int GetMaximumLevel()
    {
        int levelCount = Math.Clamp(LodLevelCount, 3, 5);
        return Math.Min(10, GetBaseLevel() + levelCount - 1);
    }

    private int GetMinimumPatchCount()
    {
        int baseDivisions = 1 << GetBaseLevel();
        return 6 * baseDivisions * baseDivisions;
    }

    private int GetMaximumPatchCount()
    {
        int basePatchCount = GetMinimumPatchCount();
        int additionalLevels = GetMaximumLevel() - GetBaseLevel();
        return basePatchCount * (1 << (additionalLevels * 2));
    }

    private void UpdateLodCounters()
    {
        _lodLevelBasePatchCount = 0;
        _lodLevelMidPatchCount = 0;
        _lodLevelFinePatchCount = 0;
        _logicalBasePatchCount = 0;
        _logicalMidPatchCount = 0;
        _logicalFinePatchCount = 0;
        _lodSkirtTriangles = 0;

        foreach (PatchRuntime runtime in _patches.Values)
        {
            if (runtime.Data.Key.Level == GetBaseLevel())
            {
                _lodLevelBasePatchCount++;
            }
            else if (runtime.Data.Key.Level == GetMiddleLevel())
            {
                _lodLevelMidPatchCount++;
            }
            else if (runtime.Data.Key.Level >= GetMaximumLevel())
            {
                _lodLevelFinePatchCount++;
            }

            _lodSkirtTriangles += runtime.Data.SkirtTriangleCount;
        }

        foreach (CubeSpherePatchKey key in _logicalLeaves)
        {
            if (key.Level == GetBaseLevel())
            {
                _logicalBasePatchCount++;
            }
            else if (key.Level == GetMiddleLevel())
            {
                _logicalMidPatchCount++;
            }
            else if (key.Level >= GetMaximumLevel())
            {
                _logicalFinePatchCount++;
            }
        }
    }

    private void ClearGeneratedChildren()
    {
        ClearDynamicCollisionLod();
        _patchPlanCancellation?.Cancel();
        _patchPlanCancellation?.Dispose();
        _patchPlanCancellation = null;

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
        _logicalLeaves.Clear();
        _targetResidentLeaves.Clear();
        _collisionShapes.Clear();
        _pendingPatchBuilds.Clear();
        _readyPatchResults.Clear();
        _readyApplyOrder.Clear();
        _activePatchJobs.Clear();
        while (_completedPatchJobs.TryDequeue(out _))
        {
        }
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

    private string StreamingTestStatusText
    {
        get
        {
            return _streamingTestState switch
            {
                CubeSphereStreamingTestState.RunningRoute =>
                    $"TASK-036 stream (I): RUNNING {_streamingTestRouteIndex + 1}/" +
                    $"{StreamingAcceptanceRoute.Length}, queue={GetPatchQueueDepth()}, " +
                    $"workers={_activePatchJobs.Count}/{_patchWorkerLimit}",
                CubeSphereStreamingTestState.WaitingForSettle =>
                    $"TASK-036 stream (I): SETTLING " +
                    $"{_streamingTestSettleElapsed:F1}/" +
                    $"{StreamingTestSettleTimeoutSeconds:F1} c, " +
                    $"queue={GetPatchQueueDepth()}, workers={_activePatchJobs.Count}",
                CubeSphereStreamingTestState.Passed =>
                    $"TASK-036 stream (I): PASS {_streamingTestResult}",
                CubeSphereStreamingTestState.Failed =>
                    $"TASK-036 stream (I): FAIL — {_streamingTestResult}",
                CubeSphereStreamingTestState.Cancelled =>
                    "TASK-036 stream (I): CANCELLED",
                _ => "TASK-036 stream (I): READY"
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
                "ПРОТОТИП C — ASYNC VISUAL + COLLISION LOD\n" +
                "Построение collision и планирование patches...";
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
            "ПРОТОТИП C — ASYNC VISUAL + COLLISION LOD\n" +
            $"Applied/resident/logical: {_patches.Count}/" +
            $"{_targetResidentLeaves.Count}/{_logicalLeaves.Count}  •  " +
            $"L{GetBaseLevel()}: {_lodLevelBasePatchCount}/{_logicalBasePatchCount}  •  " +
            $"L{GetMiddleLevel()}: {_lodLevelMidPatchCount}/{_logicalMidPatchCount}  •  " +
            $"L{GetMaximumLevel()}: {_lodLevelFinePatchCount}/{_logicalFinePatchCount}\n" +
            $"Stream: plan={_patchPlanRevision}  •  queue={GetPatchQueueDepth()}  •  " +
            $"workers={_activePatchJobs.Count}/{_patchWorkerLimit}  •  " +
            $"ready={_readyPatchResults.Count}  •  applied={_patchesApplied}  •  " +
            $"unloaded={_patchesUnloaded}\n" +
            $"Jobs: cancel={_patchJobsCancelled}  •  stale={_patchJobsStale}  •  " +
            $"errors={_patchJobsFailed}  •  lastBuild={_lastPatchBuildMilliseconds:F2} мс  •  " +
            $"cull={LodResidentAngleDegrees:F0}°+extent\n" +
            $"Collision LOD: active={_collisionPatches.Count}/" +
            $"{_targetCollisionLeaves.Count}  •  staged={_stagedCollisionPatches.Count}  •  " +
            $"queue={_pendingCollisionBuilds.Count}  •  plan={_collisionPlanRevision}  •  " +
            $"commits={_collisionCommits}  •  state={_collisionTransitionState}\n" +
            $"Collision jobs: L{GetBaseLevel()}={_collisionBasePatchCount}  •  " +
            $"L{GetMiddleLevel()}={_collisionMidPatchCount}  •  " +
            $"L{GetMaximumLevel()}={_collisionFinePatchCount}  •  " +
            $"created={_collisionPatchesCreated}  •  unloaded={_collisionPatchesUnloaded}  •  " +
            $"fallback={(_fallbackCollisionEnabled ? "on" : "off")}  •  " +
            $"activations={_collisionFallbackActivations}  •  " +
            $"recoveries={_collisionSafetyRecoveries}  •  errors={_collisionErrors}  •  " +
            $"lastBuild={_lastCollisionBuildMilliseconds:F2} мс\n" +
            $"LOD-швы: {lodSeamStatus}  •  atomic={_lodValidation?.AtomicSegments ?? 0}  •  " +
            $"open={_lodValidation?.OpenSegments ?? -1}  •  " +
            $"nonManifold={_lodValidation?.NonManifoldSegments ?? -1}  •  " +
            $"Δlod={_lodValidation?.MaximumNeighborLevelDelta ?? -1}  •  " +
            $"Δpos={(_lodValidation?.MaximumSeamPositionError ?? -1.0f):E2}\n" +
            $"Skirts: {_lodSkirtTriangles}  •  topology={_lodTopologyRevision}  •  " +
            $"validation={_lastLodUpdateMilliseconds:F2} мс  •  " +
            $"fallbackCollision={_collisionShapes.Count}/{(GenerateCollision ? 6 : 0)} " +
            $"({_collisionResolution}×{_collisionResolution})  •  " +
            $"грани={faceSeamStatus} ({_buildData.SeamComparisons}/" +
            $"{_buildData.ExpectedSeamComparisons})\n" +
            $"Игрок: {playerStatus}\n" +
            $"{contactStatus}\n" +
            $"Радиальная система: {radialStatus}  •  камера: {cameraState}  •  " +
            $"режим: {debugMode}\n" +
            $"{coordinateStatus}\n" +
            $"{originStatus}\n" +
            $"{seamTestStatus}\n" +
            $"{LodTestStatusText}\n" +
            $"{StreamingTestStatusText}\n" +
            $"{CollisionTestStatusText}\n" +
            $"Радиус: {PlanetRadius:F1} м  •  рельеф: ±{HeightAmplitude:F1} м  •  " +
            $"seed: {NoiseSeed}  •  patch: {FaceResolution}×{FaceResolution}\n" +
            "WASD — касательное движение  •  мышь — обзор  •  " +
            $"{contextualSpace}  •  R — сброс\n" +
            "F1 — грань/LOD/нормали  •  F2 — игрок/обзор  •  " +
            "T — seam  •  Y — origin  •  U — LOD  •  I — async-stream  •  " +
            "K — collision-LOD";
    }

}
