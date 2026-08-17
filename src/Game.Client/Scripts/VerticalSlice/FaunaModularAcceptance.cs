using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public sealed record FaunaModularDiagnostics(
    int ActiveNodes,
    int CompatibleMorphologies,
    int GroundNodes,
    int GroundNavigationBound,
    int VisualInterpolationFrames,
    int FlockUpdatePasses,
    int StatisticalPopulation,
    int StatisticalSpecies,
    bool NavigationExpected);

public sealed record FaunaModularAcceptanceReport(
    bool Passed,
    bool SkeletonFamilies,
    bool ModuleCompatibility,
    bool ProceduralVariation,
    bool HierarchicalUtility,
    bool SteeringStack,
    bool TieredSimulation,
    bool StatisticalFarSimulation,
    bool VisualInterpolation,
    bool LiveBinding,
    int BodyPlans,
    int States,
    int ActiveNodes,
    int StatisticalPopulation,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-198 modular fauna acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"skeletons={(SkeletonFamilies ? 1 : 0)}; compatible={(ModuleCompatibility ? 1 : 0)}; " +
        $"variation={(ProceduralVariation ? 1 : 0)}; hfsmUtility={(HierarchicalUtility ? 1 : 0)}; " +
        $"steering={(SteeringStack ? 1 : 0)}; tiers={(TieredSimulation ? 1 : 0)}; " +
        $"statistical={(StatisticalFarSimulation ? 1 : 0)}; interpolation={(VisualInterpolation ? 1 : 0)}; " +
        $"bodyPlans={BodyPlans}/6; states={States}/11; active={ActiveNodes}; far={StatisticalPopulation}; " +
        $"live={(LiveBinding ? 1 : 0)}; result={Result}";
}

public static class FaunaModularAcceptanceRunner
{
    private static readonly string[] RequiredStates =
    {
        "Idle", "Wander", "Graze", "Drink", "Sleep", "Investigate",
        "Flee", "Threaten", "Attack", "ReturnToTerritory", "FollowGroup"
    };

    public static FaunaModularAcceptanceReport Evaluate(
        EcologyCatalog catalog,
        EcologyPlan plan,
        FaunaModularDiagnostics live)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(live);

        string[] bodyPlans = catalog.Fauna.Values
            .Select(definition => definition.BodyPlan)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        FaunaSkeletonDefinition[] skeletons = FaunaBodyPlanRuntime.BodyPlans
            .Select(FaunaBodyPlanRuntime.GetSkeleton)
            .ToArray();
        bool skeletonFamilies = bodyPlans.Length == 6 &&
            skeletons.Length == 6 &&
            skeletons.Select(item => item.SkeletonId).Distinct(StringComparer.Ordinal).Count() == 6 &&
            skeletons.All(item => item.Joints.Count >= 7);

        FaunaMorphologyProfile[] profilesA = catalog.Fauna.Values
            .OrderBy(definition => definition.FaunaId, StringComparer.Ordinal)
            .Select(definition => FaunaBodyPlanRuntime.Build(
                definition,
                $"acceptance.{definition.FaunaId}.a"))
            .ToArray();
        FaunaMorphologyProfile[] profilesARepeat = catalog.Fauna.Values
            .OrderBy(definition => definition.FaunaId, StringComparer.Ordinal)
            .Select(definition => FaunaBodyPlanRuntime.Build(
                definition,
                $"acceptance.{definition.FaunaId}.a"))
            .ToArray();
        FaunaMorphologyProfile[] profilesB = catalog.Fauna.Values
            .OrderBy(definition => definition.FaunaId, StringComparer.Ordinal)
            .Select(definition => FaunaBodyPlanRuntime.Build(
                definition,
                $"acceptance.{definition.FaunaId}.b"))
            .ToArray();
        bool moduleCompatibility = profilesA.All(FaunaBodyPlanRuntime.IsCompatible) &&
            profilesA.SequenceEqual(profilesARepeat) &&
            FaunaBodyPlanRuntime.CountCompatibleCatalogDefinitions(catalog) == catalog.Fauna.Count;
        int variantPairs = 0;
        for (int index = 0; index < profilesA.Length && index < profilesB.Length; index++)
        {
            if (!profilesA[index].Modules.SequenceEqual(profilesB[index].Modules) ||
                Math.Abs(profilesA[index].WidthScale - profilesB[index].WidthScale) > 0.001)
            {
                variantPairs++;
            }
        }
        bool proceduralVariation = variantPairs >= 8;

