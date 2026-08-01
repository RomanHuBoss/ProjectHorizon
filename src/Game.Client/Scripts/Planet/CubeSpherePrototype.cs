using System;
using System.Collections.Generic;
using Godot;

public enum CubeSphereDebugMode
{
    FaceIds = 0,
    RadialNormals = 1
}

public enum CubeSphereCameraMode
{
    PlanetaryPlayer = 0,
    OverviewOrbit = 1
}

public partial class CubeSpherePrototype : Node3D
{
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

    private readonly List<MeshInstance3D> _faceMeshes = new();
    private readonly List<CollisionShape3D> _collisionShapes = new();
    private Node3D? _facesRoot;
    private StaticBody3D? _collisionBody;
    private Node3D? _cameraRig;
    private Camera3D? _overviewCamera;
    private PlanetaryPlayerController? _planetaryPlayer;
    private FloatingOriginController? _floatingOrigin;
    private Label? _hudLabel;
    private CubeSphereBuildData? _buildData;
    private CubeSphereDebugMode _debugMode = CubeSphereDebugMode.FaceIds;
    private CubeSphereCameraMode _cameraMode =
        CubeSphereCameraMode.PlanetaryPlayer;
    private bool _orbitPaused;
    private double _hudRefreshAccumulator;

    public override void _Ready()
    {
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
            _debugMode = _debugMode == CubeSphereDebugMode.FaceIds
                ? CubeSphereDebugMode.RadialNormals
                : CubeSphereDebugMode.FaceIds;
            RebuildVisualMeshes();
            UpdateHud();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.F2)
        {
            if (_floatingOrigin?.TestRunning == true)
            {
                _floatingOrigin.CancelAcceptanceTest(true);
            }

            if (_planetaryPlayer?.SeamTestRunning == true)
            {
                _planetaryPlayer.CancelSeamTraversalTest(true);
            }

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
        else if ((keyEvent.Keycode == Key.R ||
            keyEvent.PhysicalKeycode == Key.R) &&
            _floatingOrigin?.TestRunning == true)
        {
            _floatingOrigin.CancelAcceptanceTest(true);
            UpdateHud();
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
        _buildData = CubeSphereMeshBuilder.Build(
            FaceResolution,
            PlanetRadius,
            HeightAmplitude,
            NoiseFrequency,
            NoiseSeed);

        foreach (CubeSphereFaceData faceData in _buildData.Faces)
        {
            ArrayMesh faceMesh = CreateFaceMesh(faceData);
            MeshInstance3D meshInstance = new()
            {
                Name = $"Face_{faceData.DisplayName.Replace('+', 'P').Replace('-', 'N')}",
                Mesh = faceMesh
            };
            _facesRoot.AddChild(meshInstance);
            _faceMeshes.Add(meshInstance);

            if (GenerateCollision)
            {
                ConcavePolygonShape3D shape = faceMesh.CreateTrimeshShape();
                shape.BackfaceCollision = true;
                CollisionShape3D collisionShape = new()
                {
                    Name = $"Collision_{faceData.DisplayName.Replace('+', 'P').Replace('-', 'N')}",
                    Shape = shape
                };
                _collisionBody.AddChild(collisionShape);
                _collisionShapes.Add(collisionShape);
            }
        }

        double elapsedMilliseconds =
            (Time.GetTicksUsec() - startedAtMicroseconds) / 1000.0;
        GD.Print(
            "CubeSphere foundation: " +
            $"faces={_faceMeshes.Count}/6; " +
            $"resolution={_buildData.Resolution}x{_buildData.Resolution}; " +
            $"vertices={_buildData.TotalVertices}; " +
            $"triangles={_buildData.TotalTriangles}; " +
            $"collision={_collisionShapes.Count}; " +
            $"seamPairs={_buildData.SeamComparisons}/" +
            $"{_buildData.ExpectedSeamComparisons}; " +
            $"maxSeamPositionError={_buildData.MaximumSeamPositionError:E3}; " +
            $"maxSeamNormalError={_buildData.MaximumSeamNormalError:E3}; " +
            $"build={elapsedMilliseconds:F2} ms");
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
        if (_buildData is null || _faceMeshes.Count != _buildData.Faces.Count)
        {
            return;
        }

        for (int i = 0; i < _faceMeshes.Count; i++)
        {
            _faceMeshes[i].Mesh = CreateFaceMesh(_buildData.Faces[i]);
        }
    }

    private ArrayMesh CreateFaceMesh(CubeSphereFaceData faceData)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < faceData.Vertices.Count; i++)
        {
            Vector3 normal = faceData.Normals[i];
            surfaceTool.SetNormal(normal);
            surfaceTool.SetUV(faceData.Uvs[i]);
            surfaceTool.SetColor(_debugMode == CubeSphereDebugMode.FaceIds
                ? faceData.DebugColor
                : new Color(
                    (normal.X * 0.5f) + 0.5f,
                    (normal.Y * 0.5f) + 0.5f,
                    (normal.Z * 0.5f) + 0.5f,
                    1.0f));
            surfaceTool.AddVertex(faceData.Vertices[i]);
        }

