using System;
using Godot;

public enum ShipAtmosphereTestState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class ShipFlightPrototype
{
    private enum AtmosphereTestPhase
    {
        None = 0,
        Entry = 1,
        MinimumSpeed = 2,
        Drag = 3,
        ClimbLimit = 4,
        SurfaceSafety = 5,
        Exit = 6
    }

    [Export(PropertyHint.Range, "6.0,30.0,0.5")]
    public float AtmosphereTestTimeoutSeconds { get; set; } = 16.0f;

    [Export(PropertyHint.Range, "0.1,1.0,0.05")]
    public float AtmosphereTestMinimumBlend { get; set; } = 0.55f;

    [Export(PropertyHint.Range, "0.5,30.0,0.5")]
    public float AtmosphereTestMinimumDragDrop { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.0,5.0,0.1")]
    public float AtmosphereTestClimbTolerance { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "5.0,40.0,0.5")]
    public float AtmosphereTestEntrySpeed { get; set; } = 18.0f;

    [Export(PropertyHint.Range, "5.0,150.0,1.0")]
    public float AtmosphereTestEntryGuidanceAcceleration { get; set; } = 60.0f;

    [Export(PropertyHint.Range, "2.0,10.0,0.25")]
    public float AtmosphereTestEntryPhaseTimeoutSeconds { get; set; } = 5.0f;

    private Node3D? _atmospherePlanet;
    private ShipAtmosphereTestState _atmosphereTestState =
        ShipAtmosphereTestState.Ready;
    private AtmosphereTestPhase _atmosphereTestPhase = AtmosphereTestPhase.None;
    private ArcadeShipRuntimeState _atmosphereTestBaseline;
    private float _atmosphereTestElapsed;
    private float _atmospherePhaseElapsed;
    private float _atmosphereMaximumBlend;
    private float _atmosphereMinimumAltitude;
    private float _atmosphereMaximumObservedClimb;
    private float _atmosphereDragStartSpeed;
    private float _atmosphereDragEndSpeed;
    private float _atmosphereEntryStartAltitude;
    private float _atmosphereEntryMinimumAltitude;
    private int _atmosphereEntryBaseline;
    private int _atmosphereExitBaseline;
    private int _dragBaseline;
    private int _minimumSpeedBaseline;
    private int _climbBaseline;
    private int _safetyBaseline;
    private int _recoveryBaseline;
    private int _atmosphereCollisionBaseline;
    private int _atmosphereErrorBaseline;
    private int _atmosphereEntries;
    private int _atmosphereExits;
    private int _atmosphereDragApplications;
    private int _atmosphereMinimumSpeedApplications;
    private int _atmosphereClimbApplications;
    private int _atmosphereSafetyApplications;
    private int _atmosphereRecoveries;
    private int _atmosphereCollisions;
    private int _atmosphereErrors;
    private string _atmosphereTestResult = "не запускался";
    private bool _atmosphereDemoActive;

    public bool AtmosphereTestRunning =>
        _atmosphereTestState == ShipAtmosphereTestState.Running;

    public string AtmosphereTestStatusText
    {
        get
        {
            return _atmosphereTestState switch
            {
                ShipAtmosphereTestState.Running =>
                    $"TASK-045 atmosphere (L): RUNNING {_atmosphereTestPhase}, " +
                    $"t={_atmosphereTestElapsed:F1} с, " +
                    $"alt={_ship?.AltitudeAboveSurface:F1} м, " +
                    $"radial={_ship?.RadialSpeed:F1} м/с",
                ShipAtmosphereTestState.Passed =>
                    $"TASK-045 atmosphere (L): PASS entry={_atmosphereEntries}, " +
                    $"exit={_atmosphereExits}, blend={_atmosphereMaximumBlend:F2}, " +
                    $"drag={_atmosphereDragStartSpeed - _atmosphereDragEndSpeed:F1}\n" +
                    $"minSpeed={_atmosphereMinimumSpeedApplications}, " +
                    $"climb={_atmosphereMaximumObservedClimb:F1}, " +
                    $"safety={_atmosphereSafetyApplications}, " +
                    $"minAlt={_atmosphereMinimumAltitude:F1}, " +
                    $"recoveries={_atmosphereRecoveries}, " +
                    $"collisions={_atmosphereCollisions}, errors={_atmosphereErrors}",
                ShipAtmosphereTestState.Failed =>
                    $"TASK-045 atmosphere (L): FAIL — {_atmosphereTestResult}",
                ShipAtmosphereTestState.Cancelled =>
                    "TASK-045 atmosphere (L): остановлен пользователем",
                _ => "TASK-045 atmosphere (L): READY"
            };
        }
    }

