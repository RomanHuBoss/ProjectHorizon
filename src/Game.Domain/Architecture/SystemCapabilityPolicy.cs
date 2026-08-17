using System;

public enum SystemCapabilityTier
{
    Unsupported = 0,
    Minimum = 1,
    Recommended = 2
}

public sealed record SystemCapabilityInput(
    bool SupportedOperatingSystem,
    bool Is64Bit,
    int LogicalProcessorCount,
    long PhysicalMemoryBytes,
    bool PhysicalMemoryKnown,
    bool RendererSupported,
    bool PrimaryRenderer,
    bool CompatibilityRenderer,
    bool DedicatedGpu,
    long VideoMemoryCapacityBytes,
    bool VideoMemoryCapacityKnown,
    long FreeStorageBytes,
    bool FreeStorageKnown,
    bool SsdDetected,
    bool StorageMediumKnown);

public sealed record SystemCapabilityEvaluation(
    SystemCapabilityTier Tier,
    bool MinimumSatisfied,
    bool RecommendedSatisfied,
    bool OperatingSystemSatisfied,
    bool CpuSatisfied,
    bool MemorySatisfied,
    bool RendererSatisfied,
    bool VideoMemorySatisfied,
    bool StorageCapacitySatisfied,
    bool StorageMediumSatisfied,
    bool MinimumEvidenceComplete,
    GraphicsQualityProfile RecommendedGraphicsProfile)
{
    public bool HasKnownMinimumFailure => !MinimumSatisfied;
}

/// <summary>
/// Technical Specification section 28 player-system capability policy. The policy
/// deliberately treats hardware facts unavailable through a portable runtime API
/// as unknown rather than inventing values. Unknown SSD/VRAM capacity is therefore
/// reported but does not become a false hard failure.
/// </summary>
public static class SystemCapabilityPolicy
{
    public const int MinimumLogicalProcessors = 4;
    public const int RecommendedLogicalProcessors = 6;
    public const long MinimumPhysicalMemoryBytes = 8L * 1024L * 1024L * 1024L;
    public const long RecommendedPhysicalMemoryBytes = 16L * 1024L * 1024L * 1024L;
    public const long MinimumVideoMemoryBytes = 4L * 1024L * 1024L * 1024L;
    public const long RecommendedVideoMemoryBytes = 6L * 1024L * 1024L * 1024L;
    public const long MinimumFreeStorageBytes = 20L * 1024L * 1024L * 1024L;
    public const long RecommendedFreeStorageBytes = 30L * 1024L * 1024L * 1024L;

    public static SystemCapabilityEvaluation Evaluate(SystemCapabilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        bool os = input.SupportedOperatingSystem && input.Is64Bit;
        bool cpu = input.LogicalProcessorCount >= MinimumLogicalProcessors;
        bool memory = !input.PhysicalMemoryKnown ||
            input.PhysicalMemoryBytes >= MinimumPhysicalMemoryBytes;
        bool renderer = input.RendererSupported;
        bool videoMemory = !input.DedicatedGpu || !input.VideoMemoryCapacityKnown ||
            input.VideoMemoryCapacityBytes >= MinimumVideoMemoryBytes;
        bool storageCapacity = !input.FreeStorageKnown ||
            input.FreeStorageBytes >= MinimumFreeStorageBytes;
        bool storageMedium = !input.StorageMediumKnown || input.SsdDetected;
        bool minimum = os && cpu && memory && renderer && videoMemory &&
            storageCapacity && storageMedium;

        bool recommendedMemory = input.PhysicalMemoryKnown &&
            input.PhysicalMemoryBytes >= RecommendedPhysicalMemoryBytes;
        bool recommendedStorage = input.FreeStorageKnown &&
            input.FreeStorageBytes >= RecommendedFreeStorageBytes;
        bool recommendedVideo = input.DedicatedGpu &&
            (!input.VideoMemoryCapacityKnown ||
             input.VideoMemoryCapacityBytes >= RecommendedVideoMemoryBytes);
        bool recommended = minimum &&
            input.LogicalProcessorCount >= RecommendedLogicalProcessors &&
            recommendedMemory && recommendedStorage && input.PrimaryRenderer &&
            recommendedVideo && (!input.StorageMediumKnown || input.SsdDetected);

        SystemCapabilityTier tier = recommended
            ? SystemCapabilityTier.Recommended
            : minimum ? SystemCapabilityTier.Minimum : SystemCapabilityTier.Unsupported;
        GraphicsQualityProfile profile = tier switch
        {
            SystemCapabilityTier.Unsupported => GraphicsQualityProfile.Compatibility,
            SystemCapabilityTier.Recommended => GraphicsQualityProfile.Medium,
            _ when input.CompatibilityRenderer => GraphicsQualityProfile.Compatibility,
            _ => GraphicsQualityProfile.Low
        };
        bool evidenceComplete = input.PhysicalMemoryKnown && input.FreeStorageKnown &&
            (!input.DedicatedGpu || input.VideoMemoryCapacityKnown) &&
            input.StorageMediumKnown;

        return new SystemCapabilityEvaluation(
            tier,
            minimum,
            recommended,
            os,
            cpu,
            memory,
            renderer,
            videoMemory,
            storageCapacity,
            storageMedium,
            evidenceComplete,
            profile);
    }
}
