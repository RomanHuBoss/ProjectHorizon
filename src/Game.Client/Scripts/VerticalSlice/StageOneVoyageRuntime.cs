using System;
using System.Globalization;

public enum StageOneVoyageLocation
{
    PlanetSurface = 0,
    OutboundFlight = 1,
    OrbitalStation = 2,
    InboundFlight = 3
}

public enum StageOneVoyageActionResult
{
    Applied = 0,
    NotCommissioned = 1,
    FlightNotReady = 2,
    NotPiloted = 3,
    AlreadyPiloted = 4,
    InvalidLocation = 5,
    InsufficientFuel = 6,
    OutsideApproach = 7,
    TooFast = 8,
    LandingSystemOffline = 9
}

public sealed record StageOneVoyageFlightProfile(
    double Acceleration,
    double ReverseAcceleration,
    double LateralAcceleration,
    double VerticalAcceleration,
    double MaxSpeed,
    double BoostMaxSpeed,
    double PitchRateDegrees,
    double YawRateDegrees,
    double RollRateDegrees,
    double AtmosphericEfficiency);

public sealed class StageOneVoyageRuntime
{
    public const double LaunchFuelCost = 3.0;
    public const double DockFuelCost = 1.0;
    public const double UndockFuelCost = 1.0;
    public const double LandingFuelCost = 2.0;
    public const double DockingRangeMeters = 14.0;
    public const double LandingRangeMeters = 18.0;
    public const double MaximumDockingSpeed = 10.0;
    public const double MaximumLandingSpeed = 12.0;

    public const double SurfacePositionX = 0.0;
    public const double SurfacePositionY = 2.0;
    public const double SurfacePositionZ = -10.0;
    public const double LaunchPositionY = 18.0;
    public const double StationDockPositionX = 0.0;
    public const double StationDockPositionY = 35.0;
    public const double StationDockPositionZ = -1600.0;
    public const double StationUndockPositionZ = -1582.0;

    public static bool IsDockingCaptureReady(
        double distanceMeters,
        double speedMetersPerSecond) =>
        IsFiniteNonNegative(distanceMeters) &&
        distanceMeters <= DockingRangeMeters &&
        IsFiniteNonNegative(speedMetersPerSecond) &&
        speedMetersPerSecond <= MaximumDockingSpeed;

    public static bool IsLandingCaptureReady(
        double distanceMeters,
        double speedMetersPerSecond) =>
        IsFiniteNonNegative(distanceMeters) &&
        distanceMeters <= LandingRangeMeters &&
        IsFiniteNonNegative(speedMetersPerSecond) &&
        speedMetersPerSecond <= MaximumLandingSpeed;

    public const double PlanetApproachPositionY = PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters;
    public const double PlanetApproachPositionZ = -38.0;

    public StageOneVoyageRuntime(StageOneVoyageSaveData? saveData = null)
    {
        if (saveData is null)
        {
            Location = StageOneVoyageLocation.PlanetSurface;
            PositionX = SurfacePositionX;
            PositionY = SurfacePositionY;
            PositionZ = SurfacePositionZ;
            LastCheckpoint = "planet.surface";
            return;
        }

        ValidateSaveData(saveData);
        Location = saveData.Location;
        Piloted = saveData.Piloted ||
            saveData.Location != StageOneVoyageLocation.PlanetSurface;
        StationVisited = saveData.StationVisited;
        StationVisitedThisLoop = saveData.StationVisitedThisLoop;
        TakeoffCount = saveData.TakeoffCount;
        DockingCount = saveData.DockingCount;
        LandingCount = saveData.LandingCount;
        CompletedLoops = saveData.CompletedLoops;
        PositionX = saveData.PositionX;
        PositionY = saveData.PositionY;
        PositionZ = saveData.PositionZ;
        RotationX = saveData.RotationX;
        RotationY = saveData.RotationY;
        RotationZ = saveData.RotationZ;
        VelocityX = saveData.VelocityX;
        VelocityY = saveData.VelocityY;
        VelocityZ = saveData.VelocityZ;
        LastCheckpoint = saveData.LastCheckpoint;

        // TASK-178.3: saved station poses from the previous compressed orbital
        // scale must not restore the ship into the old near-surface station.
        // Normalize only checkpoint-owned poses; free-flight saves remain exact.
        if (Location == StageOneVoyageLocation.OrbitalStation)
        {
            PositionX = StationDockPositionX;
            PositionY = StationDockPositionY;
            PositionZ = StationDockPositionZ;
            RotationX = 0.0;
            RotationY = 0.0;
            RotationZ = 0.0;
            VelocityX = 0.0;
            VelocityY = 0.0;
            VelocityZ = 0.0;
        }
        else if (Location == StageOneVoyageLocation.InboundFlight &&
                 string.Equals(LastCheckpoint, "station.undocked", StringComparison.Ordinal) &&
                 Math.Abs(PositionZ) < 500.0)
        {
            PositionX = StationDockPositionX;
            PositionY = StationDockPositionY;
            PositionZ = StationUndockPositionZ;
            RotationX = 0.0;
            RotationY = Math.PI;
            RotationZ = 0.0;
            VelocityX = 0.0;
            VelocityY = 0.0;
            VelocityZ = 0.0;
        }
    }

