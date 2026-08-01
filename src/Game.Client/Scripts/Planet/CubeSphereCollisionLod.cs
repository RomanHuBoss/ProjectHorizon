using System;
using System.Collections.Generic;
using Godot;

public enum CubeSphereCollisionTransitionState
{
    Idle = 0,
    Building = 1,
    WaitingForEnable = 2,
    Overlap = 3,
    WaitingForDisable = 4
}

public enum CubeSphereCollisionTestState
{
    Ready = 0,
    Preparing = 1,
    Traversing = 2,
    WaitingForSettle = 3,
    Passed = 4,
    Failed = 5,
    Cancelled = 6
}

public partial class CubeSpherePrototype
{
    private sealed class CollisionPatchRuntime
    {
        public CollisionPatchRuntime(
            CubeSpherePatchKey key,
            CollisionShape3D collisionShape)
        {
            Key = key;
            CollisionShape = collisionShape;
        }

        public CubeSpherePatchKey Key { get; }

        public CollisionShape3D CollisionShape { get; }
    }

    [Export]
    public bool EnableDynamicCollisionLod { get; set; } = true;

    [Export(PropertyHint.Range, "10.0,80.0,0.5")]
    public float CollisionResidentAngleDegrees { get; set; } = 42.0f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05")]
    public float CollisionUpdateIntervalSeconds { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "1,8,1")]
    public int MaxCollisionAppliesPerFrame { get; set; } = 2;

    [Export(PropertyHint.Range, "1,6,1")]
    public int CollisionOverlapPhysicsFrames { get; set; } = 2;

    [Export(PropertyHint.Range, "4.0,40.0,0.5")]
    public float CollisionSafetyFallbackAngleDegrees { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "20.0,120.0,1.0")]
    public float CollisionTestTimeoutSeconds { get; set; } = 75.0f;

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float CollisionTestAllowedGroundGapSeconds { get; set; } = 0.12f;

    private readonly Dictionary<CubeSpherePatchKey, CollisionPatchRuntime>
        _collisionPatches = new();
    private readonly Dictionary<CubeSpherePatchKey, CollisionPatchRuntime>
        _stagedCollisionPatches = new();
    private readonly HashSet<CubeSpherePatchKey> _targetCollisionLeaves = new();
    private readonly HashSet<CubeSpherePatchKey> _appliedCollisionLeaves = new();
    private readonly Queue<CubeSpherePatchKey> _pendingCollisionBuilds = new();
    private CubeSphereCollisionTransitionState _collisionTransitionState =
        CubeSphereCollisionTransitionState.Idle;
    private CubeSphereCollisionTestState _collisionTestState =
        CubeSphereCollisionTestState.Ready;
    private double _collisionUpdateAccumulator;
    private int _collisionPlanRevision;
    private int _collisionCommits;
    private int _collisionPatchesCreated;
    private int _collisionPatchesUnloaded;
    private int _collisionFallbackActivations;
    private int _collisionErrors;
    private int _collisionOverlapFramesRemaining;
    private int _collisionBasePatchCount;
    private int _collisionMidPatchCount;
    private int _collisionFinePatchCount;
    private double _lastCollisionBuildMilliseconds;
    private bool _fallbackCollisionEnabled = true;
    private Vector3 _collisionAnchorDirection = Vector3.Right;
    private string _collisionLastError = string.Empty;

    private float _collisionTestElapsed;
    private float _collisionTestGroundGapCurrent;
    private float _collisionTestMaximumGroundGap;
    private int _collisionTestBaselinePlan;
    private int _collisionTestBaselineCommits;
    private int _collisionTestBaselineCreated;
    private int _collisionTestBaselineUnloaded;
    private int _collisionTestBaselineFallbackActivations;
    private int _collisionTestBaselineErrors;
    private int _collisionTestMinimumActive = int.MaxValue;
    private int _collisionTestMaximumActive;
    private string _collisionTestResult = "готов";

    public bool CollisionTestRunning =>
        _collisionTestState == CubeSphereCollisionTestState.Preparing ||
        _collisionTestState == CubeSphereCollisionTestState.Traversing ||
        _collisionTestState == CubeSphereCollisionTestState.WaitingForSettle;

    public string CollisionTestStatusText
    {
        get
        {
            string metrics =
                $"plans={_collisionPlanRevision - _collisionTestBaselinePlan}, " +
                $"commits={_collisionCommits - _collisionTestBaselineCommits}, " +
                $"active={_collisionPatches.Count}/{_targetCollisionLeaves.Count}, " +
                $"gap={_collisionTestMaximumGroundGap:F2} с, " +
                $"fallback={(_fallbackCollisionEnabled ? "on" : "off")}, " +
                $"errors={_collisionErrors - _collisionTestBaselineErrors}";

            return _collisionTestState switch
            {
                CubeSphereCollisionTestState.Preparing =>
                    $"TASK-038 collision (K): PREPARING {metrics}",
                CubeSphereCollisionTestState.Traversing =>
                    $"TASK-038 collision (K): RUNNING {metrics}",
                CubeSphereCollisionTestState.WaitingForSettle =>
                    $"TASK-038 collision (K): SETTLING {metrics}",
                CubeSphereCollisionTestState.Passed =>
                    $"TASK-038 collision (K): PASS {_collisionTestResult}",
                CubeSphereCollisionTestState.Failed =>
                    $"TASK-038 collision (K): FAIL {_collisionTestResult}",
                CubeSphereCollisionTestState.Cancelled =>
                    "TASK-038 collision (K): остановлен пользователем",
                _ => "TASK-038 collision (K): READY"
            };
        }
    }

    private void InitializeCollisionLod()
    {
        _collisionUpdateAccumulator = CollisionUpdateIntervalSeconds;
        _collisionTransitionState = CubeSphereCollisionTransitionState.Idle;
        _collisionTestState = CubeSphereCollisionTestState.Ready;
        _fallbackCollisionEnabled = true;
        _collisionAnchorDirection = GetPlayerFocusDirection();
        _collisionLastError = string.Empty;
        SetFallbackCollisionEnabled(true, false);
    }

    private void ShutdownCollisionLod()
    {
        ClearDynamicCollisionLod();
    }

    private void ProcessCollisionLod(double delta)
    {
        if (!GenerateCollision || !EnableDynamicCollisionLod)
        {
            return;
        }

        ProcessCollisionBuildQueue();
        EnsureCollisionSafetyFallback();

        _collisionUpdateAccumulator += delta;
        if (_collisionUpdateAccumulator <
            Math.Max(0.05f, CollisionUpdateIntervalSeconds))
        {
            return;
        }

        _collisionUpdateAccumulator = 0.0;
        if (_collisionTransitionState != CubeSphereCollisionTransitionState.Idle ||
            !IsPatchPlanSettled())
        {
            return;
        }

        HashSet<CubeSpherePatchKey> desired = BuildCollisionTarget(
            GetPlayerFocusDirection());
        bool targetChanged = !_appliedCollisionLeaves.SetEquals(desired);
        if (targetChanged || _fallbackCollisionEnabled)
        {
            BeginCollisionPlan(desired);
        }
    }

    private void ProcessCollisionPhysicsTransition()
    {
        if (!GenerateCollision || !EnableDynamicCollisionLod)
        {
            return;
        }

        if (_collisionTransitionState ==
            CubeSphereCollisionTransitionState.WaitingForEnable)
        {
            foreach (CollisionPatchRuntime runtime in
                _stagedCollisionPatches.Values)
            {
                if (runtime.CollisionShape.Disabled)
                {
                    return;
                }
            }

            _collisionOverlapFramesRemaining = Math.Max(
                1,
                CollisionOverlapPhysicsFrames);
            _collisionTransitionState =
                CubeSphereCollisionTransitionState.Overlap;
            return;
        }

        if (_collisionTransitionState ==
            CubeSphereCollisionTransitionState.Overlap)
        {
            _collisionOverlapFramesRemaining--;
            if (_collisionOverlapFramesRemaining > 0)
            {
                return;
            }

            foreach (KeyValuePair<CubeSpherePatchKey, CollisionPatchRuntime> pair
                in _collisionPatches)
            {
                if (!_targetCollisionLeaves.Contains(pair.Key))
                {
                    SetCollisionShapeEnabled(pair.Value.CollisionShape, false);
                }
            }

            SetFallbackCollisionEnabled(false, false);
            _collisionTransitionState =
                CubeSphereCollisionTransitionState.WaitingForDisable;
            return;
        }

        if (_collisionTransitionState ==
            CubeSphereCollisionTransitionState.WaitingForDisable)
        {
            if (_fallbackCollisionEnabled ||
                !AreFallbackCollisionShapesDisabled())
            {
                return;
            }

            foreach (KeyValuePair<CubeSpherePatchKey, CollisionPatchRuntime> pair
                in _collisionPatches)
            {
                if (!_targetCollisionLeaves.Contains(pair.Key) &&
                    !pair.Value.CollisionShape.Disabled)
                {
                    return;
                }
            }

            CommitCollisionPlan();
        }
    }

    private HashSet<CubeSpherePatchKey> BuildCollisionTarget(Vector3 focusDirection)
    {
        HashSet<CubeSpherePatchKey> target = new();
        float residentAngle = Mathf.DegToRad(
            Math.Clamp(CollisionResidentAngleDegrees, 10.0f, 80.0f));
        Vector3 normalizedFocus = focusDirection.LengthSquared() <= 0.000001f
            ? Vector3.Right
            : focusDirection.Normalized();

        foreach (CubeSpherePatchKey key in _targetResidentLeaves)
        {
            Vector3 centerDirection =
                CubeSpherePatchBuilder.GetPatchCenterDirection(key);
            float angularDistance = Mathf.Acos(Mathf.Clamp(
                normalizedFocus.Dot(centerDirection),
                -1.0f,
                1.0f));
            float angularExtent =
                CubeSpherePatchBuilder.GetPatchAngularRadiusRadians(key);
            if (angularDistance <= residentAngle + angularExtent)
            {
                target.Add(key);
            }
        }

        if (target.Count == 0 && _targetResidentLeaves.Count > 0)
        {
            CubeSpherePatchKey nearest = default;
            float nearestDot = float.NegativeInfinity;
            foreach (CubeSpherePatchKey key in _targetResidentLeaves)
            {
                float candidateDot = normalizedFocus.Dot(
                    CubeSpherePatchBuilder.GetPatchCenterDirection(key));
                if (candidateDot > nearestDot)
                {
                    nearestDot = candidateDot;
                    nearest = key;
                }
            }

            target.Add(nearest);
        }

        return target;
    }

    private void BeginCollisionPlan(HashSet<CubeSpherePatchKey> desired)
    {
        ClearStagedCollisionPatches();
        _pendingCollisionBuilds.Clear();
        _targetCollisionLeaves.Clear();
        foreach (CubeSpherePatchKey key in desired)
        {
            _targetCollisionLeaves.Add(key);
        }

        _collisionPlanRevision++;
        _collisionLastError = string.Empty;
        SetFallbackCollisionEnabled(true, true);

        foreach (CubeSpherePatchKey key in _targetCollisionLeaves)
        {
            if (!_collisionPatches.ContainsKey(key))
            {
                _pendingCollisionBuilds.Enqueue(key);
            }
        }

        _collisionTransitionState = CubeSphereCollisionTransitionState.Building;
        if (_pendingCollisionBuilds.Count == 0)
        {
            ArmCollisionPlan();
        }

        GD.Print(
            "Collision LOD plan: " +
            $"revision={_collisionPlanRevision}; target={_targetCollisionLeaves.Count}; " +
            $"reuse={CountCollisionReuse()}; build={_pendingCollisionBuilds.Count}; " +
            $"fallback={_fallbackCollisionEnabled}");
    }

    private void ProcessCollisionBuildQueue()
    {
        if (_collisionTransitionState !=
            CubeSphereCollisionTransitionState.Building)
        {
            return;
        }

        int budget = Math.Clamp(MaxCollisionAppliesPerFrame, 1, 8);
        while (budget-- > 0 && _pendingCollisionBuilds.Count > 0)
        {
            CubeSpherePatchKey key = _pendingCollisionBuilds.Dequeue();
            if (!_patches.TryGetValue(key, out PatchRuntime? visualRuntime) ||
                visualRuntime is null)
            {
                AbortCollisionPlan(
                    $"visual patch unavailable: {key.DisplayName}");
                return;
            }

            try
            {
                ulong startedAtMicroseconds = Time.GetTicksUsec();
                ArrayMesh collisionMesh =
                    CreatePatchCollisionMesh(visualRuntime.Data);
                ConcavePolygonShape3D shape =
                    collisionMesh.CreateTrimeshShape();
                shape.BackfaceCollision = true;
                CollisionShape3D collisionShape = new()
                {
                    Name = $"CollisionPatch_{key.DisplayName}",
                    Shape = shape,
                    Disabled = true,
                    DebugColor = GetCollisionDebugColor(key)
                };
                _collisionBody!.AddChild(collisionShape);
                _stagedCollisionPatches.Add(
                    key,
                    new CollisionPatchRuntime(key, collisionShape));
                _collisionPatchesCreated++;
                _lastCollisionBuildMilliseconds =
                    (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;
            }
            catch (Exception exception)
            {
                AbortCollisionPlan(
                    $"{key.DisplayName}: {exception.GetType().Name}: " +
                    exception.Message);
                return;
            }
        }

        if (_pendingCollisionBuilds.Count == 0)
        {
            ArmCollisionPlan();
        }
    }

    private void ArmCollisionPlan()
    {
        foreach (CollisionPatchRuntime runtime in
            _stagedCollisionPatches.Values)
        {
            SetCollisionShapeEnabled(runtime.CollisionShape, true);
        }

        _collisionTransitionState =
            CubeSphereCollisionTransitionState.WaitingForEnable;
    }

    private void CommitCollisionPlan()
    {
        List<CubeSpherePatchKey> obsolete = new();
        foreach (CubeSpherePatchKey key in _collisionPatches.Keys)
        {
            if (!_targetCollisionLeaves.Contains(key))
            {
                obsolete.Add(key);
            }
        }

        foreach (CubeSpherePatchKey key in obsolete)
        {
            CollisionPatchRuntime runtime = _collisionPatches[key];
            runtime.CollisionShape.QueueFree();
            _collisionPatches.Remove(key);
            _collisionPatchesUnloaded++;
        }

        foreach (KeyValuePair<CubeSpherePatchKey, CollisionPatchRuntime> pair in
            _stagedCollisionPatches)
        {
            _collisionPatches.Add(pair.Key, pair.Value);
        }

        _stagedCollisionPatches.Clear();
        _appliedCollisionLeaves.Clear();
        foreach (CubeSpherePatchKey key in _targetCollisionLeaves)
        {
            _appliedCollisionLeaves.Add(key);
        }

        _collisionAnchorDirection = GetPlayerFocusDirection();
        _collisionTransitionState = CubeSphereCollisionTransitionState.Idle;
        _collisionCommits++;
        UpdateCollisionCounters();

        GD.Print(
            "Collision LOD commit: " +
            $"revision={_collisionPlanRevision}; active={_collisionPatches.Count}; " +
            $"L{GetBaseLevel()}={_collisionBasePatchCount}; " +
            $"L{GetMiddleLevel()}={_collisionMidPatchCount}; " +
            $"L{GetMaximumLevel()}={_collisionFinePatchCount}; " +
            $"unloaded={_collisionPatchesUnloaded}; fallback=off");
    }

    private void AbortCollisionPlan(string error)
    {
        _collisionErrors++;
        _collisionLastError = error;
        _pendingCollisionBuilds.Clear();
        ClearStagedCollisionPatches();
        SetFallbackCollisionEnabled(true, true);
        _collisionTransitionState = CubeSphereCollisionTransitionState.Idle;
        GD.PushError("Collision LOD plan failed: " + error);
    }

    private void EnsureCollisionSafetyFallback()
    {
        if (_collisionPatches.Count == 0 ||
            _collisionTransitionState != CubeSphereCollisionTransitionState.Idle ||
            _fallbackCollisionEnabled)
        {
            return;
        }

        Vector3 focusDirection = GetPlayerFocusDirection();
        float angularDistance = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
            focusDirection.Dot(_collisionAnchorDirection),
            -1.0f,
            1.0f)));
        if (angularDistance > Math.Max(
            4.0f,
            CollisionSafetyFallbackAngleDegrees))
        {
            SetFallbackCollisionEnabled(true, true);
            _collisionUpdateAccumulator = CollisionUpdateIntervalSeconds;
            GD.Print(
                "Collision safety fallback enabled: " +
                $"player moved {angularDistance:F1}° from collision anchor.");
        }
    }

    private ArrayMesh CreatePatchCollisionMesh(CubeSpherePatchData patchData)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < patchData.TopVertexCount; i++)
        {
            surfaceTool.SetNormal(patchData.Normals[i]);
            surfaceTool.AddVertex(patchData.Vertices[i]);
        }

        int topIndexCount = patchData.TopTriangleCount * 3;
        for (int i = 0; i < topIndexCount; i++)
        {
            surfaceTool.AddIndex(patchData.Indices[i]);
        }

        return surfaceTool.Commit();
    }

    private void SetFallbackCollisionEnabled(bool enabled, bool countActivation)
    {
        if (_fallbackCollisionEnabled == enabled)
        {
            return;
        }

        _fallbackCollisionEnabled = enabled;
        if (enabled && countActivation)
        {
            _collisionFallbackActivations++;
        }

        foreach (CollisionShape3D collisionShape in _collisionShapes)
        {
            SetCollisionShapeEnabled(collisionShape, enabled);
        }
    }

    private static void SetCollisionShapeEnabled(
        CollisionShape3D collisionShape,
        bool enabled)
    {
        collisionShape.SetDeferred(
            CollisionShape3D.PropertyName.Disabled,
            !enabled);
    }

    private bool AreFallbackCollisionShapesDisabled()
    {
        foreach (CollisionShape3D collisionShape in _collisionShapes)
        {
            if (!collisionShape.Disabled)
            {
                return false;
            }
        }

        return true;
    }

    private Color GetCollisionDebugColor(CubeSpherePatchKey key)
    {
        if (key.Level >= GetMaximumLevel())
        {
            return new Color(1.0f, 0.18f, 0.62f, 0.55f);
        }

        return key.Level == GetMiddleLevel()
            ? new Color(1.0f, 0.55f, 0.12f, 0.5f)
            : new Color(0.18f, 0.68f, 1.0f, 0.45f);
    }

    private int CountCollisionReuse()
    {
        int reused = 0;
        foreach (CubeSpherePatchKey key in _targetCollisionLeaves)
        {
            if (_collisionPatches.ContainsKey(key))
            {
                reused++;
            }
        }

        return reused;
    }

    private void UpdateCollisionCounters()
    {
        _collisionBasePatchCount = 0;
        _collisionMidPatchCount = 0;
        _collisionFinePatchCount = 0;
        foreach (CubeSpherePatchKey key in _collisionPatches.Keys)
        {
            if (key.Level == GetBaseLevel())
            {
                _collisionBasePatchCount++;
            }
            else if (key.Level == GetMiddleLevel())
            {
                _collisionMidPatchCount++;
            }
            else if (key.Level >= GetMaximumLevel())
            {
                _collisionFinePatchCount++;
            }
        }
    }

    private bool IsCollisionLodSettled()
    {
        if (!GenerateCollision || !EnableDynamicCollisionLod)
        {
            return true;
        }

        if (_collisionTransitionState != CubeSphereCollisionTransitionState.Idle ||
            _pendingCollisionBuilds.Count > 0 ||
            _stagedCollisionPatches.Count > 0 ||
            _fallbackCollisionEnabled ||
            _collisionPatches.Count == 0 ||
            !_appliedCollisionLeaves.SetEquals(_targetCollisionLeaves))
        {
            return false;
        }

        foreach (CubeSpherePatchKey key in _targetCollisionLeaves)
        {
            if (!_collisionPatches.TryGetValue(
                    key,
                    out CollisionPatchRuntime? runtime) ||
                runtime is null ||
                runtime.CollisionShape.Disabled)
            {
                return false;
            }
        }

        return true;
    }

    private void BeginCollisionAcceptanceTest()
    {
        if (_planetaryPlayer is null ||
            _cameraMode != CubeSphereCameraMode.PlanetaryPlayer ||
            !GenerateCollision ||
            !EnableDynamicCollisionLod)
        {
            GD.Print(
                "TASK-038 collision acceptance requires dynamic collision " +
                "and planetary player camera mode.");
            return;
        }

        _collisionTestState = CubeSphereCollisionTestState.Preparing;
        _collisionTestElapsed = 0.0f;
        _collisionTestGroundGapCurrent = 0.0f;
        _collisionTestMaximumGroundGap = 0.0f;
        _collisionTestBaselinePlan = _collisionPlanRevision;
        _collisionTestBaselineCommits = _collisionCommits;
        _collisionTestBaselineCreated = _collisionPatchesCreated;
        _collisionTestBaselineUnloaded = _collisionPatchesUnloaded;
        _collisionTestBaselineFallbackActivations =
            _collisionFallbackActivations;
        _collisionTestBaselineErrors = _collisionErrors;
        _collisionTestMinimumActive = int.MaxValue;
        _collisionTestMaximumActive = 0;
        _collisionTestResult = "подготовка";
        _planetaryPlayer.CancelSeamTraversalTest(true);
        _planetaryPlayer.SetExternalMovementLocked(true);
        _collisionUpdateAccumulator = CollisionUpdateIntervalSeconds;

        GD.Print(
            "TASK-038 dynamic collision LOD acceptance started: " +
            $"angle={CollisionResidentAngleDegrees:F1}°; " +
            $"overlapFrames={CollisionOverlapPhysicsFrames}; " +
            $"fallback={_collisionShapes.Count}@{_collisionResolution}x" +
            $"{_collisionResolution}");
    }

    private void UpdateCollisionAcceptanceTest(float deltaSeconds)
    {
        if (!CollisionTestRunning || _planetaryPlayer is null)
        {
            return;
        }

        _collisionTestElapsed += deltaSeconds;
        _collisionTestMinimumActive = Math.Min(
            _collisionTestMinimumActive,
            _collisionPatches.Count);
        _collisionTestMaximumActive = Math.Max(
            _collisionTestMaximumActive,
            _collisionPatches.Count);

        if (_collisionTestState == CubeSphereCollisionTestState.Traversing ||
            _collisionTestState == CubeSphereCollisionTestState.WaitingForSettle)
        {
            if (_planetaryPlayer.HasPhysicalGroundContact)
            {
                _collisionTestGroundGapCurrent = 0.0f;
            }
            else
            {
                _collisionTestGroundGapCurrent += deltaSeconds;
                _collisionTestMaximumGroundGap = Math.Max(
                    _collisionTestMaximumGroundGap,
                    _collisionTestGroundGapCurrent);
            }
        }

        if (_collisionErrors != _collisionTestBaselineErrors)
        {
            FinishCollisionAcceptanceTest(
                CubeSphereCollisionTestState.Failed,
                $"collision errors={_collisionErrors - _collisionTestBaselineErrors}");
            return;
        }

        if (_collisionTestElapsed > CollisionTestTimeoutSeconds)
        {
            FinishCollisionAcceptanceTest(
                CubeSphereCollisionTestState.Failed,
                "timeout");
            return;
        }

        if (_collisionTestState == CubeSphereCollisionTestState.Preparing)
        {
            if (!IsPatchPlanSettled() || !IsCollisionLodSettled() ||
                !_planetaryPlayer.HasPhysicalGroundContact)
            {
                return;
            }

            _planetaryPlayer.SetExternalMovementLocked(false);
            if (!_planetaryPlayer.BeginSeamTraversalTest())
            {
                FinishCollisionAcceptanceTest(
                    CubeSphereCollisionTestState.Failed,
                    "не удалось запустить seam traversal");
                return;
            }

            _collisionTestState = CubeSphereCollisionTestState.Traversing;
            _collisionTestResult = "межгранный маршрут";
            return;
        }

        if (_collisionTestState == CubeSphereCollisionTestState.Traversing)
        {
            if (_collisionTestMaximumGroundGap >
                CollisionTestAllowedGroundGapSeconds)
            {
                FinishCollisionAcceptanceTest(
                    CubeSphereCollisionTestState.Failed,
                    $"ground gap={_collisionTestMaximumGroundGap:F2} с");
                return;
            }

            if (_planetaryPlayer.SeamTestState ==
                PlanetarySeamTestState.Failed)
            {
                FinishCollisionAcceptanceTest(
                    CubeSphereCollisionTestState.Failed,
                    "вложенный seam-test завершился FAIL");
                return;
            }

            if (_planetaryPlayer.SeamTestState ==
                PlanetarySeamTestState.Passed)
            {
                _collisionTestState =
                    CubeSphereCollisionTestState.WaitingForSettle;
                _collisionTestResult = "ожидание финального collision plan";
                _collisionUpdateAccumulator = CollisionUpdateIntervalSeconds;
            }

            return;
        }

        if (!IsPatchPlanSettled() || !IsCollisionLodSettled() ||
            !_planetaryPlayer.HasPhysicalGroundContact)
        {
            return;
        }

        int planDelta = _collisionPlanRevision - _collisionTestBaselinePlan;
        int commitDelta = _collisionCommits - _collisionTestBaselineCommits;
        int createdDelta =
            _collisionPatchesCreated - _collisionTestBaselineCreated;
        int unloadedDelta =
            _collisionPatchesUnloaded - _collisionTestBaselineUnloaded;
        int fallbackDelta =
            _collisionFallbackActivations -
            _collisionTestBaselineFallbackActivations;
        bool passed =
            planDelta >= 3 &&
            commitDelta >= 3 &&
            createdDelta > 0 &&
            unloadedDelta > 0 &&
            fallbackDelta > 0 &&
            _collisionFinePatchCount > 0 &&
            _collisionPatches.Count == _targetCollisionLeaves.Count &&
            _collisionTestMaximumGroundGap <=
                CollisionTestAllowedGroundGapSeconds &&
            _planetaryPlayer.SeamTestState == PlanetarySeamTestState.Passed;

        string result =
            $"plans={planDelta}, commits={commitDelta}, " +
            $"created={createdDelta}, unloaded={unloadedDelta}, " +
            $"fallback={fallbackDelta}, active={_collisionPatches.Count}, " +
            $"L{GetMaximumLevel()}={_collisionFinePatchCount}, " +
            $"gap={_collisionTestMaximumGroundGap:F2} с, errors=0";
        FinishCollisionAcceptanceTest(
            passed
                ? CubeSphereCollisionTestState.Passed
                : CubeSphereCollisionTestState.Failed,
            passed ? result : "критерии не выполнены; " + result);
    }

    private void CancelCollisionAcceptanceTest()
    {
        if (!CollisionTestRunning)
        {
            return;
        }

        FinishCollisionAcceptanceTest(
            CubeSphereCollisionTestState.Cancelled,
            "остановлен пользователем");
    }

    private void FinishCollisionAcceptanceTest(
        CubeSphereCollisionTestState finalState,
        string result)
    {
        if (_planetaryPlayer is not null)
        {
            if (_planetaryPlayer.SeamTestRunning)
            {
                _planetaryPlayer.CancelSeamTraversalTest(true);
            }

            _planetaryPlayer.SetExternalMovementLocked(false);
        }

        _collisionTestState = finalState;
        _collisionTestResult = result;
        string label = finalState switch
        {
            CubeSphereCollisionTestState.Passed => "PASS",
            CubeSphereCollisionTestState.Failed => "FAIL",
            _ => "CANCELLED"
        };

        GD.Print(
            $"TASK-038 dynamic collision LOD acceptance {label}: " +
            $"plans={_collisionPlanRevision - _collisionTestBaselinePlan}; " +
            $"commits={_collisionCommits - _collisionTestBaselineCommits}; " +
            $"created={_collisionPatchesCreated - _collisionTestBaselineCreated}; " +
            $"unloaded={_collisionPatchesUnloaded - _collisionTestBaselineUnloaded}; " +
            $"fallbackActivations={_collisionFallbackActivations - _collisionTestBaselineFallbackActivations}; " +
            $"activeMin={(_collisionTestMinimumActive == int.MaxValue ? 0 : _collisionTestMinimumActive)}; " +
            $"activeMax={_collisionTestMaximumActive}; " +
            $"activeFinal={_collisionPatches.Count}; " +
            $"fine={_collisionFinePatchCount}; " +
            $"maxGroundGap={_collisionTestMaximumGroundGap:F3}s; " +
            $"errors={_collisionErrors - _collisionTestBaselineErrors}; " +
            $"fallbackFinal={_fallbackCollisionEnabled}; result={result}");
    }

    private void ClearDynamicCollisionLod()
    {
        _pendingCollisionBuilds.Clear();
        ClearStagedCollisionPatches();
        foreach (CollisionPatchRuntime runtime in _collisionPatches.Values)
        {
            runtime.CollisionShape.QueueFree();
        }

        _collisionPatches.Clear();
        _targetCollisionLeaves.Clear();
        _appliedCollisionLeaves.Clear();
        _collisionTransitionState = CubeSphereCollisionTransitionState.Idle;
        _collisionBasePatchCount = 0;
        _collisionMidPatchCount = 0;
        _collisionFinePatchCount = 0;
        _fallbackCollisionEnabled = true;
    }

    private void ClearStagedCollisionPatches()
    {
        foreach (CollisionPatchRuntime runtime in
            _stagedCollisionPatches.Values)
        {
            runtime.CollisionShape.QueueFree();
        }

        _stagedCollisionPatches.Clear();
    }
}