        foreach (int index in faceData.Indices)
        {
            surfaceTool.AddIndex(index);
        }

        ArrayMesh mesh = surfaceTool.Commit();
        mesh.SurfaceSetMaterial(0, CreatePlanetMaterial());
        return mesh;
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

    private void ClearGeneratedChildren()
    {
        foreach (MeshInstance3D meshInstance in _faceMeshes)
        {
            meshInstance.QueueFree();
        }

        foreach (CollisionShape3D collisionShape in _collisionShapes)
        {
            collisionShape.QueueFree();
        }

        _faceMeshes.Clear();
        _collisionShapes.Clear();
    }

    private void UpdateHud()
    {
        if (_hudLabel is null)
        {
            return;
        }

        string orbitState = _orbitPaused ? "пауза" : "вращение";
        string debugMode = _debugMode == CubeSphereDebugMode.FaceIds
            ? "цвета граней"
            : "радиальные нормали";

        if (_buildData is null)
        {
            _hudLabel.Text =
                "ПРОТОТИП C — FLOATING ORIGIN\n" +
                "Построение геометрии...";
            return;
        }

        bool seamPass =
            _buildData.SeamComparisons == _buildData.ExpectedSeamComparisons &&
            _buildData.MaximumSeamPositionError <= 0.001f &&
            _buildData.MaximumSeamNormalError <= 0.0001f;
        string seamStatus = seamPass ? "PASS" : "FAIL";

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
                $"local=({local.X:F1},{local.Y:F1},{local.Z:F1}) м
" +
                $"Логические: ({_floatingOrigin.LogicalX:F1}," +
                $"{_floatingOrigin.LogicalY:F1},{_floatingOrigin.LogicalZ:F1}) м  •  " +
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
            "ПРОТОТИП C — FLOATING ORIGIN\n" +
            $"Грани: {_faceMeshes.Count}/6  •  collision: {_collisionShapes.Count}/" +
            $"{(GenerateCollision ? 6 : 0)}  •  швы: {seamStatus} " +
            $"({_buildData.SeamComparisons}/{_buildData.ExpectedSeamComparisons})\n" +
            $"Игрок: {playerStatus}\n" +
            $"{contactStatus}\n" +
            $"Радиальная система: {radialStatus}  •  камера: {cameraState}  •  " +
            $"режим: {debugMode}\n" +
            $"{coordinateStatus}\n" +
            $"{originStatus}\n" +
            $"{seamTestStatus}\n" +
            $"Радиус: {PlanetRadius:F1} м  •  рельеф: ±{HeightAmplitude:F1} м  •  " +
            $"seed: {NoiseSeed}  •  сетка: {_buildData.Resolution}×{_buildData.Resolution}\n" +
            "WASD — касательное движение  •  мышь — обзор  •  " +
            $"{contextualSpace}  •  R — сброс\n" +
            "F1 — цвета/нормали  •  F2 — игрок/обзор  •  " +
            "T — seam-test  •  Y — floating-origin-test";
    }
}