    public StageOneVoyageLocation Location { get; private set; }

    public bool Piloted { get; private set; }

    public bool StationVisited { get; private set; }

    public bool StationVisitedThisLoop { get; private set; }

    public int TakeoffCount { get; private set; }

    public int DockingCount { get; private set; }

    public int LandingCount { get; private set; }

    public int CompletedLoops { get; private set; }

    public double PositionX { get; private set; }

    public double PositionY { get; private set; }

    public double PositionZ { get; private set; }

    public double RotationX { get; private set; }

    public double RotationY { get; private set; }

    public double RotationZ { get; private set; }

    public double VelocityX { get; private set; }

    public double VelocityY { get; private set; }

    public double VelocityZ { get; private set; }

    public string LastCheckpoint { get; private set; }

    public bool LoopCompleted => CompletedLoops > 0;

    public bool IsPlanetarySurfaceApproach =>
        Location == StageOneVoyageLocation.InboundFlight &&
        string.Equals(LastCheckpoint, "planet.approach", StringComparison.Ordinal);

    public string BuildSummary()
    {
        return $"location={Location}; piloted={(Piloted ? 1 : 0)}; " +
            $"stationVisited={(StationVisited ? 1 : 0)}; " +
            $"takeoffs={TakeoffCount}; docks={DockingCount}; " +
            $"landings={LandingCount}; loops={CompletedLoops}; " +
            $"checkpoint={LastCheckpoint}";
    }

    public StageOneVoyageActionResult TryBoard(
        ShipSystemsRuntime shipSystems,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        if (!shipSystems.Commissioned)
        {
            result = GameLocalizationService.Text("ui.voyage.ship_not_commissioned");
            return StageOneVoyageActionResult.NotCommissioned;
        }

        if (!shipSystems.FlightReady)
        {
            result = GameLocalizationService.Text("ui.voyage.ship_not_ready");
            return StageOneVoyageActionResult.FlightNotReady;
        }

        if (Location != StageOneVoyageLocation.PlanetSurface)
        {
            result = GameLocalizationService.Format("ui.voyage.cannot_board", ("location", Location));
            return StageOneVoyageActionResult.InvalidLocation;
        }

        if (Piloted)
        {
            result = GameLocalizationService.Text("ui.voyage.already_piloted");
            return StageOneVoyageActionResult.AlreadyPiloted;
        }

        Piloted = true;
        LastCheckpoint = "planet.boarded";
        result = GameLocalizationService.Text("ui.voyage.boarded");
        return StageOneVoyageActionResult.Applied;
    }

    public StageOneVoyageActionResult TryLaunch(
        ShipSystemsRuntime shipSystems,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        StageOneVoyageActionResult readiness = ValidatePilotedFlightReadiness(
            shipSystems,
            out result);
        if (readiness != StageOneVoyageActionResult.Applied)
        {
            return readiness;
        }

        if (Location != StageOneVoyageLocation.PlanetSurface)
        {
            result = GameLocalizationService.Format("ui.voyage.takeoff_requires", ("location", Location));
            return StageOneVoyageActionResult.InvalidLocation;
        }

        if (shipSystems.GetSystemHealth("ship.system.landing") <= 0.0)
        {
            result = GameLocalizationService.Text("ui.voyage.landing_offline");
            return StageOneVoyageActionResult.LandingSystemOffline;
        }

        if (!shipSystems.TryConsumeFuel(LaunchFuelCost, out string fuelResult))
        {
            result = fuelResult;
            return StageOneVoyageActionResult.InsufficientFuel;
        }

        Location = StageOneVoyageLocation.OutboundFlight;
        StationVisitedThisLoop = false;
        TakeoffCount++;
        SetPose(
            SurfacePositionX,
            LaunchPositionY,
            SurfacePositionZ,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0);
        LastCheckpoint = "flight.outbound";
        result = GameLocalizationService.Format("ui.voyage.takeoff_complete", ("fuel", shipSystems.Fuel.ToString("0.#", CultureInfo.InvariantCulture)));
        return StageOneVoyageActionResult.Applied;
    }

