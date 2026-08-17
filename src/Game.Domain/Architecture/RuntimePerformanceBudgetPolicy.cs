using System;

public enum RuntimePerformanceProfile
{
    Medium = 0,
    Low = 1
}

public enum RuntimePerformanceQualityState
{
    Nominal = 0,
    Constrained = 1,
    Critical = 2
}

[Flags]
public enum RuntimePerformanceOverrun
{
    None = 0,
    FrameRate = 1 << 0,
    CpuFrame = 1 << 1,
    DrawCalls = 1 << 2,
    RenderPrimitives = 1 << 3,
    ActivePhysics = 1 << 4,
    FullAi = 1 << 5,
    SimplifiedAi = 1 << 6,
    VideoMemory = 1 << 7,
    ProcessMemory = 1 << 8,
    ManagedAllocations = 1 << 9
}

public sealed record RuntimePerformanceBudget(
    RuntimePerformanceProfile Profile,
    double TargetFramesPerSecond,
    double CpuFrameMilliseconds,
    double GpuFrameMilliseconds,
    int MaximumDrawCalls,
    long MaximumRenderedPrimitives,
    int MaximumActivePhysicsBodies,
    int MaximumFullAi,
    int MaximumSimplifiedAi,
    long MaximumVideoMemoryBytes,
    long MaximumProcessMemoryBytes,
    long MaximumManagedAllocationBytesPerFrame);

public sealed record RuntimePerformanceSampleCore(
    double FramesPerSecond,
    double CpuFrameMilliseconds,
    double PhysicsMilliseconds,
    double NavigationMilliseconds,
    int DrawCalls,
    long RenderedPrimitives,
    int ActivePhysicsBodies,
    int FullAi,
    int SimplifiedAi,
    long VideoMemoryBytes,
    long ProcessMemoryBytes,
    long ManagedAllocationBytesPerFrame,
    bool RenderingMetricsAvailable,
    bool VideoMemoryMetricAvailable);

public sealed record RuntimePerformanceEvaluation(
    RuntimePerformanceBudget Budget,
    RuntimePerformanceOverrun Overruns,
    double PressureRatio)
{
    public bool WithinBudget => Overruns == RuntimePerformanceOverrun.None;
}

public sealed record RuntimePerformanceQualitySettings(
    double VegetationDistanceScale,
    int MaximumCloudLayers,
    double SecondaryCloudOpacityScale);

/// <summary>
/// Section 27 runtime performance policy. The Medium limits reproduce the normative
/// scene budgets; Low reduces scene/memory/allocation budgets by at least 30 percent
/// while allowing the 30 FPS frame-time target from the specification.
/// </summary>
public static class RuntimePerformanceBudgetPolicy
{
    public const long Kibibyte = 1024L;
    public const long Mebibyte = 1024L * Kibibyte;
    public const long Gibibyte = 1024L * Mebibyte;

    public static RuntimePerformanceBudget Medium { get; } = new(
        RuntimePerformanceProfile.Medium,
        TargetFramesPerSecond: 60.0,
        CpuFrameMilliseconds: 16.6,
        GpuFrameMilliseconds: 16.6,
        MaximumDrawCalls: 1500,
        MaximumRenderedPrimitives: 2_000_000,
        MaximumActivePhysicsBodies: 500,
        MaximumFullAi: 20,
        MaximumSimplifiedAi: 80,
        MaximumVideoMemoryBytes: 4L * Gibibyte,
        MaximumProcessMemoryBytes: 6L * Gibibyte,
        MaximumManagedAllocationBytesPerFrame: 256L * Kibibyte);

    public static RuntimePerformanceBudget Low { get; } = new(
        RuntimePerformanceProfile.Low,
        TargetFramesPerSecond: 30.0,
        CpuFrameMilliseconds: 33.3,
        GpuFrameMilliseconds: 33.3,
        MaximumDrawCalls: 1050,
        MaximumRenderedPrimitives: 1_400_000,
        MaximumActivePhysicsBodies: 350,
        MaximumFullAi: 14,
        MaximumSimplifiedAi: 56,
        MaximumVideoMemoryBytes: 2_800L * Mebibyte,
        MaximumProcessMemoryBytes: 4_300L * Mebibyte,
        MaximumManagedAllocationBytesPerFrame: 179L * Kibibyte);

    public static RuntimePerformanceBudget Get(RuntimePerformanceProfile profile) =>
        profile == RuntimePerformanceProfile.Low ? Low : Medium;

