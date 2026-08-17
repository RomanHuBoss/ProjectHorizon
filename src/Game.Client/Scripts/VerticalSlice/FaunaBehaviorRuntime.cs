using System;
using System.Collections.Generic;
using System.Linq;

public enum FaunaSimulationTier
{
    Near = 0,
    MidHigh = 1,
    MidLow = 2,
    Statistical = 3
}

public enum FaunaBehaviorLayer
{
    Survival = 0,
    Territory = 1,
    Needs = 2,
    Social = 3,
    Awareness = 4,
    Ambient = 5
}

public sealed record FaunaUtilityScore(
    string State,
    FaunaBehaviorLayer Layer,
    double Score);

/// <summary>
/// TASK-198 / Technical Specification sections 12.2-12.3.
/// Hierarchical state selection uses utility scores inside ordered layers.
/// Decision frequency is distance-tiered while visual interpolation remains a
/// per-frame responsibility of EcologyFaunaNode.
/// </summary>
public static class FaunaBehaviorRuntime
{
    public const double NearDistanceMeters = 25.0;
    public const double MidHighDistanceMeters = 70.0;
    public const double MidLowDistanceMeters = 150.0;
    public const double NearFrequencyHz = SystemFrequencyPolicy.NearbyAiHz;
    public const double MidHighFrequencyHz = 5.0;
    public const double MidLowFrequencyHz = SystemFrequencyPolicy.DistantAiHz;
    public const double FarStatisticalFrequencyHz = 0.5;

    public static FaunaSimulationTier ResolveSimulationTier(double distanceMeters)
    {
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0.0)
        {
            return FaunaSimulationTier.Statistical;
        }
        if (distanceMeters <= NearDistanceMeters)
        {
            return FaunaSimulationTier.Near;
        }
        if (distanceMeters <= MidHighDistanceMeters)
        {
            return FaunaSimulationTier.MidHigh;
        }
        if (distanceMeters <= MidLowDistanceMeters)
        {
            return FaunaSimulationTier.MidLow;
        }
        return FaunaSimulationTier.Statistical;
    }

    public static double GetDecisionFrequencyHz(double distanceMeters) =>
        ResolveSimulationTier(distanceMeters) switch
        {
            FaunaSimulationTier.Near => NearFrequencyHz,
            FaunaSimulationTier.MidHigh => MidHighFrequencyHz,
            FaunaSimulationTier.MidLow => MidLowFrequencyHz,
            _ => 0.0
        };

    public static IReadOnlyList<FaunaUtilityScore> ScoreBehaviors(
        EcologyFaunaDefinition definition,
        EcologyBehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        List<FaunaUtilityScore> scores = new();
        bool Has(string state) => definition.Behaviors.Contains(state, StringComparer.Ordinal);
        void Add(string state, FaunaBehaviorLayer layer, double score)
        {
            if (Has(state))
            {
                scores.Add(new FaunaUtilityScore(state, layer, Math.Clamp(score, 0.0, 1.0)));
            }
        }

        if (context.HitRecently)
        {
            Add("Attack", FaunaBehaviorLayer.Survival,
                definition.Aggression * Math.Clamp(1.25 - context.DistanceToThreat / 10.0, 0.15, 1.0));
            Add("Flee", FaunaBehaviorLayer.Survival,
                (1.0 - definition.Aggression) * 0.85 + Math.Clamp(context.DistanceToThreat / 18.0, 0.0, 0.20));
        }

        double territoryPressure = definition.TerritoryRadius <= 0.0
            ? 0.0
            : Math.Clamp((context.DistanceFromTerritory - definition.TerritoryRadius) /
                Math.Max(4.0, definition.TerritoryRadius), 0.0, 1.0);
        Add("ReturnToTerritory", FaunaBehaviorLayer.Territory, territoryPressure);

        Add("Sleep", FaunaBehaviorLayer.Needs, context.Fatigue);
        Add("Drink", FaunaBehaviorLayer.Needs,
            context.AtWater ? context.Thirst : context.Thirst * 0.18);
        Add("Graze", FaunaBehaviorLayer.Needs,
            string.Equals(definition.Diet, "Herbivore", StringComparison.Ordinal)
                ? context.Hunger
                : 0.0);

        Add("FollowGroup", FaunaBehaviorLayer.Social,
            Math.Clamp((context.GroupDistance - 6.0) / 16.0, 0.0, 1.0));

        double threatNear = Math.Clamp(1.0 - context.DistanceToThreat / 12.0, 0.0, 1.0);
        Add("Threaten", FaunaBehaviorLayer.Awareness,
            definition.Aggression * threatNear);
        Add("Investigate", FaunaBehaviorLayer.Awareness,
            Math.Clamp(1.0 - context.DistanceToThreat / 20.0, 0.0, 1.0) *
            (0.75 - definition.Aggression * 0.25));

        Add("Wander", FaunaBehaviorLayer.Ambient, 0.55);
        Add("Idle", FaunaBehaviorLayer.Ambient, 0.35);
        return scores;
    }

    public static string SelectBehavior(
        EcologyFaunaDefinition definition,
        EcologyBehaviorContext context)
    {
        IReadOnlyList<FaunaUtilityScore> scores = ScoreBehaviors(definition, context);
        foreach (FaunaBehaviorLayer layer in Enum.GetValues<FaunaBehaviorLayer>())
        {
            FaunaUtilityScore? best = scores
                .Where(score => score.Layer == layer && MeetsLayerThreshold(score, context, definition))
                .OrderByDescending(score => score.Score)
                .ThenBy(score => score.State, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best is not null)
            {
                return best.State;
            }
        }
        return definition.Behaviors.Contains("Idle", StringComparer.Ordinal)
            ? "Idle"
            : definition.Behaviors[0];
    }

    private static bool MeetsLayerThreshold(
        FaunaUtilityScore score,
        EcologyBehaviorContext context,
        EcologyFaunaDefinition definition) => score.Layer switch
        {
            FaunaBehaviorLayer.Survival => context.HitRecently && score.Score >= 0.24,
            FaunaBehaviorLayer.Territory =>
                context.DistanceFromTerritory > definition.TerritoryRadius && score.Score > 0.0,
            FaunaBehaviorLayer.Needs => score.State switch
            {
                "Sleep" => context.Fatigue >= 0.82,
                "Drink" => context.AtWater && context.Thirst >= 0.74,
                "Graze" => context.Hunger >= 0.70,
                _ => false
            },
            FaunaBehaviorLayer.Social => context.GroupDistance >= 12.0 && score.Score > 0.0,
            FaunaBehaviorLayer.Awareness => score.State switch
            {
                "Threaten" => definition.Aggression >= 0.35 && context.DistanceToThreat <= 10.0,
                "Investigate" => context.DistanceToThreat <= 18.0,
                _ => false
            },
            FaunaBehaviorLayer.Ambient => true,
            _ => false
        };
}
