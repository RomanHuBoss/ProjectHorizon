using System;
using Godot;

public enum ShipLandingTestState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class ShipFlightPrototype
{
    [Export(PropertyHint.Range, "5.0,30.0,0.5")]
    public float LandingTestTimeoutSeconds { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "10.0,100.0,1.0")]
    public float LandingTestApproachAltitude { get; set; } = 48.0f;

    [Export(PropertyHint.Range, "0.1,3.0,0.05")]
    public float LandingTestMaximumPositionError { get; set; } = 0.75f;

    [Export(PropertyHint.Range, "0.1,10.0,0.1")]
    public float LandingTestMaximumAngularErrorDegrees { get; set; } = 2.5f;

    private ShipLandingTestState _landingTestState = ShipLandingTestState.Ready;
    private ArcadeShipRuntimeState _landingTestBaseline;
    private ArcadeShipRuntimeState _landingDemoBaseline;
    private Node3D? _landingMarker;
    private ShipLandingTestSite? _landingSite;
    private float _landingTestElapsed;
    private float _landingAlignedElapsed;
    private int _landingReservationBaseline;
    private int _landingAlignmentBaseline;
    private int _landingCollisionBaseline;
    private int _landingErrorBaseline;
    private int _landingCandidateChecks;
    private int _landingSurfaceHits;
    private int _landingSlopeRejections;
    private int _landingObstacleRejections;
    private int _landingReservations;
    private int _landingAlignments;
    private int _landingCollisions;
    private int _landingErrors;
    private float _landingFinalPositionError;
    private float _landingFinalAngularError;
    private float _landingReservedSlope;
    private float _landingReservedClearance;
    private string _landingTestResult = "не запускался";
    private bool _landingDemoActive;

    public bool LandingTestRunning =>
        _landingTestState == ShipLandingTestState.Running;

    public string LandingTestStatusText
    {
        get
        {
            return _landingTestState switch
            {
                ShipLandingTestState.Running =>
                    $"TASK-047 landing (N): RUNNING {_ship?.LandingState}, " +
                    $"t={_landingTestElapsed:F1} с, " +
                    $"checks={GetLandingCandidateDelta()}, " +
                    $"slopeReject={GetLandingSlopeRejectDelta()}, " +
                    $"obstacleReject={GetLandingObstacleRejectDelta()}",
                ShipLandingTestState.Passed =>
                    $"TASK-047 landing (N): PASS checks={_landingCandidateChecks}, " +
                    $"slopeReject={_landingSlopeRejections}, " +
                    $"obstacleReject={_landingObstacleRejections}, " +
                    $"slope={_landingReservedSlope:F1}°, " +
                    $"clear={FormatLandingClearance(_landingReservedClearance)}, " +
                    $"posErr={_landingFinalPositionError:F2} м, " +
                    $"angErr={_landingFinalAngularError:F2}°",
                ShipLandingTestState.Failed =>
                    $"TASK-047 landing (N): FAIL — {_landingTestResult}",
                ShipLandingTestState.Cancelled =>
                    "TASK-047 landing (N): остановлен пользователем",
                _ => "TASK-047 landing (N): READY"
            };
        }
    }

    private string LandingCompactStatus
    {
        get
        {
            if (_ship is null)
            {
                return "Посадка: недоступна";
            }

            string reservation = _ship.HasLandingReservation
                ? $"slope={_ship.LandingReservedSlopeDegrees:F1}°, " +
                    $"clear={FormatLandingClearance(_ship.LandingReservedClearance)}"
                : "точка не зарезервирована";
            return
                $"Посадка: {_ship.LandingState}/{_ship.TouchdownState}  •  " +
                $"{reservation}  •  gear={_ship.LandingGearDeployment:F2}  •  " +
                $"контакты={_ship.LandingGearContactCount}/" +
                $"{_ship.LandingGearProbeCount}  •  " +
                $"posErr={FormatLandingError(_ship.LandingPositionError)}  •  " +
                $"angErr={FormatLandingAngle(_ship.LandingAngularErrorDegrees)}";
        }
    }

    private string LandingDetailedStatus
    {
        get
        {
            if (_ship is null)
            {
                return "Landing system: unavailable";
            }

            return
                $"Landing state={_ship.LandingState}  •  " +
                $"reserved={_ship.HasLandingReservation}  •  " +
                $"candidate checks={_ship.LandingCandidateChecks}\n" +
                $"Surface hits={_ship.LandingSurfaceHits}  •  " +
                $"slope rejects={_ship.LandingSlopeRejections}  •  " +
                $"obstacle rejects={_ship.LandingObstacleRejections}\n" +
                $"Reservations={_ship.LandingReservations}  •  " +
                $"alignments={_ship.LandingAlignmentCompletions}  •  " +
                $"slope={_ship.LandingReservedSlopeDegrees:F2}°  •  " +
                $"clearance={FormatLandingClearance(_ship.LandingReservedClearance)}\n" +
                $"Position error={FormatLandingError(_ship.LandingPositionError)}  •  " +
                $"angular error={FormatLandingAngle(_ship.LandingAngularErrorDegrees)}  •  " +
                $"failure={_ship.LandingFailureReason}\n" +
                $"Touchdown state={_ship.TouchdownState}  •  " +
                $"gear={_ship.LandingGearDeployment:F2}  •  " +
                $"contacts={_ship.LandingGearContactCount}/" +
                $"{_ship.LandingGearProbeCount}  •  " +
                $"physics locked={_ship.PhysicsLockedOnGear}\n" +
                $"Touchdown clearance={_ship.TouchdownClearance:F2} m  •  " +
                $"speed={_ship.TouchdownSpeed:F2} m/s  •  " +
                $"attempts/completions={_ship.TouchdownAttempts}/" +
                $"{_ship.TouchdownCompletions}  •  " +
                $"takeoffs={_ship.TakeoffCompletions}";
        }
    }

    private void InitializeLandingPrototype()
    {
        _landingMarker = GetNodeOrNull<Node3D>("LandingTargetMarker");
        _landingSite = GetNodeOrNull<ShipLandingTestSite>(
            "AtmospherePlanet/LandingTestSite");

        if (_landingMarker is null || _landingSite is null)
        {
            throw new InvalidOperationException(
                "ShipFlightPrototype requires LandingTargetMarker and LandingTestSite.");
        }

        _landingMarker.Visible = false;
        GD.Print(
            "Prototype D landing-point system ready. " +
            "Press M for assist and N for acceptance test.");
    }

    private void UpdateLandingPrototype(float deltaSeconds)
    {
        _ = deltaSeconds;
        if (_ship is null || _landingMarker is null)
        {
            return;
        }

        if (_ship.HasLandingReservation)
        {
            Vector3 normal = _ship.LandingReservedNormal.Normalized();
            _landingMarker.Visible = true;
            _landingMarker.GlobalTransform = new Transform3D(
                CreateLandingMarkerBasis(normal),
                _ship.LandingReservedPoint + (normal * 0.18f));
        }
        else
        {
            _landingMarker.Visible = false;
        }
    }

    private bool HandleLandingInput(Key physical, Key logical)
    {
        if (physical == Key.N || logical == Key.N)
        {
            if (_testState == ShipFlightTestState.Running ||
                AtmosphereTestRunning || TouchdownTestRunning ||
                LandingSoakRunning)
            {
                return true;
            }

            if (LandingTestRunning)
            {
                FinishLandingTest(
                    ShipLandingTestState.Cancelled,
                    "остановлен пользователем");
            }
            else
            {
                BeginLandingTest();
            }

            return true;
        }

        if (physical == Key.M || logical == Key.M)
        {
            if (_testState == ShipFlightTestState.Running ||
                AtmosphereTestRunning || LandingTestRunning ||
                TouchdownTestRunning || LandingSoakRunning)
            {
                return true;
            }

            ToggleLandingDemo();
            return true;
        }

        return false;
    }

    private void ToggleLandingDemo()
    {
        if (_ship is null || _landingSite is null)
        {
            return;
        }

        if (!_landingDemoActive)
        {
            if (_ship.LandingAssistActive || _ship.TouchdownSequenceActive)
            {
                _ship.CancelTouchdownSequence(false);
                _ship.CancelLandingAssist(true);
                GD.Print("Landing sequence: cancelled.");
                return;
            }

            _landingDemoBaseline = _ship.CaptureRuntimeState();
            PositionShipForLandingApproach();
            _ship.SetManualControlEnabled(false);
            _ship.RequestLandingAssist(true);
            _landingDemoActive = true;
            _landingDemoTouchdownStarted = false;
            _landingDemoTakeoffStarted = false;
            GD.Print(
                "Landing demo stage 1/3: search and alignment started. " +
                "Press M again after Aligned for touchdown.");
            return;
        }

        if (_ship.LandingState == ShipLandingAssistState.Aligned &&
            _ship.TouchdownState == ShipTouchdownState.Idle &&
            !_landingDemoTouchdownStarted)
        {
            if (_ship.RequestTouchdown())
            {
                _landingDemoTouchdownStarted = true;
                GD.Print(
                    "Landing demo stage 2/3: touchdown started. " +
                    "Press M after LANDED for takeoff.");
            }
            return;
        }

        if (_ship.TouchdownState == ShipTouchdownState.Landed &&
            !_landingDemoTakeoffStarted)
        {
            _landingDemoTakeoffBaseline = _ship.TakeoffCompletions;
            if (_ship.RequestTakeoff())
            {
                _landingDemoTakeoffStarted = true;
                GD.Print("Landing demo stage 3/3: takeoff started.");
            }
            return;
        }

        _ship.CancelTouchdownSequence(false);
        _ship.CancelLandingAssist(false);
        _ship.RestoreRuntimeState(_landingDemoBaseline);
        _ship.SetManualControlEnabled(true);
        _landingDemoActive = false;
        _landingDemoTouchdownStarted = false;
        _landingDemoTakeoffStarted = false;
        GD.Print("Landing demo: baseline restored.");
    }

    private void BeginLandingTest()
    {
        if (_ship is null || _landingSite is null)
        {
            return;
        }

        if (_landingDemoActive)
        {
            _ship.CancelLandingAssist(false);
            _ship.RestoreRuntimeState(_landingDemoBaseline);
            _landingDemoActive = false;
        }

        _landingTestBaseline = _ship.CaptureRuntimeState();
        PositionShipForLandingApproach();
        _ship.SetManualControlEnabled(false);
        _ship.SetAutoStabilization(true);
        _ship.SetCameraMode(ShipCameraMode.Chase, false);

        _landingReservationBaseline = _ship.LandingReservations;
        _landingAlignmentBaseline = _ship.LandingAlignmentCompletions;
        _landingCollisionBaseline = _ship.CollisionEvents;
        _landingErrorBaseline = _ship.RuntimeErrorCount;
        _landingTestElapsed = 0.0f;
        _landingAlignedElapsed = 0.0f;
        _landingTestResult = "выполняется";
        _landingTestState = ShipLandingTestState.Running;
        _ship.RequestLandingAssist(true);

        GD.Print("TASK-047 landing-point acceptance started.");
    }

    private void UpdateLandingTest(float deltaSeconds)
    {
        if (_ship is null)
        {
            return;
        }

        _landingTestElapsed += deltaSeconds;
        if (_landingTestElapsed > LandingTestTimeoutSeconds)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"timeout state={_ship.LandingState}");
            return;
        }

        if (_ship.LandingState == ShipLandingAssistState.Failed)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                "search failed: " + _ship.LandingFailureReason);
            return;
        }

        if (_ship.RuntimeErrorCount > _landingErrorBaseline)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                "runtime state error");
            return;
        }

        if (_ship.LandingState == ShipLandingAssistState.Aligned)
        {
            _landingAlignedElapsed += deltaSeconds;
            if (_landingAlignedElapsed >= 0.75f)
            {
                EvaluateLandingTest();
            }
        }
        else
        {
            _landingAlignedElapsed = 0.0f;
        }
    }

    private void EvaluateLandingTest()
    {
        if (_ship is null)
        {
            return;
        }

        CaptureLandingTestMetrics();

        if (_landingCandidateChecks < 3 || _landingSurfaceHits < 3)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"insufficient probes={_landingCandidateChecks}/" +
                $"{_landingSurfaceHits}");
        }
        else if (_landingSlopeRejections < 1)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                "slope rejection not observed");
        }
        else if (_landingObstacleRejections < 1)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                "obstacle rejection not observed");
        }
        else if (_landingReservations < 1 || _landingAlignments < 1)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"reservation/alignment={_landingReservations}/" +
                $"{_landingAlignments}");
        }
        else if (_ship.LandingReservedSlopeDegrees >
            _ship.LandingMaximumSlopeDegrees)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"reserved slope={_ship.LandingReservedSlopeDegrees:F2}°");
        }
        else if (_ship.LandingReservedClearance <
            _ship.LandingObstacleClearance)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"clearance={_ship.LandingReservedClearance:F2} m");
        }
        else if (_landingFinalPositionError >
            LandingTestMaximumPositionError)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"position error={_landingFinalPositionError:F2} m");
        }
        else if (_landingFinalAngularError >
            LandingTestMaximumAngularErrorDegrees)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"angular error={_landingFinalAngularError:F2}°");
        }
        else if (_ship.Speed > 0.8f || _ship.AngularSpeedDegrees > 1.0f)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"not stable speed={_ship.Speed:F2}, " +
                $"angular={_ship.AngularSpeedDegrees:F2}");
        }
        else if (_landingCollisions > 0)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"unexpected collisions={_landingCollisions}");
        }
        else if (_landingErrors > 0)
        {
            FinishLandingTest(
                ShipLandingTestState.Failed,
                $"runtime errors={_landingErrors}");
        }
        else
        {
            FinishLandingTest(
                ShipLandingTestState.Passed,
                "поиск, резервирование и выравнивание подтверждены");
        }
    }

    private void PositionShipForLandingApproach()
    {
        if (_ship is null || _landingSite is null)
        {
            return;
        }

        Transform3D approach = _ship.CreateAtmosphericTransform(
            LandingTestApproachAltitude,
            _landingSite.GetTestApproachDirection(),
            Vector3.Forward);
        _ship.SetKinematicState(
            approach,
            Vector3.Zero,
            Vector3.Zero);
        _ship.ClearRadialGuidance();
    }

    private void CaptureLandingTestMetrics()
    {
        if (_ship is null)
        {
            return;
        }

        _landingCandidateChecks = _ship.LandingCandidateChecks;
        _landingSurfaceHits = _ship.LandingSurfaceHits;
        _landingSlopeRejections = _ship.LandingSlopeRejections;
        _landingObstacleRejections = _ship.LandingObstacleRejections;
        _landingReservations =
            _ship.LandingReservations - _landingReservationBaseline;
        _landingAlignments =
            _ship.LandingAlignmentCompletions - _landingAlignmentBaseline;
        _landingCollisions =
            _ship.CollisionEvents - _landingCollisionBaseline;
        _landingErrors =
            _ship.RuntimeErrorCount - _landingErrorBaseline;
        _landingFinalPositionError = _ship.LandingPositionError;
        _landingFinalAngularError = _ship.LandingAngularErrorDegrees;
        _landingReservedSlope = _ship.LandingReservedSlopeDegrees;
        _landingReservedClearance = _ship.LandingReservedClearance;
    }

    private void FinishLandingTest(
        ShipLandingTestState finalState,
        string result)
    {
        if (_ship is null)
        {
            return;
        }

        CaptureLandingTestMetrics();
        _landingTestState = finalState;
        _landingTestResult = result;
        _ship.CancelLandingAssist(false);
        _ship.RestoreRuntimeState(_landingTestBaseline);
        _ship.SetManualControlEnabled(true);

        string status = finalState switch
        {
            ShipLandingTestState.Passed => "PASS",
            ShipLandingTestState.Failed => "FAIL",
            ShipLandingTestState.Cancelled => "CANCELLED",
            _ => finalState.ToString().ToUpperInvariant()
        };

        GD.Print(
            $"TASK-047 landing-point acceptance {status}: " +
            $"checks={_landingCandidateChecks}; " +
            $"hits={_landingSurfaceHits}; " +
            $"slopeReject={_landingSlopeRejections}; " +
            $"obstacleReject={_landingObstacleRejections}; " +
            $"reservations={_landingReservations}; " +
            $"alignments={_landingAlignments}; " +
            $"slope={_landingReservedSlope:F2}; " +
            $"clearance={_landingReservedClearance:F2}; " +
            $"positionError={_landingFinalPositionError:F3}; " +
            $"angularError={_landingFinalAngularError:F3}; " +
            $"collisions={_landingCollisions}; errors={_landingErrors}; " +
            $"result={result}");
    }

    private int GetLandingCandidateDelta()
    {
        return _ship?.LandingCandidateChecks ?? 0;
    }

    private int GetLandingSlopeRejectDelta()
    {
        return _ship?.LandingSlopeRejections ?? 0;
    }

    private int GetLandingObstacleRejectDelta()
    {
        return _ship?.LandingObstacleRejections ?? 0;
    }

    private static Basis CreateLandingMarkerBasis(Vector3 radialUp)
    {
        Vector3 reference = Math.Abs(radialUp.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 forward = reference.Slide(radialUp).Normalized();
        Vector3 right = forward.Cross(radialUp).Normalized();
        Vector3 back = right.Cross(radialUp).Normalized();
        return new Basis(right, radialUp, back).Orthonormalized();
    }

    private static string FormatLandingClearance(float value)
    {
        return float.IsPositiveInfinity(value)
            ? "∞"
            : $"{value:F1} м";
    }

    private static string FormatLandingError(float value)
    {
        return float.IsPositiveInfinity(value)
            ? "—"
            : $"{value:F2} м";
    }

    private static string FormatLandingAngle(float value)
    {
        return float.IsPositiveInfinity(value)
            ? "—"
            : $"{value:F2}°";
    }
}