    public static RuntimePerformanceEvaluation Evaluate(
        RuntimePerformanceSampleCore sample,
        RuntimePerformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(sample);
        RuntimePerformanceBudget budget = Get(profile);
        RuntimePerformanceOverrun overrun = RuntimePerformanceOverrun.None;
        double pressure = 0.0;

        AddInverseRatio(sample.FramesPerSecond, budget.TargetFramesPerSecond,
            RuntimePerformanceOverrun.FrameRate, ref overrun, ref pressure);
        AddRatio(sample.CpuFrameMilliseconds, budget.CpuFrameMilliseconds,
            RuntimePerformanceOverrun.CpuFrame, ref overrun, ref pressure);
        if (sample.RenderingMetricsAvailable)
        {
            AddRatio(sample.DrawCalls, budget.MaximumDrawCalls,
                RuntimePerformanceOverrun.DrawCalls, ref overrun, ref pressure);
            AddRatio(sample.RenderedPrimitives, budget.MaximumRenderedPrimitives,
                RuntimePerformanceOverrun.RenderPrimitives, ref overrun, ref pressure);
        }
        AddRatio(sample.ActivePhysicsBodies, budget.MaximumActivePhysicsBodies,
            RuntimePerformanceOverrun.ActivePhysics, ref overrun, ref pressure);
        AddRatio(sample.FullAi, budget.MaximumFullAi,
            RuntimePerformanceOverrun.FullAi, ref overrun, ref pressure);
        AddRatio(sample.SimplifiedAi, budget.MaximumSimplifiedAi,
            RuntimePerformanceOverrun.SimplifiedAi, ref overrun, ref pressure);
        if (sample.VideoMemoryMetricAvailable)
        {
            AddRatio(sample.VideoMemoryBytes, budget.MaximumVideoMemoryBytes,
                RuntimePerformanceOverrun.VideoMemory, ref overrun, ref pressure);
        }
        AddRatio(sample.ProcessMemoryBytes, budget.MaximumProcessMemoryBytes,
            RuntimePerformanceOverrun.ProcessMemory, ref overrun, ref pressure);
        AddRatio(sample.ManagedAllocationBytesPerFrame,
            budget.MaximumManagedAllocationBytesPerFrame,
            RuntimePerformanceOverrun.ManagedAllocations, ref overrun, ref pressure);

        return new RuntimePerformanceEvaluation(budget, overrun, pressure);
    }

    public static RuntimePerformanceQualitySettings ResolveQualitySettings(
        RuntimePerformanceProfile profile,
        RuntimePerformanceQualityState state)
    {
        if (profile == RuntimePerformanceProfile.Low)
        {
            return state switch
            {
                RuntimePerformanceQualityState.Critical => new(0.55, 1, 0.55),
                RuntimePerformanceQualityState.Constrained => new(0.65, 1, 0.70),
                _ => new(0.75, 1, 0.85)
            };
        }

        return state switch
        {
            RuntimePerformanceQualityState.Critical => new(0.70, 1, 0.70),
            RuntimePerformanceQualityState.Constrained => new(0.85, 1, 0.90),
            _ => new(1.00, 2, 1.00)
        };
    }

    private static void AddInverseRatio(
        double actual,
        double minimum,
        RuntimePerformanceOverrun flag,
        ref RuntimePerformanceOverrun overrun,
        ref double pressure)
    {
        if (!double.IsFinite(actual) || actual <= 0.0 || minimum <= 0.0)
        {
            return;
        }
        double ratio = minimum / actual;
        pressure = Math.Max(pressure, ratio);
        if (actual < minimum)
        {
            overrun |= flag;
        }
    }

    private static void AddRatio(
        double actual,
        double limit,
        RuntimePerformanceOverrun flag,
        ref RuntimePerformanceOverrun overrun,
        ref double pressure)
    {
        if (!double.IsFinite(actual) || actual < 0.0 || limit <= 0.0)
        {
            return;
        }
        double ratio = actual / limit;
        pressure = Math.Max(pressure, ratio);
        if (ratio > 1.0)
        {
            overrun |= flag;
        }
    }

    private static void AddRatio(
        long actual,
        long limit,
        RuntimePerformanceOverrun flag,
        ref RuntimePerformanceOverrun overrun,
        ref double pressure) =>
        AddRatio((double)actual, limit, flag, ref overrun, ref pressure);

    private static void AddRatio(
        int actual,
        int limit,
        RuntimePerformanceOverrun flag,
        ref RuntimePerformanceOverrun overrun,
        ref double pressure) =>
        AddRatio((double)actual, limit, flag, ref overrun, ref pressure);
}

/// <summary>
/// Hysteretic presentation-only governor. It never changes authoritative gameplay
/// frequencies, collision, persistence or simulation results.
/// </summary>
public sealed class RuntimePerformanceAdaptiveGovernor
{
    public const int ConstrainedOverrunSamples = 4;
    public const int CriticalOverrunSamples = 12;
    public const int RecoveryCleanSamples = 20;
    public const double SeverePressureRatio = 1.35;

    private int _overrunSamples;
    private int _cleanSamples;

    public RuntimePerformanceQualityState State { get; private set; } =
        RuntimePerformanceQualityState.Nominal;

    public int OverrunSamples => _overrunSamples;
    public int CleanSamples => _cleanSamples;

    public bool Observe(RuntimePerformanceEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        RuntimePerformanceQualityState before = State;
        if (!evaluation.WithinBudget)
        {
            _cleanSamples = 0;
            _overrunSamples++;
            if (evaluation.PressureRatio >= SeverePressureRatio ||
                _overrunSamples >= CriticalOverrunSamples)
            {
                State = RuntimePerformanceQualityState.Critical;
            }
            else if (_overrunSamples >= ConstrainedOverrunSamples)
            {
                State = RuntimePerformanceQualityState.Constrained;
            }
        }
        else
        {
            _overrunSamples = 0;
            _cleanSamples++;
            if (_cleanSamples >= RecoveryCleanSamples)
            {
                State = State switch
                {
                    RuntimePerformanceQualityState.Critical =>
                        RuntimePerformanceQualityState.Constrained,
                    RuntimePerformanceQualityState.Constrained =>
                        RuntimePerformanceQualityState.Nominal,
                    _ => RuntimePerformanceQualityState.Nominal
                };
                _cleanSamples = 0;
            }
        }
        return State != before;
    }

    public void Reset()
    {
        State = RuntimePerformanceQualityState.Nominal;
        _overrunSamples = 0;
        _cleanSamples = 0;
    }
}