    private string AtmosphereCompactStatus
    {
        get
        {
            if (_ship is null)
            {
                return "Атмосфера: недоступна";
            }

            string mode = _ship.InAtmosphere ? "ATMOSPHERE" : "SPACE";
            string safety = _ship.SurfaceSafetyActive
                ? "SAFETY"
                : _ship.StallProtectionActive
                    ? "MIN-SPEED"
                    : "NORMAL";
            return
                $"Среда: {mode}  •  alt={_ship.AltitudeAboveSurface:F1} м  •  " +
                $"blend={_ship.AtmosphereBlend:F2}  •  radial={_ship.RadialSpeed:F1} м/с  •  " +
                $"{safety}";
        }
    }

    private string AtmosphereDetailedStatus
    {
        get
        {
            if (_ship is null)
            {
                return "Atmosphere reference: unavailable";
            }

            return
                $"Atmosphere: {(_ship.InAtmosphere ? "ACTIVE" : "SPACE")}  •  " +
                $"altitude={_ship.AltitudeAboveSurface:F2} м  •  " +
                $"blend={_ship.AtmosphereBlend:F3}\n" +
                $"Radial speed={_ship.RadialSpeed:F2} м/с  •  " +
                $"forward airspeed={_ship.ForwardAirSpeed:F2} м/с  •  " +
                $"stall assist={_ship.StallProtectionActive}  •  " +
                $"surface safety={_ship.SurfaceSafetyActive}\n" +
                $"Atmosphere counters: entry={_ship.AtmosphereEntryCount}, " +
                $"exit={_ship.AtmosphereExitCount}, drag={_ship.AtmosphereDragApplications}, " +
                $"minSpeed={_ship.MinimumSpeedAssistApplications}, " +
                $"climbLimit={_ship.ClimbLimitApplications}, " +
                $"safety={_ship.SurfaceSafetyApplications}, " +
                $"recoveries={_ship.SurfaceRecoveryCount}";
        }
    }

    private void InitializeAtmospherePrototype()
    {
        _atmospherePlanet = GetNodeOrNull<Node3D>("AtmospherePlanet");
        if (_ship is not null && _atmospherePlanet is not null)
        {
            _ship.SetAtmosphereBody(_atmospherePlanet);
        }

        GD.Print(
            "Prototype D atmospheric mode ready. " +
            "Press P for approach and L for acceptance test.");
    }

    private bool HandleAtmosphereInput(Key physical, Key logical)
    {
        if (physical == Key.L || logical == Key.L)
        {
            if (_testState == ShipFlightTestState.Running ||
                LandingTestRunning || TouchdownTestRunning ||
                (_ship?.LandingAssistActive ?? false) ||
                (_ship?.TouchdownSequenceActive ?? false))
            {
                return true;
            }

            if (AtmosphereTestRunning)
            {
                FinishAtmosphereTest(
                    ShipAtmosphereTestState.Cancelled,
                    "остановлен пользователем");
            }
            else
            {
                BeginAtmosphereTest();
            }

            return true;
        }

        if (physical == Key.P || logical == Key.P)
        {
            if (_testState == ShipFlightTestState.Running || AtmosphereTestRunning ||
                LandingTestRunning || TouchdownTestRunning ||
                (_ship?.LandingAssistActive ?? false) ||
                (_ship?.TouchdownSequenceActive ?? false))
            {
                return true;
            }

            ToggleAtmosphereApproach();
            return true;
        }

        return false;
    }

