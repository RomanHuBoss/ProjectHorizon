using System;
using Godot;

public enum ShipTouchdownTestState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class ShipFlightPrototype
{
    private enum TouchdownTestPhase
    {
        None = 0,
        SearchAndAlign = 1,
        Descending = 2,
        LandedHold = 3,
        TakingOff = 4
    }

    [Export(PropertyHint.Range, "10.0,60.0,0.5")]
    public float TouchdownTestTimeoutSeconds { get; set; } = 34.0f;

    [Export(PropertyHint.Range, "1,5,1")]
    public int TouchdownTestCycles { get; set; } = 2;

    [Export(PropertyHint.Range, "0.2,3.0,0.1")]
    public float TouchdownTestLandedHoldSeconds { get; set; } = 0.8f;

    [Export(PropertyHint.Range, "0.5,8.0,0.1")]
    public float TouchdownTestMaximumContactSpeed { get; set; } = 3.2f;

    [Export(PropertyHint.Range, "0.05,1.5,0.05")]
    public float TouchdownTestMaximumPositionError { get; set; } = 0.4f;

    [Export(PropertyHint.Range, "0.1,5.0,0.1")]
    public float TouchdownTestMaximumAngularErrorDegrees { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "2.0,30.0,0.5")]
    public float TouchdownTestMinimumTakeoffClearance { get; set; } = 10.0f;

    private ShipTouchdownTestState _touchdownTestState =
        ShipTouchdownTestState.Ready;
    private TouchdownTestPhase _touchdownTestPhase = TouchdownTestPhase.None;
    private ArcadeShipRuntimeState _touchdownTestBaseline;
    private float _touchdownTestElapsed;
    private float _touchdownLandedHoldElapsed;
    private int _touchdownCyclesCompleted;
    private int _touchdownAttemptBaseline;
    private int _touchdownCompletionBaseline;
    private int _landedLockBaseline;
    private int _takeoffCompletionBaseline;
    private int _touchdownRecoveryBaseline;
    private int _touchdownCollisionBaseline;
    private int _touchdownErrorBaseline;
    private int _testTouchdownAttempts;
    private int _testTouchdowns;
    private int _testLandedLocks;
    private int _testTakeoffs;
    private int _testRecoveries;
    private int _touchdownTestCollisions;
    private int _touchdownTestErrors;
    private int _maximumGearContacts;
    private float _maximumTouchdownSpeed;
    private float _maximumLandedPositionError;
    private float _maximumLandedAngularError;
    private float _minimumTakeoffClearance;
    private string _touchdownTestResult = "не запускался";

    private bool _landingDemoTouchdownStarted;
    private bool _landingDemoTakeoffStarted;
    private int _landingDemoTakeoffBaseline;

    public bool TouchdownTestRunning =>
        _touchdownTestState == ShipTouchdownTestState.Running;

    public string TouchdownTestStatusText
    {
        get
        {
            return _touchdownTestState switch
            {
                ShipTouchdownTestState.Running =>
                    $"TASK-049 touchdown (O): RUNNING {_touchdownTestPhase}, " +
                    $"cycle={_touchdownCyclesCompleted + 1}/{TouchdownTestCycles}, " +
                    $"t={_touchdownTestElapsed:F1} с, " +
                    $"gear={_ship?.LandingGearContactCount ?? 0}/" +
                    $"{_ship?.LandingGearProbeCount ?? 0}",
                ShipTouchdownTestState.Passed =>
                    $"TASK-049 touchdown (O): PASS cycles={_touchdownCyclesCompleted}, " +
                    $"touchdowns={_testTouchdowns}, takeoffs={_testTakeoffs}, " +
                    $"gear={_maximumGearContacts}, " +
                    $"vTouch={_maximumTouchdownSpeed:F2} м/с, " +
                    $"posErr={_maximumLandedPositionError:F2} м, " +
                    $"angErr={_maximumLandedAngularError:F2}°, " +
                    $"takeoff={_minimumTakeoffClearance:F1} м",
                ShipTouchdownTestState.Failed =>
                    $"TASK-049 touchdown (O): FAIL — {_touchdownTestResult}",
                ShipTouchdownTestState.Cancelled =>
                    "TASK-049 touchdown (O): остановлен пользователем",
                _ => "TASK-049 touchdown (O): READY"
            };
        }
    }

    private bool HandleTouchdownInput(Key physical, Key logical)
    {
        if (physical != Key.O && logical != Key.O)
        {
            return false;
        }

        if (_testState == ShipFlightTestState.Running ||
            AtmosphereTestRunning || LandingTestRunning ||
            LandingSoakRunning)
        {
            return true;
        }

        if (TouchdownTestRunning)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Cancelled,
                "остановлен пользователем");
        }
        else
        {
            BeginTouchdownTest();
        }

        return true;
    }

    private void InitializeTouchdownPrototype()
    {
        GD.Print(
            "Prototype D touchdown system ready. " +
            "Press M for manual stages and O for acceptance test.");
    }

    private void UpdateTouchdownPrototype(float deltaSeconds)
    {
        _ = deltaSeconds;
        if (_ship is null || !_landingDemoActive)
        {
            return;
        }

        if (_landingDemoTakeoffStarted &&
            _ship.TakeoffCompletions > _landingDemoTakeoffBaseline)
        {
            _landingDemoActive = false;
            _landingDemoTouchdownStarted = false;
            _landingDemoTakeoffStarted = false;
            GD.Print("Landing demo cycle completed; manual control restored.");
        }
    }

    private void BeginTouchdownTest()
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

        _touchdownTestBaseline = _ship.CaptureRuntimeState();
        _touchdownAttemptBaseline = _ship.TouchdownAttempts;
        _touchdownCompletionBaseline = _ship.TouchdownCompletions;
        _landedLockBaseline = _ship.LandedLockCompletions;
        _takeoffCompletionBaseline = _ship.TakeoffCompletions;
        _touchdownRecoveryBaseline = _ship.TouchdownRecoveries;
        _touchdownCollisionBaseline = _ship.CollisionEvents;
        _touchdownErrorBaseline = _ship.RuntimeErrorCount;
        _touchdownTestElapsed = 0.0f;
        _touchdownLandedHoldElapsed = 0.0f;
        _touchdownCyclesCompleted = 0;
        _testTouchdownAttempts = 0;
        _testTouchdowns = 0;
        _testLandedLocks = 0;
        _testTakeoffs = 0;
        _testRecoveries = 0;
        _touchdownTestCollisions = 0;
        _touchdownTestErrors = 0;
        _maximumGearContacts = 0;
        _maximumTouchdownSpeed = 0.0f;
        _maximumLandedPositionError = 0.0f;
        _maximumLandedAngularError = 0.0f;
        _minimumTakeoffClearance = float.PositiveInfinity;
        _touchdownTestResult = "выполняется";
        _touchdownTestState = ShipTouchdownTestState.Running;
        BeginAutomatedTouchdownCycle();
        GD.Print(
            "TASK-049 touchdown/takeoff acceptance started: " +
            $"cycles={TouchdownTestCycles}");
    }

    private void BeginAutomatedTouchdownCycle()
    {
        if (_ship is null)
        {
            return;
        }

        PositionShipForLandingApproach();
        _ship.SetManualControlEnabled(false);
        _ship.SetAutoStabilization(true);
        _ship.SetCameraMode(ShipCameraMode.Chase, false);
        _ship.RequestLandingAssist(true);
        SetTouchdownTestPhase(TouchdownTestPhase.SearchAndAlign);
    }

    private void UpdateTouchdownTest(float deltaSeconds)
    {
        if (_ship is null)
        {
            return;
        }

        _touchdownTestElapsed += deltaSeconds;
        _maximumGearContacts = Math.Max(
            _maximumGearContacts,
            _ship.LandingGearContactCount);
        _maximumTouchdownSpeed = Math.Max(
            _maximumTouchdownSpeed,
            _ship.TouchdownSpeed);

        if (_touchdownTestElapsed > TouchdownTestTimeoutSeconds)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"timeout phase={_touchdownTestPhase}, " +
                $"cycle={_touchdownCyclesCompleted + 1}");
            return;
        }

        if (_ship.RuntimeErrorCount > _touchdownErrorBaseline)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                "runtime state error");
            return;
        }

        if (_ship.LandingState == ShipLandingAssistState.Failed ||
            _ship.TouchdownState == ShipTouchdownState.Failed)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                "landing/touchdown failure: " +
                _ship.LandingFailureReason + " " +
                _ship.TouchdownFailureReason);
            return;
        }

        switch (_touchdownTestPhase)
        {
            case TouchdownTestPhase.SearchAndAlign:
                if (_ship.LandingState == ShipLandingAssistState.Aligned)
                {
                    if (!_ship.RequestTouchdown())
                    {
                        FinishTouchdownTest(
                            ShipTouchdownTestState.Failed,
                            "touchdown request rejected");
                        return;
                    }

                    SetTouchdownTestPhase(TouchdownTestPhase.Descending);
                }
                break;

            case TouchdownTestPhase.Descending:
                if (_ship.TouchdownState == ShipTouchdownState.Landed)
                {
                    _maximumLandedPositionError = Math.Max(
                        _maximumLandedPositionError,
                        _ship.TouchdownPositionError);
                    _maximumLandedAngularError = Math.Max(
                        _maximumLandedAngularError,
                        _ship.TouchdownAngularErrorDegrees);
                    _touchdownLandedHoldElapsed = 0.0f;
                    SetTouchdownTestPhase(TouchdownTestPhase.LandedHold);
                }
                break;

            case TouchdownTestPhase.LandedHold:
                if (_ship.TouchdownState != ShipTouchdownState.Landed ||
                    !_ship.PhysicsLockedOnGear)
                {
                    FinishTouchdownTest(
                        ShipTouchdownTestState.Failed,
                        "landed physics lock lost");
                    return;
                }

                _touchdownLandedHoldElapsed += deltaSeconds;
                if (_touchdownLandedHoldElapsed >=
                    TouchdownTestLandedHoldSeconds)
                {
                    if (!_ship.RequestTakeoff())
                    {
                        FinishTouchdownTest(
                            ShipTouchdownTestState.Failed,
                            "takeoff request rejected");
                        return;
                    }

                    SetTouchdownTestPhase(TouchdownTestPhase.TakingOff);
                }
                break;

            case TouchdownTestPhase.TakingOff:
                if (_ship.TouchdownState == ShipTouchdownState.Idle &&
                    _ship.TakeoffCompletions >
                        _takeoffCompletionBaseline + _touchdownCyclesCompleted)
                {
                    _minimumTakeoffClearance = Math.Min(
                        _minimumTakeoffClearance,
                        _ship.LastTakeoffClearance);
                    _touchdownCyclesCompleted++;
                    if (_touchdownCyclesCompleted >= TouchdownTestCycles)
                    {
                        EvaluateTouchdownTest();
                    }
                    else
                    {
                        BeginAutomatedTouchdownCycle();
                    }
                }
                break;
        }
    }

    private void EvaluateTouchdownTest()
    {
        if (_ship is null)
        {
            return;
        }

        CaptureTouchdownTestMetrics();
        int requiredCycles = Math.Max(1, TouchdownTestCycles);
        if (_touchdownCyclesCompleted < requiredCycles ||
            _testTouchdownAttempts < requiredCycles ||
            _testTouchdowns < requiredCycles ||
            _testLandedLocks < requiredCycles ||
            _testTakeoffs < requiredCycles)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"incomplete cycles={_touchdownCyclesCompleted}, " +
                $"attempts/touchdowns/locks/takeoffs=" +
                $"{_testTouchdownAttempts}/{_testTouchdowns}/" +
                $"{_testLandedLocks}/{_testTakeoffs}");
        }
        else if (_maximumGearContacts < _ship.LandingGearProbeCount)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"gear contacts={_maximumGearContacts}/" +
                $"{_ship.LandingGearProbeCount}");
        }
        else if (_maximumTouchdownSpeed > TouchdownTestMaximumContactSpeed)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"touchdown speed={_maximumTouchdownSpeed:F2} m/s");
        }
        else if (_maximumLandedPositionError >
            TouchdownTestMaximumPositionError)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"landed position error=" +
                $"{_maximumLandedPositionError:F3} m");
        }
        else if (_maximumLandedAngularError >
            TouchdownTestMaximumAngularErrorDegrees)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"landed angular error=" +
                $"{_maximumLandedAngularError:F3}°");
        }
        else if (_minimumTakeoffClearance <
            TouchdownTestMinimumTakeoffClearance)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"takeoff clearance={_minimumTakeoffClearance:F2} m");
        }
        else if (_testRecoveries > 0 || _touchdownTestCollisions > 0 ||
            _touchdownTestErrors > 0)
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Failed,
                $"recoveries/collisions/errors=" +
                $"{_testRecoveries}/{_touchdownTestCollisions}/{_touchdownTestErrors}");
        }
        else
        {
            FinishTouchdownTest(
                ShipTouchdownTestState.Passed,
                "touchdown, landed lock, takeoff and repeat cycle confirmed");
        }
    }

    private void CaptureTouchdownTestMetrics()
    {
        if (_ship is null)
        {
            return;
        }

        _testTouchdownAttempts =
            _ship.TouchdownAttempts - _touchdownAttemptBaseline;
        _testTouchdowns =
            _ship.TouchdownCompletions - _touchdownCompletionBaseline;
        _testLandedLocks =
            _ship.LandedLockCompletions - _landedLockBaseline;
        _testTakeoffs =
            _ship.TakeoffCompletions - _takeoffCompletionBaseline;
        _testRecoveries =
            _ship.TouchdownRecoveries - _touchdownRecoveryBaseline;
        _touchdownTestCollisions =
            _ship.CollisionEvents - _touchdownCollisionBaseline;
        _touchdownTestErrors =
            _ship.RuntimeErrorCount - _touchdownErrorBaseline;
    }

    private void FinishTouchdownTest(
        ShipTouchdownTestState finalState,
        string result)
    {
        if (_ship is null)
        {
            return;
        }

        CaptureTouchdownTestMetrics();
        _touchdownTestState = finalState;
        _touchdownTestPhase = TouchdownTestPhase.None;
        _touchdownTestResult = result;
        _ship.CancelTouchdownSequence(false);
        _ship.CancelLandingAssist(false);
        _ship.RestoreRuntimeState(_touchdownTestBaseline);
        _ship.SetManualControlEnabled(true);

        string status = finalState switch
        {
            ShipTouchdownTestState.Passed => "PASS",
            ShipTouchdownTestState.Failed => "FAIL",
            ShipTouchdownTestState.Cancelled => "CANCELLED",
            _ => finalState.ToString().ToUpperInvariant()
        };

        GD.Print(
            $"TASK-049 touchdown/takeoff acceptance {status}: " +
            $"cycles={_touchdownCyclesCompleted}; " +
            $"attempts={_testTouchdownAttempts}; " +
            $"touchdowns={_testTouchdowns}; locks={_testLandedLocks}; " +
            $"takeoffs={_testTakeoffs}; gear={_maximumGearContacts}; " +
            $"touchdownSpeed={_maximumTouchdownSpeed:F3}; " +
            $"positionError={_maximumLandedPositionError:F3}; " +
            $"angularError={_maximumLandedAngularError:F3}; " +
            $"takeoffClearance={_minimumTakeoffClearance:F2}; " +
            $"recoveries={_testRecoveries}; " +
            $"collisions={_touchdownTestCollisions}; errors={_touchdownTestErrors}; " +
            $"result={result}");
    }

    private void SetTouchdownTestPhase(TouchdownTestPhase phase)
    {
        _touchdownTestPhase = phase;
    }
}
