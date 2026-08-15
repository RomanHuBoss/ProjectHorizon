using System;
using System.Globalization;
using System.Linq;

public sealed record InterplanetaryTravelAcceptanceReport(
    bool Passed,
    bool StarterPlanetCoverage,
    bool TargetSelection,
    bool TargetPersistence,
    bool FuelDebited,
    bool Guidance,
    bool WorldHandoff,
    bool Arrival,
    bool TransferPersistence,
    bool SameSystemInvariant,
    double PlannedDistanceMeters,
    double FuelCost,
    string SourcePlanetId,
    string TargetPlanetId,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS target=1 fuel=1 handoff=1 arrival=1 restore=1 {SourcePlanetId}->{TargetPlanetId}"
        : $"FAIL {Result}";

    public string BuildOutputLine() =>
        "TASK-152 interplanetary travel acceptance " +
        (Passed ? "PASS" : "FAIL") + ": " +
        $"starterPlanets={(StarterPlanetCoverage ? 4 : 0)}/4; " +
        $"targetSelection={(TargetSelection ? 1 : 0)}; " +
        $"targetPersistence={(TargetPersistence ? 1 : 0)}; " +
        $"fuelDebited={(FuelDebited ? 1 : 0)}; " +
        $"guidance={(Guidance ? 1 : 0)}; " +
        $"worldHandoff={(WorldHandoff ? 1 : 0)}; " +
        $"arrival={(Arrival ? 1 : 0)}; " +
        $"transferPersistence={(TransferPersistence ? 1 : 0)}; " +
        $"sameSystem={(SameSystemInvariant ? 1 : 0)}; " +
        $"source={SourcePlanetId}; target={TargetPlanetId}; " +
        $"distance={PlannedDistanceMeters.ToString("0.0", CultureInfo.InvariantCulture)}m; " +
        $"fuel={FuelCost.ToString("0.00", CultureInfo.InvariantCulture)}; " +
        $"result={Result}";
}

