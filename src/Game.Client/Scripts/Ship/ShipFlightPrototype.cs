using System;
using Godot;

public enum ShipFlightTestState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum ShipPrototypeHudMode
{
    Compact = 0,
    Detailed = 1,
    Hidden = 2
}

public partial class ShipFlightPrototype : Node3D
{
    private enum FlightTestPhase
    {
        None = 0,
        Settle = 1,
        ForwardThrust = 2,
        Rotation = 3,
        LateralThrusters = 4,
        Boost = 5,
        BrakeAndStabilize = 6,
        CameraSwitch = 7
    }

    [Export(PropertyHint.Range, "5.0,30.0,0.5")]
    public float TestTimeoutSeconds { get; set; } = 15.0f;

    [Export(PropertyHint.Range, "10.0,100.0,1.0")]
    public float TestMinimumSpeed { get; set; } = 35.0f;

    [Export(PropertyHint.Range, "10.0,200.0,1.0")]
    public float TestMinimumTravelDistance { get; set; } = 45.0f;

    [Export(PropertyHint.Range, "0.5,20.0,0.1")]
    public float TestMinimumLateralSpeed { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.5,20.0,0.1")]
    public float TestMinimumVerticalSpeed { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "5.0,180.0,1.0")]
    public float TestMinimumAngularSpeedDegrees { get; set; } = 45.0f;

    [Export(PropertyHint.Range, "0.1,5.0,0.1")]
    public float TestMaximumFinalSpeed { get; set; } = 0.8f;

