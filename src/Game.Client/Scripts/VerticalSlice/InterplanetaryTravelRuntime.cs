using System;
using System.Globalization;
using System.Linq;

public enum InterplanetaryTravelPhase
{
    Idle = 0,
    TargetSelected = 1,
    Cruising = 2
}

public enum InterplanetaryTravelActionResult
{
    Applied = 0,
    NoTarget = 1,
    SamePlanet = 2,
    NonLandable = 3,
    NotPiloted = 4,
    InvalidLocation = 5,
    FlightNotReady = 6,
    InsufficientFuel = 7,
    InvalidDistance = 8,
    AlreadyCruising = 9,
    TargetChanged = 10
}

public readonly record struct InterplanetaryGuidance(
    float Forward,
    bool Boost,
    bool Brake,
    bool ArrivalReady,
    float SpeedLimit);

public sealed record InterplanetaryTravelSnapshot(
    InterplanetaryTravelPhase Phase,
    string SourcePlanetId,
    string TargetPlanetId,
    double PlannedDistanceMeters,
    double FuelCost);

/// <summary>
/// Pure state machine for same-system planetary transfers. Godot coordinates are
/// supplied by the presentation adapter; the runtime owns validation, fuel cost,
/// guidance thresholds and the exact source/target transfer transaction.
/// </summary>
public sealed class InterplanetaryTravelRuntime
{
    public const double ArrivalRadiusMeters = 16.0;
    public const double MaximumArrivalSpeed = 11.0;
    public const double BrakingDistanceMeters = 48.0;
    public const double CruiseSpeedMetersPerSecond = 600.0;
    public const double AssumedBrakeDecelerationMetersPerSecondSquared = 38.0;
    public const double BrakingSafetyFactor = 0.72;
    public const double CruiseSpeedTolerance = 1.03;
    public const double CruiseAccelerationThreshold = 0.96;
    public const double MinimumFuelCost = 0.75;
    public const double MaximumFuelCost = 4.5;

    public InterplanetaryTravelPhase Phase { get; private set; }
        = InterplanetaryTravelPhase.Idle;

    public string SourcePlanetId { get; private set; } = string.Empty;

    public string TargetPlanetId { get; private set; } = string.Empty;

    public double PlannedDistanceMeters { get; private set; }

    public double FuelCost { get; private set; }

    public bool IsCruising => Phase == InterplanetaryTravelPhase.Cruising;

    public InterplanetaryTravelSnapshot CaptureSnapshot() => new(
        Phase,
        SourcePlanetId,
        TargetPlanetId,
        PlannedDistanceMeters,
        FuelCost);

    /// <summary>
    /// TASK-178 cross-system navigation invariant. A planetary target is scoped
    /// to the current star system and may never survive a hyperspace mutation.
    /// This method deliberately validates both the galaxy selection and this
    /// runtime's cached transaction state.
    /// </summary>
    public bool IsSelectionConsistentWith(GalaxyNavigationRuntime galaxy)
    {
        ArgumentNullException.ThrowIfNull(galaxy);
        bool currentPlanetInScope = galaxy.CurrentSystem.Planets.Any(planet =>
            string.Equals(
                planet.PlanetId,
                galaxy.CurrentPlanetId,
                StringComparison.Ordinal));
        if (!currentPlanetInScope)
        {
            return false;
        }

        if (Phase == InterplanetaryTravelPhase.Idle)
        {
            return string.IsNullOrWhiteSpace(galaxy.SelectedPlanetId) &&
                string.IsNullOrWhiteSpace(SourcePlanetId) &&
                string.IsNullOrWhiteSpace(TargetPlanetId) &&
                Math.Abs(PlannedDistanceMeters) <= double.Epsilon &&
                Math.Abs(FuelCost) <= double.Epsilon;
        }

        bool targetInScope = !string.IsNullOrWhiteSpace(TargetPlanetId) &&
            galaxy.CurrentSystem.Planets.Any(planet => string.Equals(
                planet.PlanetId,
                TargetPlanetId,
                StringComparison.Ordinal));
        bool identityMatches =
            targetInScope &&
            string.Equals(
                SourcePlanetId,
                galaxy.CurrentPlanetId,
                StringComparison.Ordinal) &&
            string.Equals(
                TargetPlanetId,
                galaxy.SelectedPlanetId,
                StringComparison.Ordinal) &&
            !string.Equals(
                SourcePlanetId,
                TargetPlanetId,
                StringComparison.Ordinal);
        if (!identityMatches)
        {
            return false;
        }

        return Phase switch
        {
            InterplanetaryTravelPhase.TargetSelected =>
                Math.Abs(PlannedDistanceMeters) <= double.Epsilon &&
                Math.Abs(FuelCost) <= double.Epsilon,
            InterplanetaryTravelPhase.Cruising =>
                double.IsFinite(PlannedDistanceMeters) &&
                PlannedDistanceMeters > ArrivalRadiusMeters &&
                double.IsFinite(FuelCost) &&
                FuelCost >= MinimumFuelCost &&
                FuelCost <= MaximumFuelCost,
            _ => false
        };
    }

