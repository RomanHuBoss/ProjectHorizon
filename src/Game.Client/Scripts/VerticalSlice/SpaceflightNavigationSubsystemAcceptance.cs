using System;
using System.Linq;

public sealed record SpaceflightNavigationSubsystemModelAcceptanceReport(
    bool Passed,
    int ContractsPassed,
    int ContractsTotal,
    bool ShipSystemsContract,
    bool VoyageContract,
    bool GalaxyContract,
    bool StarSystemContract,
    bool InterplanetaryContract,
    bool WorldSceneContract,
    bool ReadinessChain,
    bool FuelChain,
    bool TransitionChain,
    bool PersistenceChain,
    bool NavigationIdentity,
    bool BoundedResidency,
    string Result)
{
    public string BuildSummary() =>
        $"contracts={ContractsPassed}/{ContractsTotal}; readiness={(ReadinessChain ? 1 : 0)}; " +
        $"fuel={(FuelChain ? 1 : 0)}; transitions={(TransitionChain ? 1 : 0)}; " +
        $"persistence={(PersistenceChain ? 1 : 0)}; identity={(NavigationIdentity ? 1 : 0)}; " +
        $"bounded={(BoundedResidency ? 1 : 0)}";
}

/// <summary>
/// TASK-178 model-level closure of the complete spaceflight/navigation stack.
/// The runner composes the normative acceptance reports already produced by
/// TASK-110/112/114/128/148/152 and then verifies the cross-contract chains
/// that no individual acceptance owns by itself.
/// </summary>
public static class SpaceflightNavigationSubsystemAcceptanceRunner
{
    public const int ExpectedContractCount = 6;

    public static SpaceflightNavigationSubsystemModelAcceptanceReport Run(
        ShipSystemsAcceptanceReport ship,
        StageOneVoyageAcceptanceReport voyage,
        GalaxyNavigationAcceptanceReport galaxy,
        StarSystemSimulationAcceptanceReport starSystem,
        InterplanetaryTravelAcceptanceReport interplanetary,
        WorldSceneCoordinatorAcceptanceReport worldScene)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(voyage);
        ArgumentNullException.ThrowIfNull(galaxy);
        ArgumentNullException.ThrowIfNull(starSystem);
        ArgumentNullException.ThrowIfNull(interplanetary);
        ArgumentNullException.ThrowIfNull(worldScene);

        bool[] contracts =
        {
            ship.Passed,
            voyage.Passed,
            galaxy.Passed,
            starSystem.Passed,
            interplanetary.Passed,
            worldScene.Passed
        };
        int contractsPassed = contracts.Count(value => value);

        bool readinessChain =
            ship.FlightReadiness &&
            ship.HyperspaceReadiness &&
            ship.PreRepairBlocked &&
            ship.PreRepairFlightReady &&
            ship.CommissionTransition &&
            ship.PostRepairFlightReady &&
            voyage.PreRepairBlocked &&
            voyage.ReadinessRejected &&
            galaxy.Preconditions;

        bool fuelChain =
            ship.FuelLifecycle &&
            voyage.FuelDebited &&
            galaxy.FuelDebited &&
            interplanetary.FuelDebited;

        bool transitionChain =
            voyage.Takeoff &&
            voyage.Docking &&
            voyage.Undock &&
            voyage.Landing &&
            voyage.LoopCompleted &&
            galaxy.HyperspaceJump &&
            starSystem.SystemTransition &&
            interplanetary.WorldHandoff &&
            interplanetary.Arrival &&
            worldScene.TransitionGraph &&
            worldScene.LiveTransitionPath &&
            worldScene.HyperspaceSystemChange;

        bool persistenceChain =
            ship.ColdRestore &&
            ship.ExactRoundTrip &&
            voyage.ColdRestore &&
            voyage.ExactRoundTrip &&
            galaxy.VisitedPersistence &&
            galaxy.ColdRestore &&
            galaxy.ExactRoundTrip &&
            interplanetary.TargetPersistence &&
            interplanetary.TransferPersistence &&
            worldScene.TransactionalSwap &&
            worldScene.StateRestored;

        bool navigationIdentity =
            galaxy.DeterministicGeneration &&
            galaxy.CoordinateHierarchy &&
            galaxy.RoutePlanning &&
            galaxy.Stress100 &&
            starSystem.DeterministicGeneration &&
            starSystem.BodyCoverage &&
            starSystem.AnalyticOrbits &&
            starSystem.RepresentationLevels &&
            starSystem.SingleDetailedPlanet &&
            interplanetary.StarterPlanetCoverage &&
            interplanetary.SameSystemInvariant;

        bool boundedResidency =
            starSystem.SurfaceActivation &&
            starSystem.ActivationPipeline &&
            worldScene.SingleLiveScene &&
            worldScene.LiveContextMatch &&
            worldScene.ResidencyPolicy &&
            worldScene.MaxHostChildren == 1;

        bool passed =
            contractsPassed == ExpectedContractCount &&
            readinessChain &&
            fuelChain &&
            transitionChain &&
            persistenceChain &&
            navigationIdentity &&
            boundedResidency;

        return new SpaceflightNavigationSubsystemModelAcceptanceReport(
            passed,
            contractsPassed,
            ExpectedContractCount,
            ship.Passed,
            voyage.Passed,
            galaxy.Passed,
            starSystem.Passed,
            interplanetary.Passed,
            worldScene.Passed,
            readinessChain,
            fuelChain,
            transitionChain,
            persistenceChain,
            navigationIdentity,
            boundedResidency,
            passed
                ? "ship readiness, voyage, planetary transfer, hyperspace, star-system simulation and world residency form one coherent navigation contract"
                : BuildFailureSummary(
                    ship.Passed,
                    voyage.Passed,
                    galaxy.Passed,
                    starSystem.Passed,
                    interplanetary.Passed,
                    worldScene.Passed,
                    readinessChain,
                    fuelChain,
                    transitionChain,
                    persistenceChain,
                    navigationIdentity,
                    boundedResidency));
    }

    private static string BuildFailureSummary(
        bool ship,
        bool voyage,
        bool galaxy,
        bool starSystem,
        bool interplanetary,
        bool worldScene,
        bool readiness,
        bool fuel,
        bool transitions,
        bool persistence,
        bool identity,
        bool bounded)
    {
        return "failed=" + string.Join(
            ",",
            new[]
            {
                ("ship", ship),
                ("voyage", voyage),
                ("galaxy", galaxy),
                ("star-system", starSystem),
                ("interplanetary", interplanetary),
                ("world-scene", worldScene),
                ("readiness-chain", readiness),
                ("fuel-chain", fuel),
                ("transition-chain", transitions),
                ("persistence-chain", persistence),
                ("navigation-identity", identity),
                ("bounded-residency", bounded)
            }
            .Where(pair => !pair.Item2)
            .Select(pair => pair.Item1));
    }
}
