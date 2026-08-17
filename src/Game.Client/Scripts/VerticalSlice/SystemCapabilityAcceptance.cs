using System;

public sealed record SystemCapabilityAcceptanceReport(
    bool MinimumPolicy,
    bool RecommendedPolicy,
    bool CompatibilityFallback,
    bool UnknownEvidenceSafe,
    bool RecommendOnly,
    bool LiveCapture,
    SystemCapabilityTier LiveTier,
    GraphicsQualityProfile LiveRecommendation,
    bool LiveMinimumSatisfied)
{
    public bool Passed => MinimumPolicy && RecommendedPolicy &&
        CompatibilityFallback && UnknownEvidenceSafe && RecommendOnly && LiveCapture;

    public string BuildOutputLine() =>
        $"TASK-206 system capability acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"minimum={(MinimumPolicy ? 1 : 0)}; recommended={(RecommendedPolicy ? 1 : 0)}; " +
        $"compatibility={(CompatibilityFallback ? 1 : 0)}; unknownSafe={(UnknownEvidenceSafe ? 1 : 0)}; " +
        $"recommendOnly={(RecommendOnly ? 1 : 0)}; live={(LiveCapture ? 1 : 0)}; " +
        $"tier={LiveTier}; minimumLive={(LiveMinimumSatisfied ? 1 : 0)}; " +
        $"recommendedProfile={LiveRecommendation}; " +
        "result=section-28-player-system-capability-preflight-without-hardware-guessing.";
}

public static class SystemCapabilityAcceptanceRunner
{
    public static SystemCapabilityAcceptanceReport Evaluate(
        SystemCapabilitySnapshot live,
        GraphicsQualityProfile requestedProfile)
    {
        ArgumentNullException.ThrowIfNull(live);
        const long GiB = 1024L * 1024L * 1024L;

        SystemCapabilityEvaluation minimum = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            SupportedOperatingSystem: true,
            Is64Bit: true,
            LogicalProcessorCount: 4,
            PhysicalMemoryBytes: 8 * GiB,
            PhysicalMemoryKnown: true,
            RendererSupported: true,
            PrimaryRenderer: true,
            CompatibilityRenderer: false,
            DedicatedGpu: true,
            VideoMemoryCapacityBytes: 4 * GiB,
            VideoMemoryCapacityKnown: true,
            FreeStorageBytes: 20 * GiB,
            FreeStorageKnown: true,
            SsdDetected: true,
            StorageMediumKnown: true));
        bool minimumPolicy = minimum.MinimumSatisfied &&
            minimum.Tier == SystemCapabilityTier.Minimum &&
            minimum.RecommendedGraphicsProfile == GraphicsQualityProfile.Low;

        SystemCapabilityEvaluation recommended = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 8, 32 * GiB, true,
            true, true, false, true,
            8 * GiB, true, 40 * GiB, true, true, true));
        bool recommendedPolicy = recommended.MinimumSatisfied &&
            recommended.RecommendedSatisfied &&
            recommended.Tier == SystemCapabilityTier.Recommended &&
            recommended.RecommendedGraphicsProfile == GraphicsQualityProfile.Medium;

        SystemCapabilityEvaluation compatibility = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 4, 8 * GiB, true,
            true, false, true, false,
            0, false, 25 * GiB, true, false, false));
        bool compatibilityFallback = compatibility.MinimumSatisfied &&
            compatibility.RecommendedGraphicsProfile == GraphicsQualityProfile.Compatibility;

        SystemCapabilityEvaluation unknown = SystemCapabilityPolicy.Evaluate(new SystemCapabilityInput(
            true, true, 6, 16 * GiB, true,
            true, true, false, true,
            0, false, 35 * GiB, true, false, false));
        bool unknownSafe = unknown.MinimumSatisfied && !unknown.MinimumEvidenceComplete;

        // TASK-206 is advisory only. The current user profile is accepted unchanged;
        // only TASK-202's renderer fallback may override it for compatibility safety.
        bool recommendOnly = GraphicsQualityProfilePolicy.IsValid(requestedProfile);
        bool liveCapture = !string.IsNullOrWhiteSpace(live.OperatingSystem) &&
            live.LogicalProcessorCount > 0 &&
            !string.IsNullOrWhiteSpace(live.RenderingMethod) &&
            GraphicsQualityProfilePolicy.IsValid(live.Evaluation.RecommendedGraphicsProfile);

        return new SystemCapabilityAcceptanceReport(
            minimumPolicy,
            recommendedPolicy,
            compatibilityFallback,
            unknownSafe,
            recommendOnly,
            liveCapture,
            live.Evaluation.Tier,
            live.Evaluation.RecommendedGraphicsProfile,
            live.Evaluation.MinimumSatisfied);
    }
}
