using System;
using System.Collections.Generic;
using System.Linq;

public sealed record FaunaSkeletonDefinition(
    string BodyPlan,
    string SkeletonId,
    IReadOnlyList<string> Joints,
    IReadOnlyList<string> HeadModules,
    IReadOnlyList<string> TorsoModules,
    IReadOnlyList<string> LimbModules,
    IReadOnlyList<string> TailModules,
    IReadOnlyList<string> HornModules,
    IReadOnlyList<string> ShellModules);

public sealed record FaunaMorphologyProfile(
    string BodyPlan,
    string SkeletonId,
    int JointCount,
    string HeadModule,
    string TorsoModule,
    string LimbModule,
    string TailModule,
    string HornModule,
    string ShellModule,
    double WidthScale,
    double HeightScale,
    double LengthScale,
    double MaterialRoughness,
    double ColorVariation)
{
    public IReadOnlyList<string> Modules => new[]
    {
        HeadModule,
        TorsoModule,
        LimbModule,
        TailModule,
        HornModule,
        ShellModule
    };
}

/// <summary>
/// TASK-198 / Technical Specification section 12.1.
/// Provides six immutable skeleton families and deterministic procedural module
/// selection. Modules are namespaced by skeleton family, so cross-skeleton
/// assembly is rejected before a visual entity is created.
/// </summary>
public static class FaunaBodyPlanRuntime
{
    private static readonly IReadOnlyDictionary<string, FaunaSkeletonDefinition> Skeletons =
        new Dictionary<string, FaunaSkeletonDefinition>(StringComparer.Ordinal)
        {
            ["Biped"] = Define(
                "Biped", "skeleton.biped.v1",
                new[] { "root", "spine", "neck", "head", "leg_l", "leg_r", "tail" },
                new[] { "round", "beaked", "crest" },
                new[] { "upright", "barrel", "slender" },
                new[] { "digitigrade", "stilt", "clawed" },
                new[] { "none", "balance", "short" },
                new[] { "none", "pair", "crest" },
                new[] { "none", "dorsal", "shoulder" }),
            ["Quadruped"] = Define(
                "Quadruped", "skeleton.quadruped.v1",
                new[] { "root", "spine", "neck", "head", "fore_l", "fore_r", "hind_l", "hind_r", "tail" },
                new[] { "muzzle", "broad", "crest" },
                new[] { "barrel", "low", "arched" },
                new[] { "hoof", "paw", "stilt" },
                new[] { "none", "long", "brush" },
                new[] { "none", "pair", "crown" },
                new[] { "none", "dorsal", "full" }),
            ["Hexapod"] = Define(
                "Hexapod", "skeleton.hexapod.v1",
                new[] { "root", "thorax", "head", "leg_fl", "leg_fr", "leg_ml", "leg_mr", "leg_rl", "leg_rr", "tail" },
                new[] { "mandible", "round", "shielded" },
                new[] { "segmented", "armored", "low" },
                new[] { "spider", "stilt", "clawed" },
                new[] { "none", "spine", "short" },
                new[] { "none", "antenna", "pair" },
                new[] { "dorsal", "full", "plates" }),
            ["Flying"] = Define(
                "Flying", "skeleton.flying.v1",
                new[] { "root", "thorax", "neck", "head", "wing_l", "wing_r", "tail", "leg_l", "leg_r" },
                new[] { "beak", "round", "sensor" },
                new[] { "glider", "compact", "keel" },
                new[] { "talon", "short", "none" },
                new[] { "fan", "fork", "rudder" },
                new[] { "none", "crest", "pair" },
                new[] { "none", "dorsal", "keel" }),
            ["Aquatic"] = Define(
                "Aquatic", "skeleton.aquatic.v1",
                new[] { "root", "spine", "head", "fin_l", "fin_r", "tail", "tail_fin" },
                new[] { "wedge", "round", "filter" },
                new[] { "fusiform", "ray", "eel" },
                new[] { "fin", "paddle", "none" },
                new[] { "fork", "fan", "eel" },
                new[] { "none", "sensor", "crest" },
                new[] { "none", "dorsal", "armored" }),
            ["Crawler"] = Define(
                "Crawler", "skeleton.crawler.v1",
                new[] { "root", "front", "head", "segment_a", "segment_b", "segment_c", "limb_l", "limb_r", "tail" },
                new[] { "wedge", "mandible", "round" },
                new[] { "segmented", "low", "armored" },
                new[] { "crawler", "clawed", "none" },
                new[] { "none", "taper", "spine" },
                new[] { "none", "antenna", "pair" },
                new[] { "plates", "dorsal", "full" })
        };