        EcologyFaunaDefinition attacker = catalog.Fauna.Values.First(definition =>
            definition.Aggression >= 0.60 &&
            definition.Behaviors.Contains("Attack", StringComparer.Ordinal));
        EcologyFaunaDefinition grazer = catalog.Fauna.Values.First(definition =>
            definition.Diet == "Herbivore" &&
            definition.Behaviors.Contains("Graze", StringComparer.Ordinal));
        EcologyFaunaDefinition follower = catalog.Fauna.Values.First(definition =>
            definition.Behaviors.Contains("FollowGroup", StringComparer.Ordinal));
        bool stateCoverage = RequiredStates.All(state => catalog.Fauna.Values.Any(definition =>
            definition.Behaviors.Contains(state, StringComparer.Ordinal)));
        bool hierarchicalUtility = stateCoverage &&
            Enum.GetValues<FaunaBehaviorLayer>().Length == 6 &&
            FaunaBehaviorRuntime.ScoreBehaviors(attacker, new EcologyBehaviorContext(
                4.0, 0.2, 0.2, 0.2, 4.0, 1.0, false, true)).Count >= 4 &&
            FaunaBehaviorRuntime.SelectBehavior(attacker, new EcologyBehaviorContext(
                4.0, 0.2, 0.2, 0.2, 4.0, 1.0, false, true)) == "Attack" &&
            FaunaBehaviorRuntime.SelectBehavior(grazer, new EcologyBehaviorContext(
                30.0, 0.9, 0.2, 0.2, 4.0, 1.0, false, false)) == "Graze" &&
            FaunaBehaviorRuntime.SelectBehavior(follower, new EcologyBehaviorContext(
                30.0, 0.1, 0.1, 0.1, 20.0, 1.0, false, false)) == "FollowGroup";

        FaunaFlockSample self = new("a", "fauna.test", Vector3.Zero, Vector3.Forward, true);
        FaunaFlockSteering boids = FaunaFlockRuntime.Compute(
            self,
            new[]
            {
                self,
                new FaunaFlockSample("b", "fauna.test", new Vector3(1.0f, 0.0f, 0.0f), Vector3.Forward, true),
                new FaunaFlockSample("c", "fauna.test", new Vector3(-2.0f, 0.0f, 1.0f), Vector3.Forward, true)
            });
        bool navigationLive = !live.NavigationExpected || live.GroundNavigationBound > 0;
        bool steeringStack = boids.Neighbors == 2 && boids.Combined.LengthSquared() > 0.0001f &&
            live.FlockUpdatePasses > 0 && navigationLive;

        bool tieredSimulation =
            FaunaBehaviorRuntime.ResolveSimulationTier(8.0) == FaunaSimulationTier.Near &&
            FaunaBehaviorRuntime.ResolveSimulationTier(40.0) == FaunaSimulationTier.MidHigh &&
            FaunaBehaviorRuntime.ResolveSimulationTier(100.0) == FaunaSimulationTier.MidLow &&
            FaunaBehaviorRuntime.ResolveSimulationTier(180.0) == FaunaSimulationTier.Statistical &&
            Math.Abs(FaunaBehaviorRuntime.GetDecisionFrequencyHz(8.0) - 10.0) < 0.001 &&
            Math.Abs(FaunaBehaviorRuntime.GetDecisionFrequencyHz(40.0) - 5.0) < 0.001 &&
            Math.Abs(FaunaBehaviorRuntime.GetDecisionFrequencyHz(100.0) - 2.0) < 0.001 &&
            Math.Abs(FaunaBehaviorRuntime.GetDecisionFrequencyHz(180.0)) < 0.001;

        FaunaStatisticalSimulationRuntime far = new(plan.SimplifiedFauna);
        far.Tick(4.1);
        FaunaStatisticalSnapshot statistical = far.CreateSnapshot();
        bool statisticalFar = statistical.Ticks >= 2 &&
            statistical.Population == plan.SimplifiedFauna.Count &&
            statistical.Species > 0 &&
            statistical.MeanActivity is > 0.0 and <= 1.0 &&
            live.StatisticalPopulation == plan.SimplifiedFauna.Count &&
            live.StatisticalSpecies > 0;
        bool visualInterpolation = live.VisualInterpolationFrames > 0;
        bool liveBinding = live.ActiveNodes == plan.ActiveFauna.Count &&
            live.CompatibleMorphologies == live.ActiveNodes &&
            live.GroundNodes > 0;
        bool passed = skeletonFamilies && moduleCompatibility && proceduralVariation &&
            hierarchicalUtility && steeringStack && tieredSimulation && statisticalFar &&
            visualInterpolation && liveBinding;
        return new FaunaModularAcceptanceReport(
            passed,
            skeletonFamilies,
            moduleCompatibility,
            proceduralVariation,
            hierarchicalUtility,
            steeringStack,
            tieredSimulation,
            statisticalFar,
            visualInterpolation,
            liveBinding,
            bodyPlans.Length,
            RequiredStates.Length,
            live.ActiveNodes,
            live.StatisticalPopulation,
            passed
                ? "spec section 12 fixed skeleton families, compatible modular morphology, HFSM utility AI, navmesh/boids steering and 10/5/2/statistical tiers verified"
                : "one or more modular fauna invariants failed");
    }
}