    public StageOneVoyageActionResult TryDock(
        ShipSystemsRuntime shipSystems,
        double distanceMeters,
        double speedMetersPerSecond,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        StageOneVoyageActionResult readiness = ValidatePilotedFlightReadiness(
            shipSystems,
            out result);
        if (readiness != StageOneVoyageActionResult.Applied)
        {
            return readiness;
        }

        if (Location != StageOneVoyageLocation.OutboundFlight)
        {
            result = GameLocalizationService.Format("ui.voyage.docking_requires", ("location", Location));
            return StageOneVoyageActionResult.InvalidLocation;
        }

        if (!IsFiniteNonNegative(distanceMeters) ||
            distanceMeters > DockingRangeMeters)
        {
            result = GameLocalizationService.Format("ui.voyage.station_out_range", ("distance", distanceMeters.ToString("0.0", CultureInfo.InvariantCulture)));
            return StageOneVoyageActionResult.OutsideApproach;
        }

        if (!IsFiniteNonNegative(speedMetersPerSecond) ||
            speedMetersPerSecond > MaximumDockingSpeed)
        {
            result = GameLocalizationService.Format("ui.voyage.speed_high", ("speed", speedMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture)));
            return StageOneVoyageActionResult.TooFast;
        }

        if (!shipSystems.TryConsumeFuel(DockFuelCost, out string fuelResult))
        {
            result = fuelResult;
            return StageOneVoyageActionResult.InsufficientFuel;
        }