    private void ToggleAtmosphereApproach()
    {
        if (_ship is null || !_ship.HasAtmosphereReference)
        {
            return;
        }

        if (_atmosphereDemoActive)
        {
            _ship.ClearRadialGuidance();
            _ship.ResetToSpawn();
            _atmosphereDemoActive = false;
            GD.Print("Atmospheric approach: returned to space spawn.");
            return;
        }

        Transform3D approach = _ship.CreateAtmosphericTransform(
            _ship.AtmosphereHeight + 8.0f,
            Vector3.Up,
            Vector3.Forward);
        Vector3 forward = -approach.Basis.Z;
        Vector3 velocity =
            (forward * (_ship.AtmosphereMinimumForwardSpeed + 8.0f)) -
            (approach.Basis.Y * 12.0f);
        _ship.SetKinematicState(approach, velocity, Vector3.Zero);
        _ship.SetRadialGuidance(-12.0f, 45.0f);
        _ship.SetAutoStabilization(true);
        _atmosphereDemoActive = true;
        GD.Print("Atmospheric approach: positioned above entry boundary.");
    }

    private void BeginAtmosphereTest()
    {
        if (_ship is null || !_ship.HasAtmosphereReference ||
            LandingTestRunning || TouchdownTestRunning ||
            _ship.LandingAssistActive || _ship.TouchdownSequenceActive)
        {
            return;
        }

        _atmosphereTestBaseline = _ship.CaptureRuntimeState();
        _ship.ResetToSpawn();
        _ship.SetManualControlEnabled(false);
        _ship.SetAutoStabilization(true);
        _ship.SetCameraMode(ShipCameraMode.Chase, false);

        _dragBaseline = _ship.AtmosphereDragApplications;
        _minimumSpeedBaseline = _ship.MinimumSpeedAssistApplications;
        _climbBaseline = _ship.ClimbLimitApplications;
        _safetyBaseline = _ship.SurfaceSafetyApplications;
        _recoveryBaseline = _ship.SurfaceRecoveryCount;
        _atmosphereCollisionBaseline = _ship.CollisionEvents;
        _atmosphereErrorBaseline = _ship.RuntimeErrorCount;

        _atmosphereTestElapsed = 0.0f;
        _atmospherePhaseElapsed = 0.0f;
        _atmosphereMaximumBlend = 0.0f;
        _atmosphereMinimumAltitude = float.PositiveInfinity;
        _atmosphereMaximumObservedClimb = 0.0f;
        _atmosphereDragStartSpeed = 0.0f;
        _atmosphereDragEndSpeed = 0.0f;
        _atmosphereEntryStartAltitude = float.PositiveInfinity;
        _atmosphereEntryMinimumAltitude = float.PositiveInfinity;
        _atmosphereEntries = 0;
        _atmosphereExits = 0;
        _atmosphereDragApplications = 0;
        _atmosphereMinimumSpeedApplications = 0;
        _atmosphereClimbApplications = 0;
        _atmosphereSafetyApplications = 0;
        _atmosphereRecoveries = 0;
        _atmosphereCollisions = 0;
        _atmosphereErrors = 0;
        _atmosphereTestResult = "выполняется";
        _atmosphereTestPhase = AtmosphereTestPhase.Entry;
        _atmosphereTestState = ShipAtmosphereTestState.Running;
        _atmosphereDemoActive = false;

        Transform3D entryTransform = _ship.CreateAtmosphericTransform(
            _ship.AtmosphereHeight + 14.0f,
            Vector3.Up,
            Vector3.Forward);
        Vector3 entryForward = -entryTransform.Basis.Z;
        Vector3 entryVelocity =
            (entryForward * (_ship.AtmosphereMinimumForwardSpeed + 7.0f)) -
            (entryTransform.Basis.Y * 16.0f);
        _ship.SetKinematicState(entryTransform, entryVelocity, Vector3.Zero);
        _ship.SetRadialGuidance(
            -AtmosphereTestEntrySpeed,
            AtmosphereTestEntryGuidanceAcceleration);
        _atmosphereEntryStartAltitude = _ship.AltitudeAboveSurface;
        _atmosphereEntryMinimumAltitude = _ship.AltitudeAboveSurface;
        _atmosphereEntryBaseline = _ship.AtmosphereEntryCount;
        _atmosphereExitBaseline = _ship.AtmosphereExitCount;
        _ship.SetExternalCommand(new ShipControlCommand(
            0.35f, 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f,
            false, false));

        GD.Print("TASK-045 atmospheric flight acceptance started.");
    }

