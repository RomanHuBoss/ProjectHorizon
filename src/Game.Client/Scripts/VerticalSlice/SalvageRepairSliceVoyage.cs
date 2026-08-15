using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;

public partial class SalvageRepairSlice
{
    private StageOneVoyageRuntime? _stageOneVoyageRuntime;
    private ArcadeShipController? _voyageShip;
    private Node3D? _orbitalStation;
    private MeshInstance3D? _orbitalDockMarker;
    private MeshInstance3D? _planetApproachMarker;
    private Camera3D? _playerCamera;
    private uint _playerCollisionLayer;
    private uint _playerCollisionMask;
    private uint _shipTerminalCollisionLayer;
    private uint _shipTerminalCollisionMask;
    private bool _voyageNavigationAssist;
    private bool _stationServicesOpenedFromVoyage;
    private Task<StageOneVoyageAcceptanceReport>?
        _stageOneVoyageAcceptanceTask;
    private StageOneVoyageAcceptanceReport?
        _stageOneVoyageAcceptanceReport;
    private string _stageOneVoyageAcceptanceHud = "READY";

    private StageOneVoyageRuntime StageOneVoyage =>
        _stageOneVoyageRuntime ??
        throw new InvalidOperationException(
            "Stage 1 voyage runtime is unavailable.");

    private void BindStageOneVoyageSceneNodes()
    {
        _voyageShip = GetNodeOrNull<ArcadeShipController>(
            "Gameplay/VoyageShip");
        _orbitalStation = GetNodeOrNull<Node3D>(
            "Gameplay/OrbitalStation");
        _orbitalDockMarker = GetNodeOrNull<MeshInstance3D>(
            "Gameplay/OrbitalDockMarker");
        _planetApproachMarker = GetNodeOrNull<MeshInstance3D>(
            "Gameplay/PlanetApproachMarker");
        _playerCamera = _player?.GetNodeOrNull<Camera3D>(
            "Head/Camera3D");
        if (_voyageShip is null || _orbitalStation is null ||
            _orbitalDockMarker is null || _planetApproachMarker is null ||
            _playerCamera is null || _player is null || _shipTerminal is null)
        {
            throw new InvalidOperationException(
                "Stage 1 voyage scene is missing the ship, station, markers or camera.");
        }

        _playerCollisionLayer = _player.CollisionLayer;
        _playerCollisionMask = _player.CollisionMask;
        _shipTerminalCollisionLayer = _shipTerminal.CollisionLayer;
        _shipTerminalCollisionMask = _shipTerminal.CollisionMask;
    }

    private void InitializeStageOneVoyageRuntime(
        StageOneVoyageSaveData? saveData)
    {
        _stageOneVoyageRuntime = new StageOneVoyageRuntime(saveData);
        _voyageNavigationAssist = false;
        _stationServicesOpenedFromVoyage = false;
        ConfigureVoyageShipFromDerivedStats();
        ApplyStageOneVoyageToScene();
    }

    private void ConfigureVoyageShipFromDerivedStats()
    {
        if (_voyageShip is null || _shipSystemsRuntime is null)
        {
            return;
        }

        StageOneVoyageFlightProfile profile =
            StageOneVoyageRuntime.CreateFlightProfile(ShipSystems);
        _voyageShip.ForwardAcceleration = (float)profile.Acceleration;
        _voyageShip.ReverseAcceleration =
            (float)profile.ReverseAcceleration;
        _voyageShip.LateralAcceleration =
            (float)profile.LateralAcceleration;
        _voyageShip.VerticalAcceleration =
            (float)profile.VerticalAcceleration;
        _voyageShip.MaxSpeed = (float)profile.MaxSpeed;
        _voyageShip.BoostMaxSpeed = (float)profile.BoostMaxSpeed;
        _voyageShip.MaxPitchRateDegrees =
            (float)profile.PitchRateDegrees;
        _voyageShip.MaxYawRateDegrees =
            (float)profile.YawRateDegrees;
        _voyageShip.MaxRollRateDegrees =
            (float)profile.RollRateDegrees;
        _voyageShip.AngularAcceleration = (float)Math.Clamp(
            profile.YawRateDegrees / 14.0,
            3.0,
            12.0);
        _voyageShip.StabilizationAcceleration = (float)Math.Clamp(
            profile.RollRateDegrees / 12.0,
            5.0,
            16.0);

        double atmosphericFactor = Math.Clamp(
            profile.AtmosphericEfficiency / 100.0,
            0.0,
            1.0);
        _voyageShip.AtmosphereLiftMultiplier = (float)(
            0.75 + (0.55 * atmosphericFactor));
        _voyageShip.AtmosphereDragCoefficient = (float)(
            0.018 - (0.008 * atmosphericFactor));
        _voyageShip.AtmosphereMinimumSpeedAssist = (float)(
            24.0 - (12.0 * atmosphericFactor));
        _voyageShip.AtmosphereMaximumClimbSpeed = (float)(
            10.0 + (12.0 * atmosphericFactor));
    }