public static class InterplanetaryTravelAcceptanceRunner
{
    public static InterplanetaryTravelAcceptanceReport Run(
        ShipSystemsCatalog shipCatalog)
    {
        ArgumentNullException.ThrowIfNull(shipCatalog);
        try
        {
            GalaxyNavigationRuntime galaxy = new();
            bool starterCoverage = galaxy.CurrentSystem.Planets.Count == 4 &&
                galaxy.CurrentSystem.Planets.Count(planet =>
                    !string.Equals(planet.Archetype, "gas_giant", StringComparison.Ordinal)) == 4;
            GalaxyPlanetDefinition source = galaxy.CurrentPlanet;
            GalaxyPlanetDefinition target = galaxy.CurrentSystem.Planets
                .First(planet => !string.Equals(
                    planet.PlanetId,
                    source.PlanetId,
                    StringComparison.Ordinal));

            bool selected = galaxy.TrySelectPlanetDestination(
                target.PlanetId,
                out _);
            bool targetSelection = selected &&
                string.Equals(galaxy.CurrentPlanetId, source.PlanetId, StringComparison.Ordinal) &&
                string.Equals(galaxy.SelectedPlanetId, target.PlanetId, StringComparison.Ordinal);

            GalaxyNavigationSaveData selectedSave = galaxy.CreateSaveData();
            GalaxyNavigationRuntime selectedRestore = new(selectedSave);
            bool targetPersistence =
                string.Equals(selectedRestore.CurrentPlanetId, source.PlanetId, StringComparison.Ordinal) &&
                string.Equals(selectedRestore.SelectedPlanetId, target.PlanetId, StringComparison.Ordinal);

            ShipSystemsRuntime ship = new(shipCatalog, commissioned: true);
            StageOneVoyageRuntime voyage = new();
            bool boarded = voyage.TryBoard(ship, out _) == StageOneVoyageActionResult.Applied;
            bool launched = voyage.TryLaunch(ship, out _) == StageOneVoyageActionResult.Applied;
            double fuelBefore = ship.Fuel;
            double plannedDistance = 120.0 + Math.Abs(target.OrbitIndex - source.OrbitIndex) * 72.0;
            InterplanetaryTravelRuntime travel = new();
            travel.SynchronizeSelection(galaxy);
            bool began = travel.TryBeginCruise(
                galaxy,
                voyage,
                ship,
                plannedDistance,
                out _) == InterplanetaryTravelActionResult.Applied;
            double fuelCost = travel.FuelCost;
            bool fuelDebited = boarded && launched && began &&
                ship.Fuel < fuelBefore && fuelCost > 0.0;

            InterplanetaryGuidance far = travel.BuildGuidance(plannedDistance, 6.0);
            InterplanetaryGuidance near = travel.BuildGuidance(8.0, 5.0);
            bool guidance = far.Forward > 0.0f && !far.Brake &&
                near.Brake && near.ArrivalReady;

            string systemId = galaxy.CurrentSystem.SystemId;
            WorldSceneCoordinatorRuntime world = new(
                WorldSceneContext.Create(WorldSceneKind.Orbit, systemId, source.PlanetId));
            bool enterTransit = world.TryTransition(
                    WorldSceneContext.Create(
                        WorldSceneKind.InterplanetaryTransit,
                        systemId,
                        source.PlanetId),
                    out _) == WorldSceneTransitionResult.Applied;
            bool leaveTransit = world.TryTransition(
                    WorldSceneContext.Create(
                        WorldSceneKind.Orbit,
                        systemId,
                        target.PlanetId),
                    out _) == WorldSceneTransitionResult.Applied;
            bool worldHandoff = enterTransit && leaveTransit &&
                world.Current.Kind == WorldSceneKind.Orbit &&
                string.Equals(world.Current.PlanetId, target.PlanetId, StringComparison.Ordinal);

            bool completed = travel.TryCompleteArrival(
                galaxy,
                plannedDistance,
                out _);
            voyage.ArriveAtPlanetaryApproach();
            bool arrival = completed &&
                string.Equals(galaxy.CurrentPlanetId, target.PlanetId, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(galaxy.SelectedPlanetId) &&
                galaxy.InterplanetaryTransferCount == 1 &&
                voyage.Location == StageOneVoyageLocation.InboundFlight &&
                string.Equals(voyage.LastCheckpoint, "planet.approach", StringComparison.Ordinal);

            GalaxyNavigationSaveData completedSave = galaxy.CreateSaveData();
            GalaxyNavigationRuntime completedRestore = new(completedSave);
            bool transferPersistence =
                string.Equals(completedRestore.CurrentPlanetId, target.PlanetId, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(completedRestore.SelectedPlanetId) &&
                completedRestore.InterplanetaryTransferCount == 1 &&
                completedRestore.TotalInterplanetaryDistanceMeters >= plannedDistance;
            bool sameSystem = string.Equals(
                completedRestore.CurrentSystem.SystemId,
                systemId,
                StringComparison.Ordinal);

            bool passed = starterCoverage && targetSelection && targetPersistence &&
                fuelDebited && guidance && worldHandoff && arrival &&
                transferPersistence && sameSystem;
            string result = passed
                ? "same-system target selection, fuel-backed physical cruise contract, transactional planet handoff and exact persistence verified"
                : "one or more interplanetary travel invariants failed";
            return new InterplanetaryTravelAcceptanceReport(
                passed,
                starterCoverage,
                targetSelection,
                targetPersistence,
                fuelDebited,
                guidance,
                worldHandoff,
                arrival,
                transferPersistence,
                sameSystem,
                plannedDistance,
                began ? fuelCost : 0.0,
                source.PlanetId,
                target.PlanetId,
                result);
        }
        catch (Exception exception)
        {
            return new InterplanetaryTravelAcceptanceReport(
                false, false, false, false, false, false, false, false, false, false,
                0.0, 0.0, string.Empty, string.Empty,
                $"acceptance exception: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