        Location = StageOneVoyageLocation.OrbitalStation;
        StationVisited = true;
        StationVisitedThisLoop = true;
        DockingCount++;
        SetPose(
            StationDockPositionX,
            StationDockPositionY,
            StationDockPositionZ,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0);
        LastCheckpoint = "station.docked";
        result = GameLocalizationService.Format("ui.voyage.docked", ("fuel", shipSystems.Fuel.ToString("0.#", CultureInfo.InvariantCulture)));
        return StageOneVoyageActionResult.Applied;
    }

    public void ArriveAtOrbitalStationFromHyperspace()
    {
        if (!Piloted)
        {
            throw new InvalidOperationException(
                "Hyperspace arrival requires a piloted ship.");
        }

        Location = StageOneVoyageLocation.OrbitalStation;
        StationVisited = true;
        StationVisitedThisLoop = true;
        SetPose(
            StationDockPositionX,
            StationDockPositionY,
            StationDockPositionZ,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0);
        LastCheckpoint = "hyperspace.arrival";
    }

    public void ArriveAtPlanetaryApproach()
    {
        if (!Piloted)
        {
            throw new InvalidOperationException(
                "Planetary approach requires a piloted ship.");
        }

        if (Location is not StageOneVoyageLocation.OutboundFlight and
            not StageOneVoyageLocation.InboundFlight)
        {
            throw new InvalidOperationException(
                "Planetary approach requires orbital flight.");
        }

        Location = StageOneVoyageLocation.InboundFlight;
        SetPose(
            SurfacePositionX,
            PlanetApproachPositionY,
            PlanetApproachPositionZ,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0);
        LastCheckpoint = "planet.approach";
    }

    public StageOneVoyageActionResult TryUndock(
        ShipSystemsRuntime shipSystems,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        StageOneVoyageActionResult readiness = ValidatePilotedFlightReadiness(
            shipSystems,
            out result);
        if (readiness != StageOneVoyageActionResult.Applied)
        {
            return readiness;
        }

        if (Location != StageOneVoyageLocation.OrbitalStation)
        {
            result = GameLocalizationService.Format("ui.voyage.undock_requires", ("location", Location));
            return StageOneVoyageActionResult.InvalidLocation;
        }

        if (!shipSystems.TryConsumeFuel(UndockFuelCost, out string fuelResult))
        {
            result = fuelResult;
            return StageOneVoyageActionResult.InsufficientFuel;
        }

        Location = StageOneVoyageLocation.InboundFlight;
        SetPose(
            StationDockPositionX,
            StationDockPositionY,
            StationUndockPositionZ,
            0.0,
            Math.PI,
            0.0,
            0.0,
            0.0,
            0.0);
        LastCheckpoint = "flight.inbound";
        result = GameLocalizationService.Format("ui.voyage.undocked", ("fuel", shipSystems.Fuel.ToString("0.#", CultureInfo.InvariantCulture)));
        return StageOneVoyageActionResult.Applied;
    }

    public StageOneVoyageActionResult TryLand(
        ShipSystemsRuntime shipSystems,
        double distanceMeters,
        double speedMetersPerSecond,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        StageOneVoyageActionResult readiness = ValidatePilotedFlightReadiness(
            shipSystems,
            out result);
        if (readiness != StageOneVoyageActionResult.Applied)
        {
            return readiness;
        }

        if (Location != StageOneVoyageLocation.InboundFlight)
        {
            result = GameLocalizationService.Format("ui.voyage.landing_requires", ("location", Location));
            return StageOneVoyageActionResult.InvalidLocation;
        }

        if (shipSystems.GetSystemHealth("ship.system.landing") <= 0.0)
        {
            result = GameLocalizationService.Text("ui.voyage.landing_offline");
            return StageOneVoyageActionResult.LandingSystemOffline;
        }

        if (!IsFiniteNonNegative(distanceMeters) ||
            distanceMeters > LandingRangeMeters)
        {
            result = GameLocalizationService.Format("ui.voyage.pad_out_range", ("distance", distanceMeters.ToString("0.0", CultureInfo.InvariantCulture)));
            return StageOneVoyageActionResult.OutsideApproach;
        }

        if (!IsFiniteNonNegative(speedMetersPerSecond) ||
            speedMetersPerSecond > MaximumLandingSpeed)
        {
            result = GameLocalizationService.Format("ui.voyage.landing_speed_high", ("speed", speedMetersPerSecond.ToString("0.0", CultureInfo.InvariantCulture)));
            return StageOneVoyageActionResult.TooFast;
        }

        if (!shipSystems.TryConsumeFuel(LandingFuelCost, out string fuelResult))
        {
            result = fuelResult;
            return StageOneVoyageActionResult.InsufficientFuel;
        }

        Location = StageOneVoyageLocation.PlanetSurface;
        LandingCount++;
        if (StationVisitedThisLoop)
        {
            CompletedLoops++;
            StationVisitedThisLoop = false;
        }

        SetPose(
            SurfacePositionX,
            SurfacePositionY,
            SurfacePositionZ,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0);
        LastCheckpoint = "planet.landed";
        result = GameLocalizationService.Format("ui.voyage.landing_complete", ("loops", CompletedLoops), ("fuel", shipSystems.Fuel.ToString("0.#", CultureInfo.InvariantCulture)));
        return StageOneVoyageActionResult.Applied;
    }

    public StageOneVoyageActionResult TryDisembark(out string result)
    {
        if (!Piloted)
        {
            result = GameLocalizationService.Text("ui.voyage.not_piloted");
            return StageOneVoyageActionResult.NotPiloted;
        }

        if (Location != StageOneVoyageLocation.PlanetSurface)
        {
            result = GameLocalizationService.Format("ui.voyage.disembark_requires", ("location", Location));
            return StageOneVoyageActionResult.InvalidLocation;
        }

        Piloted = false;
        LastCheckpoint = "planet.surface";
        result = GameLocalizationService.Text("ui.voyage.disembarked");
        return StageOneVoyageActionResult.Applied;
    }

    public void UpdateFlightState(
        double positionX,
        double positionY,
        double positionZ,
        double rotationX,
        double rotationY,
        double rotationZ,
        double velocityX,
        double velocityY,
        double velocityZ)
    {
        if (Location is not StageOneVoyageLocation.OutboundFlight and
            not StageOneVoyageLocation.InboundFlight)
        {
            return;
        }

        SetPose(
            positionX,
            positionY,
            positionZ,
            rotationX,
            rotationY,
            rotationZ,
            velocityX,
            velocityY,
            velocityZ);
    }

    public StageOneVoyageSaveData CreateSaveData()
    {
        return new StageOneVoyageSaveData(
            Location,
            Piloted,
            StationVisited,
            StationVisitedThisLoop,
            TakeoffCount,
            DockingCount,
            LandingCount,
            CompletedLoops,
            PositionX,
            PositionY,
            PositionZ,
            RotationX,
            RotationY,
            RotationZ,
            VelocityX,
            VelocityY,
            VelocityZ,
            LastCheckpoint);
    }

    public static StageOneVoyageFlightProfile CreateFlightProfile(
        ShipSystemsRuntime shipSystems)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        ShipEffectiveStats stats = shipSystems.GetEffectiveStats();
        double maneuverability = Math.Clamp(stats.Maneuverability, 1.0, 120.0);
        double acceleration = Math.Clamp(stats.Acceleration, 4.0, 80.0);
        double maxSpeed = Math.Clamp(stats.MaxSpeed, 20.0, 180.0);
        return new StageOneVoyageFlightProfile(
            acceleration,
            Math.Max(4.0, acceleration * 0.62),
            Math.Max(4.0, acceleration * 0.55),
            Math.Max(4.0, acceleration * 0.50),
            maxSpeed,
            maxSpeed * 1.45,
            35.0 + maneuverability * 0.65,
            38.0 + maneuverability * 0.68,
            45.0 + maneuverability * 0.82,
            stats.AtmosphericEfficiency);
    }

    private StageOneVoyageActionResult ValidatePilotedFlightReadiness(
        ShipSystemsRuntime shipSystems,
        out string result)
    {
        if (!Piloted)
        {
            result = GameLocalizationService.Text("ui.voyage.not_piloted");
            return StageOneVoyageActionResult.NotPiloted;
        }

        if (!shipSystems.Commissioned)
        {
            result = GameLocalizationService.Text("ui.voyage.ship_not_commissioned");
            return StageOneVoyageActionResult.NotCommissioned;
        }

        if (!shipSystems.FlightReady)
        {
            result = GameLocalizationService.Text("ui.voyage.ship_not_ready");
            return StageOneVoyageActionResult.FlightNotReady;
        }

        result = GameLocalizationService.Text("ui.voyage.ready");
        return StageOneVoyageActionResult.Applied;
    }

    private void SetPose(
        double positionX,
        double positionY,
        double positionZ,
        double rotationX,
        double rotationY,
        double rotationZ,
        double velocityX,
        double velocityY,
        double velocityZ)
    {
        double[] values =
        {
            positionX,
            positionY,
            positionZ,
            rotationX,
            rotationY,
            rotationZ,
            velocityX,
            velocityY,
            velocityZ
        };
        foreach (double value in values)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > 1_000_000.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionX),
                    "Voyage pose contains a non-finite or unreasonable value.");
            }
        }

        PositionX = positionX;
        PositionY = positionY;
        PositionZ = positionZ;
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
        VelocityX = velocityX;
        VelocityY = velocityY;
        VelocityZ = velocityZ;
    }

    private static void ValidateSaveData(StageOneVoyageSaveData saveData)
    {
        if (!Enum.IsDefined(typeof(StageOneVoyageLocation), saveData.Location) ||
            saveData.TakeoffCount < 0 ||
            saveData.DockingCount < 0 ||
            saveData.LandingCount < 0 ||
            saveData.CompletedLoops < 0 ||
            saveData.DockingCount > saveData.TakeoffCount ||
            saveData.LandingCount > saveData.TakeoffCount ||
            saveData.CompletedLoops > saveData.LandingCount ||
            saveData.LastCheckpoint is null ||
            string.IsNullOrWhiteSpace(saveData.LastCheckpoint) ||
            saveData.LastCheckpoint.Length > 64)
        {
            throw new InvalidOperationException(
                "Stage 1 voyage save data contains invalid counters or identity.");
        }

        if (saveData.Location != StageOneVoyageLocation.PlanetSurface &&
            !saveData.Piloted)
        {
            throw new InvalidOperationException(
                "An active Stage 1 voyage must be piloted.");
        }

        if (saveData.StationVisitedThisLoop &&
            !saveData.StationVisited)
        {
            throw new InvalidOperationException(
                "Current-loop station visit requires persistent station visit.");
        }

        double[] values =
        {
            saveData.PositionX,
            saveData.PositionY,
            saveData.PositionZ,
            saveData.RotationX,
            saveData.RotationY,
            saveData.RotationZ,
            saveData.VelocityX,
            saveData.VelocityY,
            saveData.VelocityZ
        };
        foreach (double value in values)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > 1_000_000.0)
            {
                throw new InvalidOperationException(
                    "Stage 1 voyage save data contains an invalid pose.");
            }
        }
    }

    private static bool IsFiniteNonNegative(double value)
    {
        return double.IsFinite(value) && value >= 0.0;
    }
}