    private void ApplyStageOneVoyageToScene()
    {
        if (_voyageShip is null || _player is null ||
            _playerCamera is null || _shipTerminal is null ||
            _stageOneVoyageRuntime is null)
        {
            return;
        }

        StageOneVoyageSaveData state = StageOneVoyage.CreateSaveData();
        _voyageShip.GlobalPosition = new Vector3(
            (float)state.PositionX,
            (float)state.PositionY,
            (float)state.PositionZ);
        _voyageShip.Rotation = new Vector3(
            (float)state.RotationX,
            (float)state.RotationY,
            (float)state.RotationZ);
        _voyageShip.Velocity = new Vector3(
            (float)state.VelocityX,
            (float)state.VelocityY,
            (float)state.VelocityZ);

        bool piloted = StageOneVoyage.Piloted;
        _player.Visible = !piloted;
        _player.CollisionLayer = piloted ? 0u : _playerCollisionLayer;
        _player.CollisionMask = piloted ? 0u : _playerCollisionMask;
        _player.SetPhysicsProcess(!piloted);
        _player.SetProcessUnhandledInput(!piloted);
        _playerCamera.Current = !piloted;
        _shipTerminal.Visible = !piloted;
        _shipTerminal.CollisionLayer = piloted
            ? 0u
            : _shipTerminalCollisionLayer;
        _shipTerminal.CollisionMask = piloted
            ? 0u
            : _shipTerminalCollisionMask;
        _voyageShip.SetPilotEnabled(piloted);

        if (piloted && StageOneVoyage.Location is
            StageOneVoyageLocation.PlanetSurface or
            StageOneVoyageLocation.OrbitalStation)
        {
            _voyageShip.SetExternalCommand(ShipControlCommand.Neutral);
        }
        else if (piloted && !_voyageNavigationAssist)
        {
            _voyageShip.ClearExternalCommand();
        }

        UpdateVoyageMarkers();
        SynchronizeWorldSceneCoordinator();
        if (!piloted)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void UpdateStageOneVoyage(double delta)
    {
        if (_stageOneVoyageRuntime is null || _voyageShip is null)
        {
            return;
        }

        ConfigureVoyageShipFromDerivedStats();
        if (_orbitRuntimeActive && _orbitalDockMarker is not null)
        {
            _orbitalDockMarker.RotateY((float)(delta * 0.85));
        }

        if (_orbitRuntimeActive && _planetApproachMarker is not null)
        {
            _planetApproachMarker.RotateZ((float)(delta * 0.45));
        }

        if (!StageOneVoyage.Piloted ||
            StageOneVoyage.Location is not
                (StageOneVoyageLocation.OutboundFlight or
                 StageOneVoyageLocation.InboundFlight))
        {
            UpdateVoyageMarkers();
            return;
        }

        if (!ShipSystems.FlightReady)
        {
            _voyageShip.SetExternalCommand(new ShipControlCommand(
                0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, false, true));
        }
        else if (_voyageNavigationAssist)
        {
            Vector3 target = StageOneVoyage.Location ==
                    StageOneVoyageLocation.OutboundFlight
                ? new Vector3(
                    (float)StageOneVoyageRuntime.StationDockPositionX,
                    (float)StageOneVoyageRuntime.StationDockPositionY,
                    (float)StageOneVoyageRuntime.StationDockPositionZ)
                : new Vector3(
                    (float)StageOneVoyageRuntime.SurfacePositionX,
                    (float)StageOneVoyageRuntime.LaunchPositionY,
                    (float)StageOneVoyageRuntime.SurfacePositionZ);
            Vector3 offset = target - _voyageShip.GlobalPosition;
            float distance = offset.Length();
            if (distance > 0.25f)
            {
                _voyageShip.LookAt(target, Vector3.Up);
            }

            float approachRange = StageOneVoyage.Location ==
                    StageOneVoyageLocation.OutboundFlight
                ? (float)StageOneVoyageRuntime.DockingRangeMeters
                : (float)StageOneVoyageRuntime.LandingRangeMeters;
            float speedLimit = StageOneVoyage.Location ==
                    StageOneVoyageLocation.OutboundFlight
                ? (float)StageOneVoyageRuntime.MaximumDockingSpeed
                : (float)StageOneVoyageRuntime.MaximumLandingSpeed;
            bool braking = distance <= approachRange + 8.0f ||
                _voyageShip.Speed > speedLimit * 0.88f;
            float forward = braking ? 0.0f : 0.78f;
            _voyageShip.SetExternalCommand(new ShipControlCommand(
                forward,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                distance > 45.0f,
                braking));
        }
        else
        {
            _voyageShip.ClearExternalCommand();
        }

        Vector3 position = _voyageShip.GlobalPosition;
        Vector3 rotation = _voyageShip.Rotation;
        Vector3 velocity = _voyageShip.Velocity;
        StageOneVoyage.UpdateFlightState(
            position.X,
            position.Y,
            position.Z,
            rotation.X,
            rotation.Y,
            rotation.Z,
            velocity.X,
            velocity.Y,
            velocity.Z);
        UpdateVoyageMarkers();
    }

    private void UpdateVoyageMarkers()
    {
        if (_orbitalDockMarker is not null)
        {
            _orbitalDockMarker.Visible =
                _stageOneVoyageRuntime?.Location ==
                    StageOneVoyageLocation.OutboundFlight;
        }

        if (_planetApproachMarker is not null)
        {
            _planetApproachMarker.Visible =
                _stageOneVoyageRuntime?.Location ==
                    StageOneVoyageLocation.InboundFlight;
        }
    }

    private bool HandleStageOneVoyageInput(Key physical, Key logical)
    {
        if (_stageOneVoyageRuntime is null || !StageOneVoyage.Piloted)
        {
            return false;
        }

        if (Matches(physical, logical, Key.K))
        {
            if (StageOneVoyage.Location is
                StageOneVoyageLocation.OutboundFlight or
                StageOneVoyageLocation.InboundFlight)
            {
                _voyageNavigationAssist = !_voyageNavigationAssist;
                _status = LF("ui.voyage.nav_assist", ("state", L(_voyageNavigationAssist ? "ui.common.on" : "ui.common.off")));
                if (!_voyageNavigationAssist)
                {
                    _voyageShip?.ClearExternalCommand();
                }
            }
            else
            {
                _status = L("ui.voyage.nav_only_flight");
            }

            return true;
        }

        if (Matches(physical, logical, Key.T))
        {
            if (StageOneVoyage.Location ==
                StageOneVoyageLocation.PlanetSurface)
            {
                BeginStageOneTakeoff();
            }
            else if (StageOneVoyage.Location ==
                StageOneVoyageLocation.OrbitalStation)
            {
                BeginStageOneUndock();
            }
            else
            {
                _status = L("ui.voyage.travel_key_location");
            }

            return true;
        }

        if (Matches(physical, logical, Key.U))
        {
            _status = L("ui.voyage.land_for_management");
            return true;
        }

        if (Matches(physical, logical, Key.G))
        {
            if (_voyageShip is not null)
            {
                bool enabled = !_voyageShip.AutoStabilizationEnabled;
                _voyageShip.SetAutoStabilization(enabled);
                _status = LF("ui.voyage.stabilization", ("state", L(enabled ? "ui.common.on" : "ui.common.off")));
            }

            return true;
        }

        if (Matches(physical, logical, Key.J) ||
            Matches(physical, logical, Key.P))
        {
            _status = L("ui.voyage.exploration_unavailable");
            return true;
        }

        if (Matches(physical, logical, Key.Enter))
        {
            switch (StageOneVoyage.Location)
            {
                case StageOneVoyageLocation.OutboundFlight:
                    TryDockStageOneVoyage();
                    break;
                case StageOneVoyageLocation.OrbitalStation:
                    OpenOrbitalStationServices();
                    break;
                case StageOneVoyageLocation.InboundFlight:
                    TryLandStageOneVoyage();
                    break;
                case StageOneVoyageLocation.PlanetSurface:
                    DisembarkStageOneVoyage();
                    break;
            }

            return true;
        }

        if (Matches(physical, logical, Key.E))
        {
            if (StageOneVoyage.Location ==
                StageOneVoyageLocation.OrbitalStation)
            {
                OpenOrbitalStationServices();
                return true;
            }

            if (StageOneVoyage.Location ==
                StageOneVoyageLocation.PlanetSurface)
            {
                DisembarkStageOneVoyage();
                return true;
            }

            // Preserve the arcade controller's E roll input while flying.
            return false;
        }

        return false;
    }

    private void TryBoardStageOneVoyage(Node3D interactor)
    {
        ArgumentNullException.ThrowIfNull(interactor);
        StageOneVoyageActionResult action = StageOneVoyage.TryBoard(
            ShipSystems,
            out string result);
        _status = result;
        if (action != StageOneVoyageActionResult.Applied)
        {
            return;
        }

        _voyageNavigationAssist = false;
        ConfigureVoyageShipFromDerivedStats();
        ApplyStageOneVoyageToScene();
        _lastDomainEvent = "StageOneShipBoarded";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-112 player ship boarding PASS: " +
            $"class={ShipSystems.ShipClassId}; " +
            $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"flightReady={(ShipSystems.FlightReady ? 1 : 0)}; " +
            $"interactor={interactor.Name}; controls=T takeoff,K assist,Enter dock/land,F2 camera.");
    }