    public void SynchronizeSelection(GalaxyNavigationRuntime galaxy)
    {
        ArgumentNullException.ThrowIfNull(galaxy);
        if (IsCruising)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(galaxy.SelectedPlanetId))
        {
            Reset();
            return;
        }

        Phase = InterplanetaryTravelPhase.TargetSelected;
        SourcePlanetId = galaxy.CurrentPlanetId;
        TargetPlanetId = galaxy.SelectedPlanetId;
        PlannedDistanceMeters = 0.0;
        FuelCost = 0.0;
    }

    public InterplanetaryTravelActionResult TryBeginCruise(
        GalaxyNavigationRuntime galaxy,
        StageOneVoyageRuntime voyage,
        ShipSystemsRuntime shipSystems,
        double displayDistanceMeters,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(galaxy);
        ArgumentNullException.ThrowIfNull(voyage);
        ArgumentNullException.ThrowIfNull(shipSystems);

        if (IsCruising)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.already_active");
            return InterplanetaryTravelActionResult.AlreadyCruising;
        }

        if (string.IsNullOrWhiteSpace(galaxy.SelectedPlanetId))
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.no_target");
            return InterplanetaryTravelActionResult.NoTarget;
        }

        if (string.Equals(
            galaxy.SelectedPlanetId,
            galaxy.CurrentPlanetId,
            StringComparison.Ordinal))
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.same_planet");
            return InterplanetaryTravelActionResult.SamePlanet;
        }

        GalaxyPlanetDefinition? target = galaxy.SelectedPlanet;
        if (target is null)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.target_unavailable");
            return InterplanetaryTravelActionResult.NoTarget;
        }

        if (string.Equals(target.Archetype, "gas_giant", StringComparison.Ordinal))
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.non_landable");
            return InterplanetaryTravelActionResult.NonLandable;
        }

        if (!voyage.Piloted)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.not_piloted");
            return InterplanetaryTravelActionResult.NotPiloted;
        }

        if (voyage.Location is not StageOneVoyageLocation.OutboundFlight and
            not StageOneVoyageLocation.InboundFlight)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.invalid_location");
            return InterplanetaryTravelActionResult.InvalidLocation;
        }

        if (!shipSystems.FlightReady)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.flight_not_ready");
            return InterplanetaryTravelActionResult.FlightNotReady;
        }

        if (!double.IsFinite(displayDistanceMeters) ||
            displayDistanceMeters <= ArrivalRadiusMeters)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.invalid_distance");
            return InterplanetaryTravelActionResult.InvalidDistance;
        }

        double fuelCost = CalculateFuelCost(displayDistanceMeters);
        if (!shipSystems.TryConsumeFuel(fuelCost, out string fuelResult))
        {
            result = fuelResult;
            return InterplanetaryTravelActionResult.InsufficientFuel;
        }

        Phase = InterplanetaryTravelPhase.Cruising;
        SourcePlanetId = galaxy.CurrentPlanetId;
        TargetPlanetId = target.PlanetId;
        PlannedDistanceMeters = displayDistanceMeters;
        FuelCost = fuelCost;
        result = $"interplanetary cruise started: {SourcePlanetId} -> {TargetPlanetId}; " +
            $"distance={displayDistanceMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
            $"fuel={fuelCost.ToString("0.00", CultureInfo.InvariantCulture)}";
        return InterplanetaryTravelActionResult.Applied;
    }

    public InterplanetaryGuidance BuildGuidance(
        double distanceMeters,
        double speedMetersPerSecond)
    {
        if (!IsCruising ||
            !double.IsFinite(distanceMeters) || distanceMeters < 0.0 ||
            !double.IsFinite(speedMetersPerSecond) || speedMetersPerSecond < 0.0)
        {
            return new InterplanetaryGuidance(
                0.0f, false, true, false, (float)MaximumArrivalSpeed);
        }

        bool arrivalReady = distanceMeters <= ArrivalRadiusMeters &&
            speedMetersPerSecond <= MaximumArrivalSpeed;
        double targetSpeed = CalculateSafeCruiseSpeed(distanceMeters);
        bool braking = arrivalReady ||
            distanceMeters <= BrakingDistanceMeters ||
            speedMetersPerSecond > targetSpeed * CruiseSpeedTolerance;
        bool accelerate = !braking &&
            speedMetersPerSecond < targetSpeed * CruiseAccelerationThreshold;
        float forward = accelerate ? 0.92f : 0.0f;
        bool boost = accelerate && targetSpeed > 120.0;
        return new InterplanetaryGuidance(
            forward,
            boost,
            braking,
            arrivalReady,
            (float)targetSpeed);
    }

    public static double CalculateSafeCruiseSpeed(double distanceMeters)
    {
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }

        double availableBrakingDistance = Math.Max(
            0.0,
            distanceMeters - ArrivalRadiusMeters);
        double safeSpeedSquared =
            MaximumArrivalSpeed * MaximumArrivalSpeed +
            2.0 * AssumedBrakeDecelerationMetersPerSecondSquared *
            availableBrakingDistance * BrakingSafetyFactor;
        double safeSpeed = Math.Sqrt(Math.Max(0.0, safeSpeedSquared));
        return Math.Clamp(
            safeSpeed,
            MaximumArrivalSpeed,
            CruiseSpeedMetersPerSecond);
    }

    public bool TryCompleteArrival(
        GalaxyNavigationRuntime galaxy,
        double actualDistanceMeters,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(galaxy);
        if (!IsCruising)
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.not_active");
            return false;
        }

        if (!string.Equals(
            galaxy.SelectedPlanetId,
            TargetPlanetId,
            StringComparison.Ordinal))
        {
            result = GameLocalizationService.Text("ui.interplanetary.runtime.target_changed");
            return false;
        }

        double accountedDistance = double.IsFinite(actualDistanceMeters) &&
            actualDistanceMeters >= 0.0
            ? Math.Max(actualDistanceMeters, PlannedDistanceMeters)
            : PlannedDistanceMeters;
        bool completed = galaxy.TryCompletePlanetTransfer(
            TargetPlanetId,
            accountedDistance,
            out result);
        if (completed)
        {
            Reset();
        }
        return completed;
    }

    public void Cancel(bool keepSelectedTarget, GalaxyNavigationRuntime galaxy)
    {
        ArgumentNullException.ThrowIfNull(galaxy);
        if (!keepSelectedTarget)
        {
            galaxy.ClearPlanetDestination();
        }
        Reset();
        SynchronizeSelection(galaxy);
    }

    public static double CalculateFuelCost(double displayDistanceMeters)
    {
        if (!double.IsFinite(displayDistanceMeters) || displayDistanceMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayDistanceMeters));
        }

        return Math.Clamp(
            MinimumFuelCost + displayDistanceMeters / 145.0,
            MinimumFuelCost,
            MaximumFuelCost);
    }

    private void Reset()
    {
        Phase = InterplanetaryTravelPhase.Idle;
        SourcePlanetId = string.Empty;
        TargetPlanetId = string.Empty;
        PlannedDistanceMeters = 0.0;
        FuelCost = 0.0;
    }
}
