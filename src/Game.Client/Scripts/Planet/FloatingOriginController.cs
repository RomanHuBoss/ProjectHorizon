using System;
using System.Collections.Generic;
using Godot;

public enum FloatingOriginTestState
{
    Ready = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class FloatingOriginController : Node
{
    private static readonly Vector3[] AcceptanceOffsets =
    {
        new(2304.0f, 0.0f, 0.0f),
        new(0.0f, -2304.0f, 0.0f),
        new(0.0f, 0.0f, 2304.0f),
        new(-2304.0f, 2304.0f, -2304.0f)
    };

    [Export]
    public NodePath PlayerPath { get; set; } = new("../PlanetaryPlayer");

    [Export]
    public NodePath PlanetPath { get; set; } = new("../Planet");

    [Export]
    public NodePath OverviewCameraRigPath { get; set; } = new("../CameraRig");

    [Export(PropertyHint.Range, "512.0,16384.0,1.0")]
    public float CellSize { get; set; } = 4096.0f;

    [Export(PropertyHint.Range, "256.0,8192.0,1.0")]
    public float ShiftThreshold { get; set; } = 2048.0f;

    [Export(PropertyHint.Range, "0.05,1.0,0.01")]
    public float TestSettleSeconds { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float TestAllowedGroundGapSeconds { get; set; } = 0.12f;

    private readonly List<Node3D> _shiftTargets = new();
    private PlanetaryPlayerController? _player;
    private Node3D? _planet;
    private Node3D? _overviewCameraRig;

    private long _cellX;
    private long _cellY;
    private long _cellZ;
    private int _shiftEvents;
    private long _cellTransitions;
    private Vector3 _lastOriginShift;

    private FloatingOriginTestState _testState = FloatingOriginTestState.Ready;
    private int _testStep;
    private float _testElapsed;
    private float _testSettleElapsed;
    private float _testUnsupportedCurrent;
    private float _testMaxUnsupported;
    private int _testShiftEvents;
    private long _testCellTransitions;
    private long _testStartCellX;
    private long _testStartCellY;
    private long _testStartCellZ;
    private Transform3D _testStartPlayerTransform;
    private Transform3D _testStartPlanetTransform;
    private Transform3D _testStartCameraRigTransform;
    private Vector3 _testStartVelocity;
    private bool _testPlayerPhysicsWasEnabled;
    private Vector3 _testPlayerPlanetOffset;
    private Vector3 _testCameraPlanetOffset;
    private Vector3 _testAppliedUniformTranslation;
    private double _testExpectedLogicalX;
    private double _testExpectedLogicalY;
    private double _testExpectedLogicalZ;
    private double _testMaxLogicalError;
    private float _testMaxRelativeError;
    private float _testMaxLocalComponent;
    private float _testMaxUpError;
    private long _testPeakCellMagnitude;
    private string _testResult = "READY";

    public long CellX => _cellX;
    public long CellY => _cellY;
    public long CellZ => _cellZ;
    public int ShiftEvents => _shiftEvents;
    public long CellTransitions => _cellTransitions;
    public Vector3 LastOriginShift => _lastOriginShift;
    public Vector3 LocalPosition => _player?.GlobalPosition ?? Vector3.Zero;
    public double LogicalX => (_cellX * (double)CellSize) + LocalPosition.X;
    public double LogicalY => (_cellY * (double)CellSize) + LocalPosition.Y;
    public double LogicalZ => (_cellZ * (double)CellSize) + LocalPosition.Z;
    public FloatingOriginTestState TestState => _testState;
    public bool TestRunning => _testState == FloatingOriginTestState.Running;

    public string TestStatusText
    {
        get
        {
            return _testState switch
            {
                FloatingOriginTestState.Running =>
                    "TASK-032 origin (Y): RUNNING " +
                    $"{_testStep}/{AcceptanceOffsets.Length}, " +
                    $"shifts={_testShiftEvents}, cells={_testCellTransitions}",
                FloatingOriginTestState.Passed =>
                    "TASK-032 origin (Y): PASS " +
                    $"shifts={_testShiftEvents}, cells={_testCellTransitions}, " +
                    $"localMax={_testMaxLocalComponent:F1} м, " +
                    $"logicalErr={_testMaxLogicalError:F3} м, " +
                    $"relativeErr={_testMaxRelativeError:F4} м, " +
                    $"gap={_testMaxUnsupported:F2} с",
                FloatingOriginTestState.Failed =>
                    $"TASK-032 origin (Y): FAIL — {_testResult}",
                FloatingOriginTestState.Cancelled =>
                    "TASK-032 origin (Y): остановлен пользователем",
                _ => "TASK-032 origin (Y): READY"
            };
        }
    }

    public override void _Ready()
    {
        _player = GetNodeOrNull<PlanetaryPlayerController>(PlayerPath);
        _planet = GetNodeOrNull<Node3D>(PlanetPath);
        _overviewCameraRig = GetNodeOrNull<Node3D>(OverviewCameraRigPath);

        if (_player is null || _planet is null || _overviewCameraRig is null)
        {
            throw new InvalidOperationException(
                "FloatingOriginController requires Player, Planet and CameraRig nodes.");
        }

        if (CellSize <= 0.0f || ShiftThreshold <= 0.0f ||
            Math.Abs(ShiftThreshold - (CellSize * 0.5f)) > 0.001f)
        {
            throw new InvalidOperationException(
                "Floating origin requires ShiftThreshold to equal half of CellSize.");
        }

        _shiftTargets.Add(_planet);
        _shiftTargets.Add(_player);
        _shiftTargets.Add(_overviewCameraRig);

        GD.Print(
            "Floating origin initialized: " +
            $"cellSize={CellSize:F0} m; threshold={ShiftThreshold:F0} m; " +
            $"local={FormatVector(LocalPosition)}; cell=(0,0,0)");
    }

    public override void _PhysicsProcess(double delta)
    {
        float deltaSeconds = (float)delta;

        if (TestRunning)
        {
            UpdateAcceptanceTest(deltaSeconds);
            return;
        }

        TryShiftOrigin();
    }

    public bool BeginAcceptanceTest()
    {
        if (TestRunning || _player is null || _planet is null ||
            _overviewCameraRig is null)
        {
            return false;
        }

        _player.CancelSeamTraversalTest(true);
        _testStartVelocity = _player.Velocity;
        _player.SetExternalMovementLocked(true);
        _testPlayerPhysicsWasEnabled = _player.IsPhysicsProcessing();
        _player.SetPhysicsProcess(false);

        _testStartCellX = _cellX;
        _testStartCellY = _cellY;
        _testStartCellZ = _cellZ;
        _testStartPlayerTransform = _player.GlobalTransform;
        _testStartPlanetTransform = _planet.GlobalTransform;
        _testStartCameraRigTransform = _overviewCameraRig.GlobalTransform;
        _testPlayerPlanetOffset =
            _player.GlobalPosition - _planet.GlobalPosition;
        _testCameraPlanetOffset =
            _overviewCameraRig.GlobalPosition - _planet.GlobalPosition;
        _testAppliedUniformTranslation = Vector3.Zero;
        _testExpectedLogicalX = LogicalX;
        _testExpectedLogicalY = LogicalY;
        _testExpectedLogicalZ = LogicalZ;
        _testStep = 0;
        _testElapsed = 0.0f;
        _testSettleElapsed = 0.0f;
        _testUnsupportedCurrent = 0.0f;
        _testMaxUnsupported = 0.0f;
        _testShiftEvents = 0;
        _testCellTransitions = 0;
        _testMaxLogicalError = 0.0;
        _testMaxRelativeError = 0.0f;
        _testMaxLocalComponent = MaximumAbsoluteComponent(LocalPosition);
        _testMaxUpError = _player.UpAlignmentErrorDegrees;
        _testPeakCellMagnitude = 0;
        _testResult = "выполняется";
        _testState = FloatingOriginTestState.Running;

        GD.Print(
            "TASK-032 floating-origin acceptance started: " +
            $"cell=({_cellX},{_cellY},{_cellZ}); " +
            $"local={FormatVector(LocalPosition)}; steps={AcceptanceOffsets.Length}");
        return true;
    }

    public void CancelAcceptanceTest(bool restoreBaseline)
    {
        if (!TestRunning)
        {
            return;
        }

        if (restoreBaseline)
        {
            RestoreAcceptanceBaseline();
        }
        else if (_player is not null)
        {
            _player.SetExternalMovementLocked(false);
            _player.SetPhysicsProcess(_testPlayerPhysicsWasEnabled);
        }

        _testState = FloatingOriginTestState.Cancelled;
        _testResult = "остановлен пользователем";
        GD.Print("TASK-032 floating-origin acceptance cancelled.");
    }

    private void UpdateAcceptanceTest(float deltaSeconds)
    {
        if (_player is null || _planet is null || _overviewCameraRig is null)
        {
            FinishAcceptanceTest(false, "required nodes disappeared");
            return;
        }

        _testElapsed += deltaSeconds;
        UpdateGroundGap(deltaSeconds);
        _testMaxUpError = Math.Max(
            _testMaxUpError,
            _player.UpAlignmentErrorDegrees);

        if (_testStep < AcceptanceOffsets.Length)
        {
            Vector3 offset = AcceptanceOffsets[_testStep];
            ApplyUniformTranslation(offset, true);
            _testExpectedLogicalX += offset.X;
            _testExpectedLogicalY += offset.Y;
            _testExpectedLogicalZ += offset.Z;

            int shiftsBefore = _shiftEvents;
            long transitionsBefore = _cellTransitions;
            bool shifted = TryShiftOrigin();
            _testShiftEvents += _shiftEvents - shiftsBefore;
            _testCellTransitions += _cellTransitions - transitionsBefore;

            EvaluateAcceptanceMetrics();

            if (!shifted)
            {
                FinishAcceptanceTest(
                    false,
                    $"step {_testStep + 1} did not trigger origin shift");
                return;
            }

            _testStep++;
            return;
        }

        _testSettleElapsed += deltaSeconds;
        TryShiftOrigin();
        EvaluateAcceptanceMetrics();

        if (_testSettleElapsed < TestSettleSeconds)
        {
            return;
        }

        bool cellsRestored =
            _cellX == _testStartCellX &&
            _cellY == _testStartCellY &&
            _cellZ == _testStartCellZ;
        float finalLocalError =
            _player.GlobalPosition.DistanceTo(_testStartPlayerTransform.Origin);
        double finalLogicalError = Distance3D(
            LogicalX,
            LogicalY,
            LogicalZ,
            (_testStartCellX * (double)CellSize) +
                _testStartPlayerTransform.Origin.X,
            (_testStartCellY * (double)CellSize) +
                _testStartPlayerTransform.Origin.Y,
            (_testStartCellZ * (double)CellSize) +
                _testStartPlayerTransform.Origin.Z);

        bool pass =
            _testShiftEvents == AcceptanceOffsets.Length &&
            _testCellTransitions == 6 &&
            _testPeakCellMagnitude >= 1 &&
            cellsRestored &&
            finalLocalError <= 0.05f &&
            finalLogicalError <= 0.05 &&
            _testMaxLocalComponent <= ShiftThreshold + 0.05f &&
            _testMaxLogicalError <= 0.01 &&
            _testMaxRelativeError <= 0.001f &&
            _testMaxUnsupported <= TestAllowedGroundGapSeconds &&
            _testMaxUpError <= 3.0f;

        string details =
            $"shifts={_testShiftEvents}/{AcceptanceOffsets.Length}; " +
            $"cells={_testCellTransitions}/6; peakCell={_testPeakCellMagnitude}; " +
            $"cellsRestored={cellsRestored}; finalLocalErr={finalLocalError:F4}; " +
            $"finalLogicalErr={finalLogicalError:F4}; " +
            $"localMax={_testMaxLocalComponent:F2}; " +
            $"logicalErr={_testMaxLogicalError:F4}; " +
            $"relativeErr={_testMaxRelativeError:F5}; " +
            $"gap={_testMaxUnsupported:F3}; up={_testMaxUpError:F2}°";
        FinishAcceptanceTest(pass, details);
    }

    private bool TryShiftOrigin()
    {
        if (_player is null)
        {
            return false;
        }

        Vector3 localPosition = _player.GlobalPosition;
        long deltaCellX = CalculateCellDelta(localPosition.X);
        long deltaCellY = CalculateCellDelta(localPosition.Y);
        long deltaCellZ = CalculateCellDelta(localPosition.Z);

        if (deltaCellX == 0 && deltaCellY == 0 && deltaCellZ == 0)
        {
            return false;
        }

        Vector3 originShift = new(
            (float)(deltaCellX * (double)CellSize),
            (float)(deltaCellY * (double)CellSize),
            (float)(deltaCellZ * (double)CellSize));

        double logicalXBefore = LogicalX;
        double logicalYBefore = LogicalY;
        double logicalZBefore = LogicalZ;

        ApplyUniformTranslation(-originShift, TestRunning);
        _cellX += deltaCellX;
        _cellY += deltaCellY;
        _cellZ += deltaCellZ;
        _shiftEvents++;
        _cellTransitions +=
            Math.Abs(deltaCellX) +
            Math.Abs(deltaCellY) +
            Math.Abs(deltaCellZ);
        _lastOriginShift = originShift;

        double continuityError = Distance3D(
            logicalXBefore,
            logicalYBefore,
            logicalZBefore,
            LogicalX,
            LogicalY,
            LogicalZ);

        GD.Print(
            "Floating origin shift: " +
            $"deltaCell=({deltaCellX},{deltaCellY},{deltaCellZ}); " +
            $"cell=({_cellX},{_cellY},{_cellZ}); " +
            $"shift={FormatVector(originShift)}; " +
            $"local={FormatVector(LocalPosition)}; " +
            $"continuityError={continuityError:F6} m");
        return true;
    }

    private void ApplyUniformTranslation(Vector3 translation, bool trackTest)
    {
        foreach (Node3D target in _shiftTargets)
        {
            Transform3D transform = target.GlobalTransform;
            transform.Origin += translation;
            target.GlobalTransform = transform;
        }

        _player?.NotifyWorldTranslated(translation);
        if (trackTest)
        {
            _testAppliedUniformTranslation += translation;
        }
    }

    private void EvaluateAcceptanceMetrics()
    {
        if (_player is null || _planet is null || _overviewCameraRig is null)
        {
            return;
        }

        double logicalError = Distance3D(
            LogicalX,
            LogicalY,
            LogicalZ,
            _testExpectedLogicalX,
            _testExpectedLogicalY,
            _testExpectedLogicalZ);
        _testMaxLogicalError = Math.Max(
            _testMaxLogicalError,
            logicalError);

        float playerPlanetError =
            ((_player.GlobalPosition - _planet.GlobalPosition) -
                _testPlayerPlanetOffset).Length();
        float cameraPlanetError =
            ((_overviewCameraRig.GlobalPosition - _planet.GlobalPosition) -
                _testCameraPlanetOffset).Length();
        _testMaxRelativeError = Math.Max(
            _testMaxRelativeError,
            Math.Max(playerPlanetError, cameraPlanetError));
        _testMaxLocalComponent = Math.Max(
            _testMaxLocalComponent,
            MaximumAbsoluteComponent(LocalPosition));

        long relativeCellX = Math.Abs(_cellX - _testStartCellX);
        long relativeCellY = Math.Abs(_cellY - _testStartCellY);
        long relativeCellZ = Math.Abs(_cellZ - _testStartCellZ);
        _testPeakCellMagnitude = Math.Max(
            _testPeakCellMagnitude,
            Math.Max(relativeCellX, Math.Max(relativeCellY, relativeCellZ)));
    }

    private void UpdateGroundGap(float deltaSeconds)
    {
        if (_player?.HasPhysicalGroundContact == true)
        {
            _testUnsupportedCurrent = 0.0f;
            return;
        }

        _testUnsupportedCurrent += deltaSeconds;
        _testMaxUnsupported = Math.Max(
            _testMaxUnsupported,
            _testUnsupportedCurrent);
    }

    private void FinishAcceptanceTest(bool passed, string details)
    {
        _testResult = details;
        RestoreAcceptanceBaseline();
        _testState = passed
            ? FloatingOriginTestState.Passed
            : FloatingOriginTestState.Failed;

        string verdict = passed ? "PASS" : "FAIL";
        GD.Print(
            $"TASK-032 floating-origin acceptance {verdict}: {details}; " +
            $"duration={_testElapsed:F2} s");
    }

    private void RestoreAcceptanceBaseline()
    {
        if (_player is null || _planet is null || _overviewCameraRig is null)
        {
            return;
        }

        if (_testAppliedUniformTranslation.LengthSquared() > 0.0000001f)
        {
            _player.NotifyWorldTranslated(-_testAppliedUniformTranslation);
        }

        _player.GlobalTransform = _testStartPlayerTransform;
        _planet.GlobalTransform = _testStartPlanetTransform;
        _overviewCameraRig.GlobalTransform = _testStartCameraRigTransform;
        _player.Velocity = _testStartVelocity;
        _cellX = _testStartCellX;
        _cellY = _testStartCellY;
        _cellZ = _testStartCellZ;
        _testAppliedUniformTranslation = Vector3.Zero;
        _player.SetExternalMovementLocked(false);
        _player.SetPhysicsProcess(_testPlayerPhysicsWasEnabled);
    }

    private long CalculateCellDelta(float coordinate)
    {
        if (coordinate <= ShiftThreshold && coordinate >= -ShiftThreshold)
        {
            return 0;
        }

        return (long)Math.Floor(
            (coordinate + ShiftThreshold) / CellSize);
    }

    private static float MaximumAbsoluteComponent(Vector3 value)
    {
        return Math.Max(
            Math.Abs(value.X),
            Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
    }

    private static double Distance3D(
        double ax,
        double ay,
        double az,
        double bx,
        double by,
        double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.X:F1},{value.Y:F1},{value.Z:F1})";
    }
}
