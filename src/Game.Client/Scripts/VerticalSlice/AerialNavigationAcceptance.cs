using System;
using System.Collections.Generic;
using System.Linq;

public sealed record AerialNavigationAcceptanceReport(
    int FlyingFauna,
    int NpcShips,
    int OccupiedGridCells,
    int Obstacles,
    int PointsOfInterest,
    bool FlyingFaunaCoverage,
    bool SharedSteeringRuntime,
    bool LocalSpatialGrid,
    bool SphericalObstacleAvoidance,
    bool AltitudeEnvelope,
    bool PointOfInterestSteering,
    bool ShipSteering,
    bool Pursuit,
    bool Evade,
    bool Arrive,
    bool Formation,
    bool CombatStates,
    bool ShipObstacleClearance,
    bool RuntimeSamples,
    string Result)
{
    public bool Passed =>
        FlyingFaunaCoverage &&
        SharedSteeringRuntime &&
        LocalSpatialGrid &&
        SphericalObstacleAvoidance &&
        AltitudeEnvelope &&
        PointOfInterestSteering &&
        ShipSteering &&
        Pursuit &&
        Evade &&
        Arrive &&
        Formation &&
        CombatStates &&
        ShipObstacleClearance &&
        RuntimeSamples;
}

public static class AerialNavigationAcceptanceEvaluator
{
    public static AerialNavigationAcceptanceReport Evaluate(
        AerialSteeringSnapshot before,
        AerialSteeringSnapshot after,
        IReadOnlyList<EcologyFaunaNode> fauna,
        IReadOnlyList<NpcShipNavigationNode> ships,
        bool gridProbe,
        bool obstacleProbe,
        bool poiProbe)
    {
        ArgumentNullException.ThrowIfNull(fauna);
        ArgumentNullException.ThrowIfNull(ships);

        EcologyFaunaNode[] flying = fauna
            .Where(node => string.Equals(
                node.MovementMode,
                "Flying",
                StringComparison.Ordinal))
            .ToArray();
        NpcShipNavigationDiagnostics[] shipDiagnostics = ships
            .Select(node => node.CreateDiagnostics())
            .ToArray();

        bool faunaCoverage =
            flying.Length == EcologyCatalog.ExpectedFlyingFaunaCount &&
            flying.All(node => node.AerialSteeringBound);
        bool sharedRuntime =
            after.FlyingFaunaSamples > before.FlyingFaunaSamples &&
            after.ShipSamples > before.ShipSamples;
        bool localGrid =
            gridProbe &&
            after.OccupiedCells > 0 &&
            after.GridQueries > before.GridQueries;
        bool sphericalAvoidance =
            obstacleProbe &&
            after.ObstacleCount > 0 &&
            after.ObstacleAvoidanceActivations > before.ObstacleAvoidanceActivations;
        bool altitude = flying.All(node => node.InsideFlyingAltitudeEnvelope) &&
            after.AltitudeCorrections > before.AltitudeCorrections;
        bool poi = poiProbe &&
            after.PointOfInterestCount >= 4 &&
            after.PoiSelections > before.PoiSelections;
        bool shipSteering =
            shipDiagnostics.Length == 4 &&
            shipDiagnostics.All(item => item.Active && item.SteeringSamples > 0);
        bool pursuit = after.PursuitSamples > before.PursuitSamples;
        bool evade = after.EvadeSamples > before.EvadeSamples;
        bool arrive = after.ArriveSamples > before.ArriveSamples;
        bool formation = after.FormationSamples > before.FormationSamples;
        bool combat =
            after.CombatStateTransitions > before.CombatStateTransitions &&
            shipDiagnostics.Any(item =>
                item.Role == NpcShipNavigationRole.HostileRaider &&
                item.StateTransitions > 0);
        bool clearance = shipDiagnostics.All(item =>
            item.MinimumObstacleClearance >= -0.35f);
        bool samples =
            after.FlyingFaunaSamples - before.FlyingFaunaSamples >= flying.Length &&
            after.ShipSamples - before.ShipSamples >= shipDiagnostics.Length;

        string result =
            faunaCoverage && sharedRuntime && localGrid && sphericalAvoidance &&
            altitude && poi && shipSteering && pursuit && evade && arrive &&
            formation && combat && clearance && samples
                ? "flying-fauna and NPC-ship navigation runtime verified"
                : "one or more §30.2/§30.3 navigation invariants failed";

        return new AerialNavigationAcceptanceReport(
            flying.Length,
            shipDiagnostics.Length,
            after.OccupiedCells,
            after.ObstacleCount,
            after.PointOfInterestCount,
            faunaCoverage,
            sharedRuntime,
            localGrid,
            sphericalAvoidance,
            altitude,
            poi,
            shipSteering,
            pursuit,
            evade,
            arrive,
            formation,
            combat,
            clearance,
            samples,
            result);
    }
}
