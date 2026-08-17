using System;

public sealed record RuntimePerformanceAcceptanceReport(
    bool Passed,
    bool MediumBudgets,
    bool LowBudgets,
    bool TelemetryMonitors,
    bool AllocationBudget,
    bool AdaptiveGovernor,
    bool PresentationOnlyDegradation,
    bool LiveSample,
    string Profile,
    string RuntimeBudgetStatus,
    int Samples,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-200 runtime performance acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"medium={(MediumBudgets ? 1 : 0)}; low={(LowBudgets ? 1 : 0)}; " +
        $"telemetry={(TelemetryMonitors ? 1 : 0)}; allocations={(AllocationBudget ? 1 : 0)}; " +
        $"governor={(AdaptiveGovernor ? 1 : 0)}; presentationOnly={(PresentationOnlyDegradation ? 1 : 0)}; " +
        $"live={(LiveSample ? 1 : 0)}; profile={Profile}; samples={Samples}; " +
        $"budgetStatus={RuntimeBudgetStatus}; result={Result}";
}

public static class RuntimePerformanceAcceptanceRunner
{
    public static RuntimePerformanceAcceptanceReport Evaluate(
        RuntimePerformanceLiveSnapshot? live,
        bool vegetationAdaptiveHook,
        bool cloudAdaptiveHook)
    {
        RuntimePerformanceBudget medium = RuntimePerformanceBudgetPolicy.Medium;
        RuntimePerformanceBudget low = RuntimePerformanceBudgetPolicy.Low;
        bool mediumBudgets =
            Math.Abs(medium.TargetFramesPerSecond - 60.0) < 0.001 &&
            Math.Abs(medium.CpuFrameMilliseconds - 16.6) < 0.001 &&
            Math.Abs(medium.GpuFrameMilliseconds - 16.6) < 0.001 &&
            medium.MaximumDrawCalls == 1500 &&
            medium.MaximumRenderedPrimitives == 2_000_000 &&
            medium.MaximumActivePhysicsBodies == 500 &&
            medium.MaximumFullAi == 20 &&
            medium.MaximumSimplifiedAi == 80 &&
            medium.MaximumVideoMemoryBytes == 4L * RuntimePerformanceBudgetPolicy.Gibibyte &&
            medium.MaximumProcessMemoryBytes == 6L * RuntimePerformanceBudgetPolicy.Gibibyte;

        bool lowBudgets =
            Math.Abs(low.TargetFramesPerSecond - 30.0) < 0.001 &&
            Math.Abs(low.CpuFrameMilliseconds - 33.3) < 0.001 &&
            low.MaximumDrawCalls <= medium.MaximumDrawCalls * 0.70 &&
            low.MaximumRenderedPrimitives <= medium.MaximumRenderedPrimitives * 0.70 &&
            low.MaximumActivePhysicsBodies <= medium.MaximumActivePhysicsBodies * 0.70 &&
            low.MaximumFullAi <= medium.MaximumFullAi * 0.70 &&
            low.MaximumSimplifiedAi <= medium.MaximumSimplifiedAi * 0.70 &&
            low.MaximumVideoMemoryBytes <= medium.MaximumVideoMemoryBytes * 0.70 + RuntimePerformanceBudgetPolicy.Mebibyte &&
            low.MaximumProcessMemoryBytes <= medium.MaximumProcessMemoryBytes * 0.70 + 128L * RuntimePerformanceBudgetPolicy.Mebibyte;

        RuntimePerformanceSampleCore healthy = new(
            60.0, 8.0, 2.0, 0.5, 700, 900_000, 120, 12, 60,
            1L * RuntimePerformanceBudgetPolicy.Gibibyte,
            2L * RuntimePerformanceBudgetPolicy.Gibibyte,
            32L * RuntimePerformanceBudgetPolicy.Kibibyte,
            true, true);
        RuntimePerformanceSampleCore overloaded = healthy with
        {
            FramesPerSecond = 42.0,
            CpuFrameMilliseconds = 24.0,
            DrawCalls = 1800,
            RenderedPrimitives = 2_300_000,
            ManagedAllocationBytesPerFrame = 400L * RuntimePerformanceBudgetPolicy.Kibibyte
        };
        RuntimePerformanceEvaluation healthyEvaluation =
            RuntimePerformanceBudgetPolicy.Evaluate(healthy, RuntimePerformanceProfile.Medium);
        RuntimePerformanceEvaluation overloadedEvaluation =
            RuntimePerformanceBudgetPolicy.Evaluate(overloaded, RuntimePerformanceProfile.Medium);
        bool telemetryMonitors = healthyEvaluation.WithinBudget &&
            !overloadedEvaluation.WithinBudget &&
            overloadedEvaluation.Overruns.HasFlag(RuntimePerformanceOverrun.FrameRate) &&
            overloadedEvaluation.Overruns.HasFlag(RuntimePerformanceOverrun.CpuFrame) &&
            overloadedEvaluation.Overruns.HasFlag(RuntimePerformanceOverrun.DrawCalls) &&
            overloadedEvaluation.Overruns.HasFlag(RuntimePerformanceOverrun.RenderPrimitives);
        bool allocationBudget =
            medium.MaximumManagedAllocationBytesPerFrame == 256L * RuntimePerformanceBudgetPolicy.Kibibyte &&
            low.MaximumManagedAllocationBytesPerFrame <=
                medium.MaximumManagedAllocationBytesPerFrame * 0.70 + RuntimePerformanceBudgetPolicy.Kibibyte &&
            overloadedEvaluation.Overruns.HasFlag(RuntimePerformanceOverrun.ManagedAllocations);

        RuntimePerformanceAdaptiveGovernor governor = new();
        for (int index = 0; index < RuntimePerformanceAdaptiveGovernor.ConstrainedOverrunSamples; index++)
        {
            governor.Observe(overloadedEvaluation);
        }
        bool constrained = governor.State == RuntimePerformanceQualityState.Constrained;
        RuntimePerformanceSampleCore severe = overloaded with { CpuFrameMilliseconds = 30.0 };
        governor.Observe(RuntimePerformanceBudgetPolicy.Evaluate(severe, RuntimePerformanceProfile.Medium));
        bool critical = governor.State == RuntimePerformanceQualityState.Critical;
        for (int index = 0; index < RuntimePerformanceAdaptiveGovernor.RecoveryCleanSamples * 2; index++)
        {
            governor.Observe(healthyEvaluation);
        }
        bool adaptiveGovernor = constrained && critical &&
            governor.State == RuntimePerformanceQualityState.Nominal;

        RuntimePerformanceQualitySettings nominal =
            RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                RuntimePerformanceProfile.Medium,
                RuntimePerformanceQualityState.Nominal);
        RuntimePerformanceQualitySettings constrainedSettings =
            RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                RuntimePerformanceProfile.Medium,
                RuntimePerformanceQualityState.Constrained);
        RuntimePerformanceQualitySettings lowSettings =
            RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                RuntimePerformanceProfile.Low,
                RuntimePerformanceQualityState.Nominal);
        bool presentationOnly = vegetationAdaptiveHook && cloudAdaptiveHook &&
            nominal.VegetationDistanceScale == 1.0 && nominal.MaximumCloudLayers == 2 &&
            constrainedSettings.VegetationDistanceScale < nominal.VegetationDistanceScale &&
            constrainedSettings.MaximumCloudLayers == 1 &&
            lowSettings.VegetationDistanceScale <= 0.75 && lowSettings.MaximumCloudLayers == 1;

        bool liveSample = live is not null &&
            live.SampleIndex > 0 &&
            double.IsFinite(live.Sample.FramesPerSecond) &&
            double.IsFinite(live.Sample.CpuFrameMilliseconds) &&
            live.Sample.ProcessMemoryBytes > 0 &&
            live.Sample.ManagedAllocationBytesPerFrame >= 0 &&
            live.SceneNodeCount >= 0 && live.ResourceCount >= 0;
        bool passed = mediumBudgets && lowBudgets && telemetryMonitors &&
            allocationBudget && adaptiveGovernor && presentationOnly && liveSample;
        return new RuntimePerformanceAcceptanceReport(
            passed,
            mediumBudgets,
            lowBudgets,
            telemetryMonitors,
            allocationBudget,
            adaptiveGovernor,
            presentationOnly,
            liveSample,
            live?.Profile.ToString() ?? "none",
            live?.BudgetStatus ?? "unavailable",
            live?.SampleIndex ?? 0,
            passed
                ? "section 27 frame/scene/allocation budgets, engine telemetry and hysteretic presentation-only degradation verified"
                : "one or more runtime performance budget invariants failed");
    }
}
