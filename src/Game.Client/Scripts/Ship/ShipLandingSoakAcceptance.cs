using System;
using Godot;

public enum ShipLandingSoakState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class ShipFlightPrototype
{
    private enum LandingSoakPhase
    {
        None = 0,
        SearchAndAlign = 1,
        Descending = 2,
        LandedHold = 3
    }

    [Export(PropertyHint.Range, "10,500,1")]
    public int LandingSoakCycles { get; set; } = 100;

    [Export(PropertyHint.Range, "60.0,600.0,5.0")]
    public float LandingSoakTimeoutSeconds { get; set; } = 240.0f;

    [Export(PropertyHint.Range, "2.0,12.0,0.25")]
    public float LandingSoakCycleTimeoutSeconds { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "2.0,10.0,0.1")]
    public float LandingSoakStartClearance { get; set; } = 3.8f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float LandingSoakLandedHoldSeconds { get; set; } = 0.05f;

    [Export(PropertyHint.Range, "1.0,64.0,0.5")]
    public float LandingSoakMaximumManagedGrowthMiB { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "4.0,128.0,1.0")]
    public float LandingSoakMaximumPeakGrowthMiB { get; set; } = 32.0f;

    private ShipLandingSoakState _landingSoakState =
        ShipLandingSoakState.Ready;
    private LandingSoakPhase _landingSoakPhase = LandingSoakPhase.None;
    private ArcadeShipRuntimeState _landingSoakBaseline;
    private float _landingSoakElapsed;
    private float _landingSoakCycleElapsed;
    private float _landingSoakHoldElapsed;
    private int _landingSoakCyclesCompleted;
    private int _landingSoakAttemptBaseline;
    private int _landingSoakCompletionBaseline;
    private int _landingSoakLockBaseline;
    private int _landingSoakRecoveryBaseline;
    private int _landingSoakCollisionBaseline;
    private int _landingSoakErrorBaseline;
    private int _landingSoakAttempts;
    private int _landingSoakTouchdowns;
    private int _landingSoakLocks;
    private int _landingSoakRecoveries;
    private int _landingSoakCollisions;
    private int _landingSoakErrors;
    private int _landingSoakMinimumGearContacts;
    private float _landingSoakMaximumTouchdownSpeed;
    private float _landingSoakMaximumPositionError;
    private float _landingSoakMaximumAngularError;
    private long _landingSoakManagedBaselineBytes;
    private long _landingSoakManagedFinalBytes;
    private long _landingSoakManagedPeakBytes;
    private int _landingSoakNodeBaseline;
    private int _landingSoakNodeFinal;
    private bool _landingSoakMemoryBaselineCaptured;
    private string _landingSoakResult = "не запускался";

    public bool LandingSoakRunning =>
        _landingSoakState == ShipLandingSoakState.Running;

    public string LandingSoakStatusText
    {
        get
        {
            float memoryGrowthMiB = BytesToMiB(Math.Max(
                0L,
                _landingSoakManagedFinalBytes -
                    _landingSoakManagedBaselineBytes));
            return _landingSoakState switch
            {
                ShipLandingSoakState.Running =>
                    $"TASK-051 soak (V): RUNNING " +
                    $"{_landingSoakCyclesCompleted}/{LandingSoakCycles}, " +
                    $"phase={_landingSoakPhase}, " +
                    $"t={_landingSoakElapsed:F0} с, " +
                    $"vTouch={_landingSoakMaximumTouchdownSpeed:F2}",
                ShipLandingSoakState.Passed =>
                    $"TASK-051 soak (V): PASS " +
                    $"{_landingSoakCyclesCompleted}/{LandingSoakCycles}, " +
                    $"gear={_landingSoakMinimumGearContacts}, " +
                    $"vTouch={_landingSoakMaximumTouchdownSpeed:F2}, " +
                    $"memΔ={memoryGrowthMiB:F2} MiB, " +
                    $"nodesΔ={_landingSoakNodeFinal - _landingSoakNodeBaseline}",
                ShipLandingSoakState.Failed =>
                    $"TASK-051 soak (V): FAIL — {_landingSoakResult}",
                ShipLandingSoakState.Cancelled =>
                    "TASK-051 soak (V): остановлен пользователем",
                _ => "TASK-051 soak (V): READY"
            };
        }
    }

    private bool HandleLandingSoakInput(Key physical, Key logical)
    {
        if (physical != Key.V && logical != Key.V)
        {
            return false;
        }

        if (_testState == ShipFlightTestState.Running ||
            AtmosphereTestRunning || LandingTestRunning ||
            TouchdownTestRunning || _atmosphereDemoActive)
        {
            return true;
        }

        if (LandingSoakRunning)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Cancelled,
                "остановлен пользователем");
        }
        else
        {
            BeginLandingSoak();
        }

        return true;
    }

    private void InitializeLandingSoakPrototype()
    {
        GD.Print(
            "Prototype D landing soak ready. " +
            "Press V for 100 consecutive touchdown cycles.");
    }

    private void BeginLandingSoak()
    {
        if (_ship is null || _landingSite is null)
        {
            return;
        }

        if (_landingDemoActive)
        {
            _ship.CancelTouchdownSequence(false);
            _ship.CancelLandingAssist(false);
            _ship.RestoreRuntimeState(_landingDemoBaseline);
            _landingDemoActive = false;
        }

        _landingSoakBaseline = _ship.CaptureRuntimeState();
        _landingSoakAttemptBaseline = _ship.TouchdownAttempts;
        _landingSoakCompletionBaseline = _ship.TouchdownCompletions;
        _landingSoakLockBaseline = _ship.LandedLockCompletions;
        _landingSoakRecoveryBaseline = _ship.TouchdownRecoveries;
        _landingSoakCollisionBaseline = _ship.CollisionEvents;
        _landingSoakErrorBaseline = _ship.RuntimeErrorCount;
        _landingSoakElapsed = 0.0f;
        _landingSoakCycleElapsed = 0.0f;
        _landingSoakHoldElapsed = 0.0f;
        _landingSoakCyclesCompleted = 0;
        _landingSoakAttempts = 0;
        _landingSoakTouchdowns = 0;
        _landingSoakLocks = 0;
        _landingSoakRecoveries = 0;
        _landingSoakCollisions = 0;
        _landingSoakErrors = 0;
        _landingSoakMinimumGearContacts = int.MaxValue;
        _landingSoakMaximumTouchdownSpeed = 0.0f;
        _landingSoakMaximumPositionError = 0.0f;
        _landingSoakMaximumAngularError = 0.0f;
        _landingSoakManagedBaselineBytes = 0L;
        _landingSoakManagedFinalBytes = 0L;
        _landingSoakManagedPeakBytes = 0L;
        _landingSoakNodeBaseline = 0;
        _landingSoakNodeFinal = 0;
        _landingSoakMemoryBaselineCaptured = false;
        _landingSoakResult = "выполняется";
        _landingSoakState = ShipLandingSoakState.Running;

        PositionShipForLandingApproach();
        _ship.SetManualControlEnabled(false);
        _ship.SetAutoStabilization(true);
        _ship.SetCameraMode(ShipCameraMode.Chase, false);
        _ship.RequestLandingAssist(true);
        SetLandingSoakPhase(LandingSoakPhase.SearchAndAlign);

        GD.Print(
            "TASK-051 landing soak started: " +
            $"cycles={LandingSoakCycles}; " +
            $"startClearance={LandingSoakStartClearance:F1} m; " +
            $"timeout={LandingSoakTimeoutSeconds:F0} s");
    }

    private void UpdateLandingSoak(float deltaSeconds)
    {
        if (_ship is null)
        {
            return;
        }

        _landingSoakElapsed += deltaSeconds;
        _landingSoakCycleElapsed += deltaSeconds;
        CaptureLandingSoakRuntimeMetrics();

        if (_landingSoakElapsed > LandingSoakTimeoutSeconds)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"total timeout phase={_landingSoakPhase}, " +
                $"cycle={_landingSoakCyclesCompleted + 1}");
            return;
        }

        if (_landingSoakPhase != LandingSoakPhase.SearchAndAlign &&
            _landingSoakCycleElapsed > LandingSoakCycleTimeoutSeconds)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"cycle timeout phase={_landingSoakPhase}, " +
                $"cycle={_landingSoakCyclesCompleted + 1}");
            return;
        }

        if (_ship.RuntimeErrorCount > _landingSoakErrorBaseline)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                "runtime state error");
            return;
        }

        if (_ship.LandingState == ShipLandingAssistState.Failed ||
            _ship.TouchdownState == ShipTouchdownState.Failed)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                "landing/touchdown failure: " +
                _ship.LandingFailureReason + " " +
                _ship.TouchdownFailureReason);
            return;
        }

        switch (_landingSoakPhase)
        {
            case LandingSoakPhase.SearchAndAlign:
                if (_ship.LandingState == ShipLandingAssistState.Aligned)
                {
                    CaptureLandingSoakBaselineDiagnostics();
                    if (!_ship.PrepareTouchdownSoakCycle(
                        LandingSoakStartClearance))
                    {
                        FinishLandingSoak(
                            ShipLandingSoakState.Failed,
                            "first soak touchdown request rejected");
                        return;
                    }

                    SetLandingSoakPhase(LandingSoakPhase.Descending);
                }
                break;

            case LandingSoakPhase.Descending:
                if (_ship.TouchdownState == ShipTouchdownState.Landed)
                {
                    _landingSoakMinimumGearContacts = Math.Min(
                        _landingSoakMinimumGearContacts,
                        _ship.LandingGearContactCount);
                    _landingSoakMaximumPositionError = Math.Max(
                        _landingSoakMaximumPositionError,
                        _ship.TouchdownPositionError);
                    _landingSoakMaximumAngularError = Math.Max(
                        _landingSoakMaximumAngularError,
                        _ship.TouchdownAngularErrorDegrees);
                    _landingSoakHoldElapsed = 0.0f;
                    SetLandingSoakPhase(LandingSoakPhase.LandedHold);
                }
                break;

            case LandingSoakPhase.LandedHold:
                if (_ship.TouchdownState != ShipTouchdownState.Landed ||
                    !_ship.PhysicsLockedOnGear)
                {
                    FinishLandingSoak(
                        ShipLandingSoakState.Failed,
                        "landed physics lock lost");
                    return;
                }

                _landingSoakHoldElapsed += deltaSeconds;
                if (_landingSoakHoldElapsed < LandingSoakLandedHoldSeconds)
                {
                    return;
                }

                CompleteLandingSoakCycle();
                break;
        }
    }

    private void CompleteLandingSoakCycle()
    {
        if (_ship is null)
        {
            return;
        }

        _landingSoakCyclesCompleted++;
        CaptureLandingSoakRuntimeMetrics();
        SampleLandingSoakMemory();

        if (_landingSoakCyclesCompleted % 10 == 0 ||
            _landingSoakCyclesCompleted == LandingSoakCycles)
        {
            GD.Print(
                "TASK-051 landing soak progress: " +
                $"{_landingSoakCyclesCompleted}/{LandingSoakCycles}; " +
                $"vTouch={_landingSoakMaximumTouchdownSpeed:F3}; " +
                $"gearMin={_landingSoakMinimumGearContacts}; " +
                $"managed={BytesToMiB(_landingSoakManagedPeakBytes):F2} MiB");
        }

        if (_landingSoakCyclesCompleted >= Math.Max(1, LandingSoakCycles))
        {
            EvaluateLandingSoak();
            return;
        }

        if (!_ship.PrepareTouchdownSoakCycle(LandingSoakStartClearance))
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"cycle {_landingSoakCyclesCompleted + 1} request rejected");
            return;
        }

        SetLandingSoakPhase(LandingSoakPhase.Descending);
    }

    private void EvaluateLandingSoak()
    {
        if (_ship is null)
        {
            return;
        }

        CaptureLandingSoakRuntimeMetrics();
        CaptureLandingSoakFinalDiagnostics();
        int requiredCycles = Math.Max(1, LandingSoakCycles);
        float finalGrowthMiB = BytesToMiB(Math.Max(
            0L,
            _landingSoakManagedFinalBytes -
                _landingSoakManagedBaselineBytes));
        float peakGrowthMiB = BytesToMiB(Math.Max(
            0L,
            _landingSoakManagedPeakBytes -
                _landingSoakManagedBaselineBytes));
        int nodeDelta = _landingSoakNodeFinal - _landingSoakNodeBaseline;

        if (_landingSoakCyclesCompleted != requiredCycles ||
            _landingSoakAttempts != requiredCycles ||
            _landingSoakTouchdowns != requiredCycles ||
            _landingSoakLocks != requiredCycles)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                "counter mismatch cycles/attempts/touchdowns/locks=" +
                $"{_landingSoakCyclesCompleted}/{_landingSoakAttempts}/" +
                $"{_landingSoakTouchdowns}/{_landingSoakLocks}");
        }
        else if (_landingSoakMinimumGearContacts <
            _ship.LandingGearProbeCount)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"minimum gear contacts={_landingSoakMinimumGearContacts}/" +
                $"{_ship.LandingGearProbeCount}");
        }
        else if (_landingSoakMaximumTouchdownSpeed >
            TouchdownTestMaximumContactSpeed)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"touchdown speed={_landingSoakMaximumTouchdownSpeed:F3} m/s");
        }
        else if (_landingSoakMaximumPositionError >
            TouchdownTestMaximumPositionError)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"position error={_landingSoakMaximumPositionError:F3} m");
        }
        else if (_landingSoakMaximumAngularError >
            TouchdownTestMaximumAngularErrorDegrees)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"angular error={_landingSoakMaximumAngularError:F3}°");
        }
        else if (_landingSoakRecoveries > 0 ||
            _landingSoakCollisions > 0 ||
            _landingSoakErrors > 0)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                "recoveries/collisions/errors=" +
                $"{_landingSoakRecoveries}/" +
                $"{_landingSoakCollisions}/{_landingSoakErrors}");
        }
        else if (nodeDelta != 0)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"node leak delta={nodeDelta}");
        }
        else if (finalGrowthMiB > LandingSoakMaximumManagedGrowthMiB)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"managed growth={finalGrowthMiB:F2} MiB");
        }
        else if (peakGrowthMiB > LandingSoakMaximumPeakGrowthMiB)
        {
            FinishLandingSoak(
                ShipLandingSoakState.Failed,
                $"managed peak growth={peakGrowthMiB:F2} MiB");
        }
        else
        {
            FinishLandingSoak(
                ShipLandingSoakState.Passed,
                "100 consecutive physical touchdowns completed without " +
                "state, contact, node or managed-memory accumulation");
        }
    }

    private void CaptureLandingSoakRuntimeMetrics()
    {
        if (_ship is null)
        {
            return;
        }

        _landingSoakAttempts =
            _ship.TouchdownAttempts - _landingSoakAttemptBaseline;
        _landingSoakTouchdowns =
            _ship.TouchdownCompletions - _landingSoakCompletionBaseline;
        _landingSoakLocks =
            _ship.LandedLockCompletions - _landingSoakLockBaseline;
        _landingSoakRecoveries =
            _ship.TouchdownRecoveries - _landingSoakRecoveryBaseline;
        _landingSoakCollisions =
            _ship.CollisionEvents - _landingSoakCollisionBaseline;
        _landingSoakErrors =
            _ship.RuntimeErrorCount - _landingSoakErrorBaseline;
        _landingSoakMaximumTouchdownSpeed = Math.Max(
            _landingSoakMaximumTouchdownSpeed,
            _ship.TouchdownSpeed);
    }

    private void CaptureLandingSoakBaselineDiagnostics()
    {
        CollectManagedMemory();
        _landingSoakManagedBaselineBytes = GC.GetTotalMemory(false);
        _landingSoakManagedPeakBytes = _landingSoakManagedBaselineBytes;
        _landingSoakNodeBaseline = CountSceneNodes(GetTree().Root);
        _landingSoakMemoryBaselineCaptured = true;
    }

    private void CaptureLandingSoakFinalDiagnostics()
    {
        CollectManagedMemory();
        _landingSoakManagedFinalBytes = GC.GetTotalMemory(false);
        _landingSoakManagedPeakBytes = Math.Max(
            _landingSoakManagedPeakBytes,
            _landingSoakManagedFinalBytes);
        _landingSoakNodeFinal = CountSceneNodes(GetTree().Root);
    }

    private void SampleLandingSoakMemory()
    {
        if (!_landingSoakMemoryBaselineCaptured)
        {
            return;
        }

        _landingSoakManagedPeakBytes = Math.Max(
            _landingSoakManagedPeakBytes,
            GC.GetTotalMemory(false));
    }

    private void FinishLandingSoak(
        ShipLandingSoakState finalState,
        string result)
    {
        if (_ship is null)
        {
            return;
        }

        CaptureLandingSoakRuntimeMetrics();
        if (_landingSoakMemoryBaselineCaptured &&
            _landingSoakManagedFinalBytes == 0L)
        {
            CaptureLandingSoakFinalDiagnostics();
        }

        _landingSoakState = finalState;
        _landingSoakPhase = LandingSoakPhase.None;
        _landingSoakResult = result;
        _ship.CancelTouchdownSequence(false);
        _ship.CancelLandingAssist(false);
        _ship.RestoreRuntimeState(_landingSoakBaseline);
        _ship.SetManualControlEnabled(true);

        string status = finalState switch
        {
            ShipLandingSoakState.Passed => "PASS",
            ShipLandingSoakState.Failed => "FAIL",
            ShipLandingSoakState.Cancelled => "CANCELLED",
            _ => finalState.ToString().ToUpperInvariant()
        };
        float finalGrowthMiB = BytesToMiB(Math.Max(
            0L,
            _landingSoakManagedFinalBytes -
                _landingSoakManagedBaselineBytes));
        float peakGrowthMiB = BytesToMiB(Math.Max(
            0L,
            _landingSoakManagedPeakBytes -
                _landingSoakManagedBaselineBytes));

        GD.Print(
            $"TASK-051 100-landing soak {status}: " +
            $"cycles={_landingSoakCyclesCompleted}; " +
            $"attempts={_landingSoakAttempts}; " +
            $"touchdowns={_landingSoakTouchdowns}; " +
            $"locks={_landingSoakLocks}; " +
            $"gearMin={NormalizeMinimumGearContacts()}; " +
            $"touchdownSpeed={_landingSoakMaximumTouchdownSpeed:F3}; " +
            $"positionError={_landingSoakMaximumPositionError:F3}; " +
            $"angularError={_landingSoakMaximumAngularError:F3}; " +
            $"managedGrowthMiB={finalGrowthMiB:F3}; " +
            $"managedPeakGrowthMiB={peakGrowthMiB:F3}; " +
            $"nodeDelta={_landingSoakNodeFinal - _landingSoakNodeBaseline}; " +
            $"recoveries={_landingSoakRecoveries}; " +
            $"collisions={_landingSoakCollisions}; " +
            $"errors={_landingSoakErrors}; result={result}");
    }

    private void SetLandingSoakPhase(LandingSoakPhase phase)
    {
        _landingSoakPhase = phase;
        _landingSoakCycleElapsed = 0.0f;
    }

    private int NormalizeMinimumGearContacts()
    {
        return _landingSoakMinimumGearContacts == int.MaxValue
            ? 0
            : _landingSoakMinimumGearContacts;
    }

    private static void CollectManagedMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static int CountSceneNodes(Node node)
    {
        int count = 1;
        foreach (Node child in node.GetChildren())
        {
            count += CountSceneNodes(child);
        }

        return count;
    }

    private static float BytesToMiB(long bytes)
    {
        return bytes / (1024.0f * 1024.0f);
    }
}