    private void BeginStageOneTakeoff()
    {
        StageOneVoyageActionResult action = StageOneVoyage.TryLaunch(
            ShipSystems,
            out string result);
        _status = result;
        if (action != StageOneVoyageActionResult.Applied)
        {
            return;
        }

        _voyageNavigationAssist = true;
        ApplyStageOneVoyageToScene();
        PublishDomainEvent(new PlanetExited(
            GalaxyNavigation.CurrentPlanetId,
            DateTimeOffset.UtcNow));
        StageOneVoyageFlightProfile profile =
            StageOneVoyageRuntime.CreateFlightProfile(ShipSystems);
        GD.Print(
            "TASK-112 player takeoff PASS: " +
            $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"acceleration={profile.Acceleration.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"maxSpeed={profile.MaxSpeed.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"maneuverYaw={profile.YawRateDegrees.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            "target=orbital_station; navigationAssist=1.");
    }

    private void TryDockStageOneVoyage()
    {
        if (_voyageShip is null)
        {
            return;
        }

        Vector3 dock = new(
            (float)StageOneVoyageRuntime.StationDockPositionX,
            (float)StageOneVoyageRuntime.StationDockPositionY,
            (float)StageOneVoyageRuntime.StationDockPositionZ);
        double distance = _voyageShip.GlobalPosition.DistanceTo(dock);
        StageOneVoyageActionResult action = StageOneVoyage.TryDock(
            ShipSystems,
            distance,
            _voyageShip.Speed,
            out string result);
        _status = result;
        if (action != StageOneVoyageActionResult.Applied)
        {
            return;
        }

        _voyageNavigationAssist = false;
        ApplyStageOneVoyageToScene();
        _lastDomainEvent = "StageOneOrbitalDocking";
        GD.Print(
            "TASK-112 player orbital docking PASS: " +
            $"distance={distance.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"speed={_voyageShip.Speed.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"dockings={StageOneVoyage.DockingCount}.");
        OpenOrbitalStationServices();
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
    }

    private void OpenOrbitalStationServices()
    {
        if (_stationServicesNpc is null || _voyageShip is null)
        {
            return;
        }

        OpenStationServices(_stationServicesNpc, _voyageShip);
        if (!_stationServicesOpen)
        {
            _stationServicesOpenedFromVoyage = false;
            return;
        }

        _stationServicesOpenedFromVoyage = true;
        _lastDomainEvent = "StageOneOrbitalStationVisited";
        _status = L("ui.voyage.station_open");
        GD.Print(
            "TASK-112 player station visit PASS: " +
            $"stationVisited={(StageOneVoyage.StationVisited ? 1 : 0)}; " +
            $"credits={StationServices.PlayerCredits}; " +
            $"market={StationServices.MarketId}; services=dialogue+buy+sell+quests.");
    }

    private void BeginStageOneUndock()
    {
        StageOneVoyageActionResult action = StageOneVoyage.TryUndock(
            ShipSystems,
            out string result);
        _status = result;
        if (action != StageOneVoyageActionResult.Applied)
        {
            return;
        }

        CloseStationServices();
        _stationServicesOpenedFromVoyage = false;
        _voyageNavigationAssist = true;
        ApplyStageOneVoyageToScene();
        _lastDomainEvent = "StageOneUndock";
        QueueCurrentSnapshot(AutosaveTrigger.Takeoff);
        GD.Print(
            "TASK-112 player undock PASS: " +
            $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            "target=planetary_landing_pad; navigationAssist=1.");
    }

    private void TryLandStageOneVoyage()
    {
        if (_voyageShip is null)
        {
            return;
        }

        Vector3 landing = new(
            (float)StageOneVoyageRuntime.SurfacePositionX,
            (float)StageOneVoyageRuntime.LaunchPositionY,
            (float)StageOneVoyageRuntime.SurfacePositionZ);
        double distance = _voyageShip.GlobalPosition.DistanceTo(landing);
        StageOneVoyageActionResult action = StageOneVoyage.TryLand(
            ShipSystems,
            distance,
            _voyageShip.Speed,
            out string result);
        _status = result;
        if (action != StageOneVoyageActionResult.Applied)
        {
            return;
        }

        _voyageNavigationAssist = false;
        ApplyStageOneVoyageToScene();
        PublishDomainEvent(new PlanetEntered(
            GalaxyNavigation.CurrentPlanetId,
            DateTimeOffset.UtcNow));
        GD.Print(
            "TASK-112 player landing PASS: " +
            $"distance={distance.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"speed={_voyageShip.Speed.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"fuel={ShipSystems.Fuel.ToString("0.###", CultureInfo.InvariantCulture)}; " +
            $"landings={StageOneVoyage.LandingCount}; loops={StageOneVoyage.CompletedLoops}.");
        if (StageOneVoyage.CompletedLoops > 0)
        {
            GD.Print(
                "TASK-112 player Stage 1 loop PASS: " +
                "repair=1; board=1; takeoff=1; station=1; return=1; " +
                $"landing=1; loops={StageOneVoyage.CompletedLoops}; persistence=queued.");
        }
    }

    private void DisembarkStageOneVoyage()
    {
        StageOneVoyageActionResult action = StageOneVoyage.TryDisembark(
            out string result);
        _status = result;
        if (action != StageOneVoyageActionResult.Applied)
        {
            return;
        }

        if (_player is not null)
        {
            _player.GlobalPosition = new Vector3(0.0f, 1.05f, -4.6f);
            _player.Rotation = Vector3.Zero;
            _player.Velocity = Vector3.Zero;
        }

        ApplyStageOneVoyageToScene();
        _lastDomainEvent = "StageOneDisembark";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
        GD.Print(
            "TASK-112 player disembark PASS: " +
            $"loops={StageOneVoyage.CompletedLoops}; " +
            $"stationVisited={(StageOneVoyage.StationVisited ? 1 : 0)}; " +
            "playerControl=restored.");
    }

    private string BuildStageOneVoyageHudLine()
    {
        if (_stageOneVoyageRuntime is null)
        {
            return L("ui.hud.voyage.unavailable");
        }

        string approach = L("ui.common.not_available");
        if (_voyageShip is not null && StageOneVoyage.Piloted)
        {
            Vector3 target = StageOneVoyage.Location ==
                    StageOneVoyageLocation.OutboundFlight
                ? new Vector3(
                    (float)StageOneVoyageRuntime.StationDockPositionX,
                    (float)StageOneVoyageRuntime.StationDockPositionY,
                    (float)StageOneVoyageRuntime.StationDockPositionZ)
                : new Vector3(
                    (float)StageOneVoyageRuntime.SurfacePositionX,
                    (float)StageOneVoyageRuntime.LaunchPositionY,
                    (float)StageOneVoyageRuntime.SurfacePositionZ);
            approach = _voyageShip.GlobalPosition.DistanceTo(target)
                .ToString("0.0", CultureInfo.InvariantCulture) + "m";
        }

        string locationKey = StageOneVoyage.Location switch
        {
            StageOneVoyageLocation.PlanetSurface => "ui.voyage.location.planet_surface",
            StageOneVoyageLocation.OutboundFlight => "ui.voyage.location.outbound_flight",
            StageOneVoyageLocation.OrbitalStation => "ui.voyage.location.orbital_station",
            StageOneVoyageLocation.InboundFlight => "ui.voyage.location.inbound_flight",
            _ => "ui.voyage.location.unknown"
        };
        return LF(
            "ui.hud.voyage.summary",
            ("location", L(locationKey)),
            ("piloted", StageOneVoyage.Piloted ? 1 : 0),
            ("checkpoint", StageOneVoyage.LastCheckpoint),
            ("loops", StageOneVoyage.CompletedLoops),
            ("visited", StageOneVoyage.StationVisited ? 1 : 0),
            ("assist", _voyageNavigationAssist ? 1 : 0),
            ("approach", approach));
    }

    private void BeginStageOneVoyageAcceptance(string directory)
    {
        string testPath = System.IO.Path.Combine(
            directory,
            "save_1.stage-one-voyage-test.db");
        _stageOneVoyageAcceptanceHud = "RUNNING";
        _stageOneVoyageAcceptanceReport = null;
        _stageOneVoyageAcceptanceTask =
            StageOneVoyageAcceptanceRunner.RunAsync(
                testPath,
                SlotId,
                ShipSystemsCatalog,
                RepairRecipe,
                _lifetimeCancellation.Token);
    }

    private void PollStageOneVoyageAcceptanceTask()
    {
        if (_stageOneVoyageAcceptanceTask is null ||
            !_stageOneVoyageAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<StageOneVoyageAcceptanceReport> task =
            _stageOneVoyageAcceptanceTask;
        _stageOneVoyageAcceptanceTask = null;
        try
        {
            StageOneVoyageAcceptanceReport report =
                task.GetAwaiter().GetResult();
            _stageOneVoyageAcceptanceReport = report;
            _stageOneVoyageAcceptanceHud = report.Passed
                ? $"PASS stats={(report.DerivedStatsApplied ? 1 : 0)}, " +
                  $"takeoff={(report.Takeoff ? 1 : 0)}, " +
                  $"dock={(report.Docking ? 1 : 0)}, " +
                  $"landing={(report.Landing ? 1 : 0)}, " +
                  $"loop={(report.LoopCompleted ? 1 : 0)}, " +
                  $"restore={(report.ColdRestore ? 1 : 0)}"
                : $"FAIL {report.Result}";
            _status = report.Result;
            string output =
                "TASK-112 Stage 1 voyage acceptance " +
                (report.Passed ? "PASS" : "FAIL") + ": " +
                $"derivedStats={(report.DerivedStatsApplied ? 1 : 0)}; " +
                $"preRepairBlocked={(report.PreRepairBlocked ? 1 : 0)}; " +
                $"takeoff={(report.Takeoff ? 1 : 0)}; " +
                $"fuelDebited={(report.FuelDebited ? 1 : 0)}; " +
                $"docking={(report.Docking ? 1 : 0)}; " +
                $"stationVisited={(report.StationVisited ? 1 : 0)}; " +
                $"undock={(report.Undock ? 1 : 0)}; " +
                $"landing={(report.Landing ? 1 : 0)}; " +
                $"loopCompleted={(report.LoopCompleted ? 1 : 0)}; " +
                $"readinessRejected={(report.ReadinessRejected ? 1 : 0)}; " +
                $"coldRestore={(report.ColdRestore ? 1 : 0)}; " +
                $"legacyFallback={(report.LegacyFallback ? 1 : 0)}; " +
                $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
                $"logWritten={(report.LogWritten ? 1 : 0)}; " +
                $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
                $"integrity={report.Diagnostics.IntegrityResult}; " +
                $"elapsedMs={report.ElapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                $"result={report.Result}";
            if (report.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }

            UpdateCombinedCatalogAndShipAcceptanceState();
        }
        catch (Exception exception)
        {
            Fail("Stage 1 voyage acceptance", exception);
        }
    }
}