    public static IReadOnlyCollection<string> BodyPlans =>
        Skeletons.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public static FaunaSkeletonDefinition GetSkeleton(string bodyPlan) =>
        Skeletons.TryGetValue(bodyPlan, out FaunaSkeletonDefinition? skeleton)
            ? skeleton
            : throw new KeyNotFoundException($"Unknown fauna body plan {bodyPlan}.");

    public static FaunaMorphologyProfile Build(
        EcologyFaunaDefinition definition,
        string instanceId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        FaunaSkeletonDefinition skeleton = GetSkeleton(definition.BodyPlan);
        ulong seed = EcologyPlanner.StableHash(
            $"{definition.FaunaId}|{instanceId}|TASK-198");
        int cursor = 0;
        string Pick(IReadOnlyList<string> modules)
        {
            ulong mixed = Mix(seed, cursor++);
            string value = modules[(int)(mixed % (ulong)modules.Count)];
            return $"{definition.BodyPlan.ToLowerInvariant()}.{value}";
        }
        double Range(double minimum, double maximum)
        {
            ulong mixed = Mix(seed, cursor++);
            double unit = (mixed & 0xFFFFFFUL) / (double)0xFFFFFFUL;
            return minimum + ((maximum - minimum) * unit);
        }

        FaunaMorphologyProfile profile = new(
            definition.BodyPlan,
            skeleton.SkeletonId,
            skeleton.Joints.Count,
            Pick(skeleton.HeadModules),
            Pick(skeleton.TorsoModules),
            Pick(skeleton.LimbModules),
            Pick(skeleton.TailModules),
            Pick(skeleton.HornModules),
            Pick(skeleton.ShellModules),
            Range(0.88, 1.12),
            Range(0.90, 1.10),
            Range(0.88, 1.14),
            Range(0.48, 0.92),
            Range(-0.10, 0.10));
        if (!IsCompatible(profile))
        {
            throw new InvalidOperationException(
                $"Fauna morphology {definition.FaunaId}/{instanceId} crossed skeleton families.");
        }
        return profile;
    }

    public static bool IsCompatible(FaunaMorphologyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        FaunaSkeletonDefinition skeleton = GetSkeleton(profile.BodyPlan);
        string prefix = profile.BodyPlan.ToLowerInvariant() + ".";
        return string.Equals(profile.SkeletonId, skeleton.SkeletonId, StringComparison.Ordinal) &&
            profile.JointCount == skeleton.Joints.Count &&
            profile.Modules.All(module => module.StartsWith(prefix, StringComparison.Ordinal)) &&
            profile.WidthScale is >= 0.88 and <= 1.12 &&
            profile.HeightScale is >= 0.90 and <= 1.10 &&
            profile.LengthScale is >= 0.88 and <= 1.14;
    }

    public static int CountCompatibleCatalogDefinitions(EcologyCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Fauna.Values.Count(definition => Skeletons.ContainsKey(definition.BodyPlan));
    }

    private static FaunaSkeletonDefinition Define(
        string bodyPlan,
        string skeletonId,
        IReadOnlyList<string> joints,
        IReadOnlyList<string> heads,
        IReadOnlyList<string> torsos,
        IReadOnlyList<string> limbs,
        IReadOnlyList<string> tails,
        IReadOnlyList<string> horns,
        IReadOnlyList<string> shells) =>
        new(bodyPlan, skeletonId, joints, heads, torsos, limbs, tails, horns, shells);

    private static ulong Mix(ulong seed, int index)
    {
        ulong value = seed + 0x9E3779B97F4A7C15UL * (ulong)(index + 1);
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }
}