    private void UpdateAtmospherePrototype(float deltaSeconds)
    {
        if (_ship is null || !_atmosphereDemoActive || AtmosphereTestRunning)
        {
            return;
        }

        if (_ship.InAtmosphere && _ship.AtmosphereBlend >= 0.20f)
        {
            _ship.ClearRadialGuidance();
        }
        else if (_ship.AltitudeAboveSurface > _ship.AtmosphereHeight + 20.0f)
        {
            _ship.ClearRadialGuidance();
            _atmosphereDemoActive = false;
            GD.PushWarning(
                "Atmospheric approach aborted: ship moved away from entry boundary.");
        }
    }

    private void UpdateAtmosphereTest(float deltaSeconds)
    {
        if (_ship is null || !AtmosphereTestRunning)
        {
            return;
        }

        _atmosphereTestElapsed += deltaSeconds;
        _atmospherePhaseElapsed += deltaSeconds;
        _atmosphereMaximumBlend = Math.Max(
            _atmosphereMaximumBlend,
            _ship.AtmosphereBlend);
        if (_ship.AltitudeAboveSurface < float.PositiveInfinity)
        {
            _atmosphereMinimumAltitude = Math.Min(
                _atmosphereMinimumAltitude,
                _ship.AltitudeAboveSurface);
        }

        if (_atmosphereTestElapsed > AtmosphereTestTimeoutSeconds)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"timeout phase={_atmosphereTestPhase}");
            return;
        }

        if (_ship.RuntimeErrorCount > _atmosphereErrorBaseline)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                "runtime state error");
            return;
        }

        switch (_atmosphereTestPhase)
        {
            case AtmosphereTestPhase.Entry:
                _atmosphereEntryMinimumAltitude = Math.Min(
                    _atmosphereEntryMinimumAltitude,
                    _ship.AltitudeAboveSurface);

                if (_ship.InAtmosphere && _ship.AtmosphereBlend >= 0.20f)
                {
                    _ship.ClearRadialGuidance();
                    SetAtmosphereTestPhase(AtmosphereTestPhase.MinimumSpeed);
                    Transform3D minimumSpeedTransform =
                        _ship.CreateAtmosphericTransform(
                            _ship.AtmosphereHeight * 0.55f,
                            Vector3.Up,
                            Vector3.Forward);
                    Vector3 minimumForward = -minimumSpeedTransform.Basis.Z;
                    _ship.SetKinematicState(
                        minimumSpeedTransform,
                        minimumForward * 2.0f,
                        Vector3.Zero);
                    _ship.SetExternalCommand(ShipControlCommand.Neutral);
                }
                else if (_atmospherePhaseElapsed >
                    AtmosphereTestEntryPhaseTimeoutSeconds)
                {
                    FinishAtmosphereTest(
                        ShipAtmosphereTestState.Failed,
                        $"entry stalled startAlt={_atmosphereEntryStartAltitude:F1}, " +
                        $"minAlt={_atmosphereEntryMinimumAltitude:F1}, " +
                        $"alt={_ship.AltitudeAboveSurface:F1}, " +
                        $"radial={_ship.RadialSpeed:F1}, " +
                        $"blend={_ship.AtmosphereBlend:F2}");
                }
                break;

            case AtmosphereTestPhase.MinimumSpeed:
                if (_ship.MinimumSpeedAssistApplications > _minimumSpeedBaseline &&
                    _ship.ForwardAirSpeed >=
                        _ship.AtmosphereMinimumForwardSpeed * 0.60f)
                {
                    SetAtmosphereTestPhase(AtmosphereTestPhase.Drag);
                    Transform3D dragTransform = _ship.CreateAtmosphericTransform(
                        _ship.AtmosphereHeight * 0.45f,
                        Vector3.Up,
                        Vector3.Forward);
                    Vector3 dragForward = -dragTransform.Basis.Z;
                    float dragStartSpeed = Math.Min(50.0f, _ship.MaxSpeed);
                    _ship.SetKinematicState(
                        dragTransform,
                        dragForward * dragStartSpeed,
                        Vector3.Zero);
                    _atmosphereDragStartSpeed = dragStartSpeed;
                    _ship.SetExternalCommand(ShipControlCommand.Neutral);
                }
                else if (_atmospherePhaseElapsed > 2.5f)
                {
                    FinishAtmosphereTest(
                        ShipAtmosphereTestState.Failed,
                        $"minimum speed assist forward={_ship.ForwardAirSpeed:F1}");
                }
                break;

            case AtmosphereTestPhase.Drag:
                if (_atmospherePhaseElapsed >= 1.0f)
                {
                    _atmosphereDragEndSpeed = _ship.Speed;
                    SetAtmosphereTestPhase(AtmosphereTestPhase.ClimbLimit);
                    Transform3D climbTransform = _ship.CreateAtmosphericTransform(
                        35.0f,
                        Vector3.Up,
                        Vector3.Forward);
                    Vector3 climbForward = -climbTransform.Basis.Z;
                    Vector3 climbUp = climbTransform.Basis.Y;
                    _ship.SetKinematicState(
                        climbTransform,
                        (climbForward * 20.0f) +
                            (climbUp *
                                (_ship.AtmosphereMaximumClimbSpeed + 22.0f)),
                        Vector3.Zero);
                    _ship.SetExternalCommand(ShipControlCommand.Neutral);
                }
                break;

            case AtmosphereTestPhase.ClimbLimit:
                if (_atmospherePhaseElapsed >= 0.12f)
                {
                    _atmosphereMaximumObservedClimb = Math.Max(
                        _atmosphereMaximumObservedClimb,
                        Math.Max(0.0f, _ship.RadialSpeed));
                }

                if (_atmospherePhaseElapsed >= 0.55f)
                {
                    SetAtmosphereTestPhase(AtmosphereTestPhase.SurfaceSafety);
                    Transform3D safetyTransform = _ship.CreateAtmosphericTransform(
                        22.0f,
                        Vector3.Up,
                        Vector3.Forward);
                    Vector3 safetyForward = -safetyTransform.Basis.Z;
                    Vector3 safetyUp = safetyTransform.Basis.Y;
                    _ship.SetKinematicState(
                        safetyTransform,
                        (safetyForward * 18.0f) - (safetyUp * 30.0f),
                        Vector3.Zero);
                    _ship.SetExternalCommand(new ShipControlCommand(
                        0.2f, 0.0f, 0.0f,
                        0.0f, 0.0f, 0.0f,
                        false, false));
                }
                break;

            case AtmosphereTestPhase.SurfaceSafety:
                if (_atmospherePhaseElapsed >= 1.4f &&
                    _ship.SurfaceSafetyApplications > _safetyBaseline &&
                    _ship.RadialSpeed >= -1.0f)
                {
                    SetAtmosphereTestPhase(AtmosphereTestPhase.Exit);
                    Transform3D exitTransform = _ship.CreateAtmosphericTransform(
                        _ship.AtmosphereHeight + 16.0f,
                        Vector3.Up,
                        Vector3.Forward);
                    Vector3 exitForward = -exitTransform.Basis.Z;
                    _ship.SetKinematicState(
                        exitTransform,
                        exitForward * 20.0f,
                        Vector3.Zero);
                    _ship.SetExternalCommand(ShipControlCommand.Neutral);
                }
                else if (_atmospherePhaseElapsed > 3.0f)
                {
                    FinishAtmosphereTest(
                        ShipAtmosphereTestState.Failed,
                        $"surface safety alt={_ship.AltitudeAboveSurface:F1}, " +
                        $"radial={_ship.RadialSpeed:F1}");
                }
                break;

            case AtmosphereTestPhase.Exit:
                if (!_ship.InAtmosphere &&
                    _ship.AtmosphereExitCount > _atmosphereExitBaseline &&
                    _atmospherePhaseElapsed >= 0.35f)
                {
                    EvaluateAtmosphereTest();
                }
                break;
        }
    }

    private void SetAtmosphereTestPhase(AtmosphereTestPhase phase)
    {
        _atmosphereTestPhase = phase;
        _atmospherePhaseElapsed = 0.0f;
    }

    private void EvaluateAtmosphereTest()
    {
        if (_ship is null)
        {
            return;
        }

        CaptureAtmosphereTestCounters();
        float dragDrop = _atmosphereDragStartSpeed - _atmosphereDragEndSpeed;
        float minimumSafeAltitude = _ship.SurfaceSafetyClearance - 1.0f;

        if (_atmosphereEntries < 1)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                "atmosphere entry not detected");
        }
        else if (_atmosphereExits < 1)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                "atmosphere exit not detected");
        }
        else if (_atmosphereMaximumBlend < AtmosphereTestMinimumBlend)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"blend={_atmosphereMaximumBlend:F2}");
        }
        else if (_atmosphereMinimumSpeedApplications < 1)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                "minimum speed assist not applied");
        }
        else if (_atmosphereDragApplications < 1 ||
            dragDrop < AtmosphereTestMinimumDragDrop)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"drag drop={dragDrop:F1}");
        }
        else if (_atmosphereClimbApplications < 1 ||
            _atmosphereMaximumObservedClimb >
                _ship.AtmosphereMaximumClimbSpeed +
                AtmosphereTestClimbTolerance)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"climb={_atmosphereMaximumObservedClimb:F1}");
        }
        else if (_atmosphereSafetyApplications < 1 ||
            _atmosphereMinimumAltitude < minimumSafeAltitude)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"surface safety minAlt={_atmosphereMinimumAltitude:F1}");
        }
        else if (_atmosphereRecoveries > 0)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"hard recoveries={_atmosphereRecoveries}");
        }
        else if (_atmosphereCollisions > 0)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"surface collisions={_atmosphereCollisions}");
        }
        else if (_atmosphereErrors > 0)
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Failed,
                $"runtime errors={_atmosphereErrors}");
        }
        else
        {
            FinishAtmosphereTest(
                ShipAtmosphereTestState.Passed,
                "атмосферные ограничения подтверждены");
        }
    }

    private void CaptureAtmosphereTestCounters()
    {
        if (_ship is null)
        {
            return;
        }

        _atmosphereEntries =
            _ship.AtmosphereEntryCount - _atmosphereEntryBaseline;
        _atmosphereExits =
            _ship.AtmosphereExitCount - _atmosphereExitBaseline;
        _atmosphereDragApplications =
            _ship.AtmosphereDragApplications - _dragBaseline;
        _atmosphereMinimumSpeedApplications =
            _ship.MinimumSpeedAssistApplications - _minimumSpeedBaseline;
        _atmosphereClimbApplications =
            _ship.ClimbLimitApplications - _climbBaseline;
        _atmosphereSafetyApplications =
            _ship.SurfaceSafetyApplications - _safetyBaseline;
        _atmosphereRecoveries =
            _ship.SurfaceRecoveryCount - _recoveryBaseline;
        _atmosphereCollisions =
            _ship.CollisionEvents - _atmosphereCollisionBaseline;
        _atmosphereErrors =
            _ship.RuntimeErrorCount - _atmosphereErrorBaseline;
    }

    private void FinishAtmosphereTest(
        ShipAtmosphereTestState finalState,
        string result)
    {
        if (_ship is null)
        {
            return;
        }

        CaptureAtmosphereTestCounters();
        _atmosphereTestState = finalState;
        _atmosphereTestPhase = AtmosphereTestPhase.None;
        _atmosphereTestResult = result;
        _ship.ClearRadialGuidance();
        _ship.RestoreRuntimeState(_atmosphereTestBaseline);
        _ship.SetManualControlEnabled(true);
        _atmosphereDemoActive = false;

        string status = finalState switch
        {
            ShipAtmosphereTestState.Passed => "PASS",
            ShipAtmosphereTestState.Failed => "FAIL",
            ShipAtmosphereTestState.Cancelled => "CANCELLED",
            _ => finalState.ToString().ToUpperInvariant()
        };

        GD.Print(
            $"TASK-045 atmospheric flight acceptance {status}: " +
            $"entryStart={_atmosphereEntryStartAltitude:F2}; " +
            $"entryMin={_atmosphereEntryMinimumAltitude:F2}; " +
            $"entries={_atmosphereEntries}; exits={_atmosphereExits}; " +
            $"maxBlend={_atmosphereMaximumBlend:F3}; " +
            $"dragDrop={_atmosphereDragStartSpeed - _atmosphereDragEndSpeed:F2}; " +
            $"minSpeed={_atmosphereMinimumSpeedApplications}; " +
            $"climbLimit={_atmosphereClimbApplications}; " +
            $"maxClimb={_atmosphereMaximumObservedClimb:F2}; " +
            $"safety={_atmosphereSafetyApplications}; " +
            $"minAltitude={_atmosphereMinimumAltitude:F2}; " +
            $"recoveries={_atmosphereRecoveries}; " +
            $"collisions={_atmosphereCollisions}; errors={_atmosphereErrors}; " +
            $"result={result}");
    }
}