    [Export(PropertyHint.Range, "0.1,10.0,0.1")]
    public float TestMaximumFinalAngularSpeedDegrees { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "360.0,1000.0,10.0")]
    public float HudCompactWidth { get; set; } = 620.0f;

    [Export(PropertyHint.Range, "120.0,400.0,10.0")]
    public float HudCompactHeight { get; set; } = 210.0f;

    [Export(PropertyHint.Range, "500.0,1200.0,10.0")]
    public float HudDetailedWidth { get; set; } = 760.0f;

    [Export(PropertyHint.Range, "300.0,900.0,10.0")]
    public float HudDetailedHeight { get; set; } = 520.0f;

    private ArcadeShipController? _ship;
    private MarginContainer? _compactMargin;
    private Label? _compactLabel;
    private MarginContainer? _detailedMargin;
    private Label? _detailedLabel;
    private PanelContainer? _hiddenHint;
    private ShipPrototypeHudMode _hudMode = ShipPrototypeHudMode.Compact;

    private ShipFlightTestState _testState = ShipFlightTestState.Ready;
    private FlightTestPhase _testPhase = FlightTestPhase.None;
    private ArcadeShipRuntimeState _testBaseline;
    private Vector3 _testStartPosition;
    private float _testElapsed;
    private float _phaseElapsed;
    private float _maximumSpeed;
    private float _maximumAngularSpeed;
    private float _maximumLateralSpeed;
    private float _maximumVerticalSpeed;
    private float _maximumDistance;
    private float _finalSpeed;
    private float _finalAngularSpeed;
    private int _testCameraSwitches;
    private int _testCollisions;
    private int _testErrors;
    private int _cameraSwitchBaseline;
    private int _collisionBaseline;
    private int _runtimeErrorBaseline;
    private string _testResult = "не запускался";

    public string FlightTestStatusText
    {
        get
        {
            return _testState switch
            {
                ShipFlightTestState.Running =>
                    $"TASK-043 flight (J): RUNNING {_testPhase}, " +
                    $"t={_testElapsed:F1} с, vmax={_maximumSpeed:F1} м/с",
                ShipFlightTestState.Passed =>
                    $"TASK-043 flight (J): PASS vmax={_maximumSpeed:F1}, " +
                    $"distance={_maximumDistance:F1}, lateral={_maximumLateralSpeed:F1}, " +
                    $"vertical={_maximumVerticalSpeed:F1}, angular={_maximumAngularSpeed:F1}°/с, " +
                    $"final={_finalSpeed:F2}/{_finalAngularSpeed:F2}",
                ShipFlightTestState.Failed =>
                    $"TASK-043 flight (J): FAIL — {_testResult}",
                ShipFlightTestState.Cancelled =>
                    "TASK-043 flight (J): остановлен пользователем",
                _ => "TASK-043 flight (J): READY"
            };
        }
    }

    public override void _Ready()
    {
        _ship = GetNodeOrNull<ArcadeShipController>("ArcadeShip");
        _compactMargin = GetNodeOrNull<MarginContainer>("Hud/CompactMargin");
        _compactLabel = GetNodeOrNull<Label>(
            "Hud/CompactMargin/PanelContainer/Label");
        _detailedMargin = GetNodeOrNull<MarginContainer>("Hud/DetailedMargin");
        _detailedLabel = GetNodeOrNull<Label>(
            "Hud/DetailedMargin/PanelContainer/ScrollContainer/Label");
        _hiddenHint = GetNodeOrNull<PanelContainer>("Hud/HiddenHint");

        if (_ship is null || _compactMargin is null || _compactLabel is null ||
            _detailedMargin is null || _detailedLabel is null ||
            _hiddenHint is null)
        {
            throw new InvalidOperationException(
                "ShipFlightPrototype scene is missing ship or HUD nodes.");
        }

        GetViewport().SizeChanged += UpdateHudLayout;
        InitializeAtmospherePrototype();
        InitializeLandingPrototype();
        InitializeTouchdownPrototype();
        InitializeLandingSoakPrototype();
        ApplyHudMode();
        UpdateHud();
        GD.Print("Prototype D flight foundation ready. Press J for acceptance test.");
    }

    public override void _ExitTree()
    {
        if (GetViewport() is Viewport viewport)
        {
            viewport.SizeChanged -= UpdateHudLayout;
        }
    }

    public override void _Process(double delta)
    {
        UpdateAtmospherePrototype((float)delta);
        UpdateLandingPrototype((float)delta);
        UpdateTouchdownPrototype((float)delta);

        if (LandingSoakRunning)
        {
            UpdateLandingSoak((float)delta);
        }

        if (_testState == ShipFlightTestState.Running)
        {
            UpdateFlightTest((float)delta);
        }

        if (AtmosphereTestRunning)
        {
            UpdateAtmosphereTest((float)delta);
        }

        if (LandingTestRunning)
        {
            UpdateLandingTest((float)delta);
        }

        if (TouchdownTestRunning)
        {
            UpdateTouchdownTest((float)delta);
        }

        UpdateHud();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo)
        {
            return;
        }

        Key physical = keyEvent.PhysicalKeycode;
        Key logical = keyEvent.Keycode;

        if (physical == Key.H || logical == Key.H)
        {
            _hudMode = (ShipPrototypeHudMode)(((int)_hudMode + 1) % 3);
            ApplyHudMode();
            GD.Print($"Ship HUD mode: {_hudMode}");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (HandleLandingSoakInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (HandleTouchdownInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (HandleAtmosphereInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (HandleLandingInput(physical, logical))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (physical == Key.J || logical == Key.J)
        {
            if (AtmosphereTestRunning || LandingTestRunning ||
                TouchdownTestRunning || LandingSoakRunning ||
                (_ship?.LandingAssistActive ?? false) ||
                (_ship?.TouchdownSequenceActive ?? false))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            if (_testState == ShipFlightTestState.Running)
            {
                FinishFlightTest(
                    ShipFlightTestState.Cancelled,
                    "остановлен пользователем");
            }
            else
            {
                BeginFlightTest();
            }

            GetViewport().SetInputAsHandled();
        }
    }

    private void BeginFlightTest()
    {
        if (_ship is null || AtmosphereTestRunning || LandingTestRunning ||
            TouchdownTestRunning || LandingSoakRunning ||
            _ship.LandingAssistActive ||
            _ship.TouchdownSequenceActive)
        {
            return;
        }

        _testBaseline = _ship.CaptureRuntimeState();
        _ship.ResetToSpawn();
        _ship.SetManualControlEnabled(false);
        _ship.SetAutoStabilization(true);
        _ship.SetCameraMode(ShipCameraMode.Chase, false);
        _ship.SetExternalCommand(ShipControlCommand.Neutral);

        _testStartPosition = _ship.GlobalPosition;
        _testElapsed = 0.0f;
        _phaseElapsed = 0.0f;
        _maximumSpeed = 0.0f;
        _maximumAngularSpeed = 0.0f;
        _maximumLateralSpeed = 0.0f;
        _maximumVerticalSpeed = 0.0f;
        _maximumDistance = 0.0f;
        _finalSpeed = 0.0f;
        _finalAngularSpeed = 0.0f;
        _testCameraSwitches = 0;
        _testCollisions = 0;
        _testErrors = 0;
        _cameraSwitchBaseline = _ship.CameraSwitchCount;
        _collisionBaseline = _ship.CollisionEvents;
        _runtimeErrorBaseline = _ship.RuntimeErrorCount;
        _testResult = "выполняется";
        _testPhase = FlightTestPhase.Settle;
        _testState = ShipFlightTestState.Running;

        GD.Print("TASK-043 arcade flight acceptance started.");
    }

    private void UpdateFlightTest(float deltaSeconds)
    {
        if (_ship is null)
        {
            return;
        }

        _testElapsed += deltaSeconds;
        _phaseElapsed += deltaSeconds;
        UpdateTestMetrics();

        if (_testElapsed > TestTimeoutSeconds)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"timeout phase={_testPhase}");
            return;
        }

        if (_ship.RuntimeErrorCount > _runtimeErrorBaseline)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                "runtime state error");
            return;
        }

        switch (_testPhase)
        {
            case FlightTestPhase.Settle:
                _ship.SetExternalCommand(ShipControlCommand.Neutral);
                if (_phaseElapsed >= 0.45f)
                {
                    SetTestPhase(
                        FlightTestPhase.ForwardThrust,
                        new ShipControlCommand(
                            1.0f, 0.0f, 0.0f,
                            0.0f, 0.0f, 0.0f,
                            false, false));
                }
                break;

            case FlightTestPhase.ForwardThrust:
                if (_phaseElapsed >= 1.5f)
                {
                    SetTestPhase(
                        FlightTestPhase.Rotation,
                        new ShipControlCommand(
                            0.65f, 0.0f, 0.0f,
                            0.55f, 0.75f, 0.65f,
                            false, false));
                }
                break;

            case FlightTestPhase.Rotation:
                if (_phaseElapsed >= 1.5f)
                {
                    SetTestPhase(
                        FlightTestPhase.LateralThrusters,
                        new ShipControlCommand(
                            0.25f, 1.0f, 0.8f,
                            0.0f, 0.0f, 0.0f,
                            false, false));
                }
                break;

            case FlightTestPhase.LateralThrusters:
                if (_phaseElapsed >= 1.2f)
                {
                    SetTestPhase(
                        FlightTestPhase.Boost,
                        new ShipControlCommand(
                            1.0f, 0.0f, 0.0f,
                            0.0f, 0.0f, 0.0f,
                            true, false));
                }
                break;

            case FlightTestPhase.Boost:
                if (_phaseElapsed >= 1.5f)
                {
                    SetTestPhase(
                        FlightTestPhase.BrakeAndStabilize,
                        new ShipControlCommand(
                            0.0f, 0.0f, 0.0f,
                            0.0f, 0.0f, 0.0f,
                            false, true));
                }
                break;

            case FlightTestPhase.BrakeAndStabilize:
                if (_ship.Speed <= TestMaximumFinalSpeed &&
                    _ship.AngularSpeedDegrees <=
                        TestMaximumFinalAngularSpeedDegrees)
                {
                    _ship.SetCameraMode(ShipCameraMode.Cockpit, true);
                    SetTestPhase(
                        FlightTestPhase.CameraSwitch,
                        ShipControlCommand.Neutral);
                }
                else if (_phaseElapsed >= 4.5f)
                {
                    FinishFlightTest(
                        ShipFlightTestState.Failed,
                        $"brake speed={_ship.Speed:F2}, " +
                        $"angular={_ship.AngularSpeedDegrees:F2}");
                }
                break;

            case FlightTestPhase.CameraSwitch:
                if (_phaseElapsed >= 0.35f &&
                    _ship.CameraMode == ShipCameraMode.Cockpit)
                {
                    _ship.SetCameraMode(ShipCameraMode.Chase, true);
                }

                if (_phaseElapsed >= 0.7f)
                {
                    EvaluateFlightTest();
                }
                break;
        }
    }

    private void SetTestPhase(
        FlightTestPhase phase,
        ShipControlCommand command)
    {
        if (_ship is null)
        {
            return;
        }

        _testPhase = phase;
        _phaseElapsed = 0.0f;
        _ship.SetExternalCommand(command);
    }

    private void UpdateTestMetrics()
    {
        if (_ship is null)
        {
            return;
        }

        _maximumSpeed = Math.Max(_maximumSpeed, _ship.Speed);
        _maximumAngularSpeed = Math.Max(
            _maximumAngularSpeed,
            _ship.AngularSpeedDegrees);
        _maximumLateralSpeed = Math.Max(
            _maximumLateralSpeed,
            Math.Abs(_ship.LocalVelocity.X));
        _maximumVerticalSpeed = Math.Max(
            _maximumVerticalSpeed,
            Math.Abs(_ship.LocalVelocity.Y));
        _maximumDistance = Math.Max(
            _maximumDistance,
            _testStartPosition.DistanceTo(_ship.GlobalPosition));
    }

    private void EvaluateFlightTest()
    {
        if (_ship is null)
        {
            return;
        }

        int cameraSwitches =
            _ship.CameraSwitchCount - _cameraSwitchBaseline;
        int collisions = _ship.CollisionEvents - _collisionBaseline;

        if (_maximumSpeed < TestMinimumSpeed)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"vmax={_maximumSpeed:F1} < {TestMinimumSpeed:F1}");
        }
        else if (_maximumDistance < TestMinimumTravelDistance)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"distance={_maximumDistance:F1} < " +
                $"{TestMinimumTravelDistance:F1}");
        }
        else if (_maximumLateralSpeed < TestMinimumLateralSpeed)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"lateral={_maximumLateralSpeed:F1} < " +
                $"{TestMinimumLateralSpeed:F1}");
        }
        else if (_maximumVerticalSpeed < TestMinimumVerticalSpeed)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"vertical={_maximumVerticalSpeed:F1} < " +
                $"{TestMinimumVerticalSpeed:F1}");
        }
        else if (_maximumAngularSpeed < TestMinimumAngularSpeedDegrees)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"angular={_maximumAngularSpeed:F1} < " +
                $"{TestMinimumAngularSpeedDegrees:F1}");
        }
        else if (_ship.Speed > TestMaximumFinalSpeed ||
            _ship.AngularSpeedDegrees > TestMaximumFinalAngularSpeedDegrees)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"final speed={_ship.Speed:F2}, " +
                $"angular={_ship.AngularSpeedDegrees:F2}");
        }
        else if (cameraSwitches < 2)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"camera switches={cameraSwitches}");
        }
        else if (collisions > 0)
        {
            FinishFlightTest(
                ShipFlightTestState.Failed,
                $"unexpected collisions={collisions}");
        }
        else
        {
            FinishFlightTest(
                ShipFlightTestState.Passed,
                "все аркадные режимы подтверждены");
        }
    }

    private void FinishFlightTest(
        ShipFlightTestState finalState,
        string result)
    {
        if (_ship is null)
        {
            return;
        }

        _testCameraSwitches =
            _ship.CameraSwitchCount - _cameraSwitchBaseline;
        _testCollisions = _ship.CollisionEvents - _collisionBaseline;
        _testErrors = _ship.RuntimeErrorCount - _runtimeErrorBaseline;
        _finalSpeed = _ship.Speed;
        _finalAngularSpeed = _ship.AngularSpeedDegrees;
        _testState = finalState;
        _testPhase = FlightTestPhase.None;
        _testResult = result;
        _ship.RestoreRuntimeState(_testBaseline);
        _ship.SetManualControlEnabled(true);

        string status = finalState switch
        {
            ShipFlightTestState.Passed => "PASS",
            ShipFlightTestState.Failed => "FAIL",
            ShipFlightTestState.Cancelled => "CANCELLED",
            _ => finalState.ToString().ToUpperInvariant()
        };

        GD.Print(
            $"TASK-043 arcade flight acceptance {status}: " +
            $"vmax={_maximumSpeed:F2}; distance={_maximumDistance:F2}; " +
            $"lateral={_maximumLateralSpeed:F2}; " +
            $"vertical={_maximumVerticalSpeed:F2}; " +
            $"angular={_maximumAngularSpeed:F2}; " +
            $"finalSpeed={_finalSpeed:F2}; " +
            $"finalAngular={_finalAngularSpeed:F2}; " +
            $"cameraSwitches={_testCameraSwitches}; " +
            $"collisions={_testCollisions}; errors={_testErrors}; " +
            $"result={result}");
    }

    private void ApplyHudMode()
    {
        if (_compactMargin is null || _detailedMargin is null ||
            _hiddenHint is null)
        {
            return;
        }

        _compactMargin.Visible = _hudMode == ShipPrototypeHudMode.Compact;
        _detailedMargin.Visible = _hudMode == ShipPrototypeHudMode.Detailed;
        _hiddenHint.Visible = _hudMode == ShipPrototypeHudMode.Hidden;
        UpdateHudLayout();
    }

    private void UpdateHudLayout()
    {
        if (_compactMargin is null || _detailedMargin is null)
        {
            return;
        }

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        const float margin = 12.0f;
        float maximumWidth = Math.Max(1.0f, viewport.X - (margin * 2.0f));
        float maximumHeight = Math.Max(1.0f, viewport.Y - (margin * 2.0f));

        _compactMargin.Position = new Vector2(margin, margin);
        _compactMargin.Size = new Vector2(
            Math.Min(HudCompactWidth, maximumWidth),
            Math.Min(HudCompactHeight, maximumHeight));
        _compactMargin.CustomMinimumSize = _compactMargin.Size;

        _detailedMargin.Position = new Vector2(margin, margin);
        _detailedMargin.Size = new Vector2(
            Math.Min(HudDetailedWidth, maximumWidth),
            Math.Min(HudDetailedHeight, maximumHeight));
        _detailedMargin.CustomMinimumSize = _detailedMargin.Size;
    }

    private static string FormatCompactTestState(Enum state)
    {
        return state.ToString().ToUpperInvariant() switch
        {
            "PASSED" => "PASS",
            "FAILED" => "FAIL",
            "CANCELLED" => "STOP",
            "RUNNING" => "RUN",
            _ => "READY"
        };
    }

    private void UpdateHud()
    {
        if (_ship is null || _compactLabel is null || _detailedLabel is null)
        {
            return;
        }

        string camera = _ship.CameraMode == ShipCameraMode.Chase
            ? "погоня"
            : "кабина";
        string stabilization = _ship.AutoStabilizationEnabled
            ? "ON"
            : "OFF";
        string driveState = _ship.BoostActive
            ? "BOOST"
            : _ship.BrakeActive
                ? "BRAKE"
                : "CRUISE";

        _compactLabel.Text =
            "ПРОТОТИП D — КОСМОС + АТМОСФЕРА + ПОСАДКА  •  H — HUD\n" +
            $"Скорость: {_ship.Speed:F1} м/с  •  режим: {driveState}  •  " +
            $"камера: {camera}  •  стабилизация: {stabilization}\n" +
            $"Local V: X={_ship.LocalVelocity.X:F1}  " +
            $"Y={_ship.LocalVelocity.Y:F1}  " +
            $"Z={_ship.LocalVelocity.Z:F1} м/с  •  " +
            $"ω={_ship.AngularSpeedDegrees:F1}°/с\n" +
            $"{AtmosphereCompactStatus}\n" +
            $"{LandingCompactStatus}\n" +
            $"Tests: J={FormatCompactTestState(_testState)}  " +
            $"L={FormatCompactTestState(_atmosphereTestState)}  " +
            $"N={FormatCompactTestState(_landingTestState)}\n" +
            $"{TouchdownTestStatusText}\n" +
            $"{LandingSoakStatusText}\n" +
            "W — тяга  •  S/X — тормоз  •  A/D — боковая  •  Space/C — вверх/вниз  •  " +
            "мышь — тангаж/рыскание\n" +
            "Q/E — крен  •  B — форсаж  •  X — тормоз  •  " +
            "G — стабилизация  •  F2 — камера  •  P — атмосфера  •  " +
            "M — посадка/касание/взлёт  •  J/L/N/O/V — тесты";

        _detailedLabel.Text =
            "ПРОТОТИП D — АРКАДНАЯ ФИЗИКА КОРАБЛЯ\n" +
            "HUD: подробный  •  H — компактный/скрытый  •  " +
            "Esc — освободить курсор\n" +
            $"World position: ({_ship.GlobalPosition.X:F1}, " +
            $"{_ship.GlobalPosition.Y:F1}, {_ship.GlobalPosition.Z:F1}) м\n" +
            $"World velocity: ({_ship.Velocity.X:F2}, " +
            $"{_ship.Velocity.Y:F2}, {_ship.Velocity.Z:F2}) м/с\n" +
            $"Local velocity: ({_ship.LocalVelocity.X:F2}, " +
            $"{_ship.LocalVelocity.Y:F2}, {_ship.LocalVelocity.Z:F2}) м/с\n" +
            $"Speed: {_ship.Speed:F2} м/с  •  " +
            $"angular: {_ship.AngularSpeedDegrees:F2}°/с\n" +
            $"Angular local: pitch={Mathf.RadToDeg(_ship.AngularVelocityLocal.X):F2}  " +
            $"yaw={Mathf.RadToDeg(_ship.AngularVelocityLocal.Y):F2}  " +
            $"roll={Mathf.RadToDeg(_ship.AngularVelocityLocal.Z):F2}°/с\n" +
            $"Drive: {driveState}  •  camera={camera}  •  " +
            $"camera switches={_ship.CameraSwitchCount}\n" +
            $"Auto stabilization: {stabilization}  •  " +
            $"external control={_ship.ExternalControlActive}\n" +
            $"Collision events={_ship.CollisionEvents}  •  " +
            $"runtime errors={_ship.RuntimeErrorCount}\n" +
            $"{AtmosphereDetailedStatus}\n" +
            $"{LandingDetailedStatus}\n\n" +
            $"{FlightTestStatusText}\n" +
            $"{AtmosphereTestStatusText}\n" +
            $"{LandingTestStatusText}\n" +
            $"{TouchdownTestStatusText}\n" +
            $"{LandingSoakStatusText}\n" +
            $"Free-flight metrics: vmax={_maximumSpeed:F2}; " +
            $"distance={_maximumDistance:F2}; " +
            $"lateral={_maximumLateralSpeed:F2}; " +
            $"vertical={_maximumVerticalSpeed:F2}; " +
            $"angular={_maximumAngularSpeed:F2}\n\n" +
            "Управление:\n" +
            "W — тяга вперёд  •  S/X — тормоз\n" +
            "A/D — боковые импульсные двигатели\n" +
            "Space/C — вертикальные импульсные двигатели\n" +
            "Мышь или стрелки — тангаж и рыскание\n" +
            "Q/E — крен\n" +
            "B — форсаж\n" +
            "X — торможение\n" +
            "G — автоматическая стабилизация\n" +
            "F2 — переключение погоня/кабина\n" +
            "R — сброс корабля\n" +
            "P — атмосферный подход/возврат в космос\n" +
            "M — поиск точки и автоматическое выравнивание\n" +
            "J — автоматический free-flight test\n" +
            "L — автоматический atmosphere test\n" +
            "N — автоматический landing-point test\n" +
            "O — автоматический touchdown/takeoff test\n" +
            "V — soak test 100 последовательных посадок\n" +
            "H — compact/detailed/hidden HUD";
    }
}
