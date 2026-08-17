using Xunit;

public sealed class RuntimePerformanceBudgetTests
{
    [Fact]
    public void MediumProfileMatchesSection27SceneBudgets()
    {
        RuntimePerformanceBudget budget = RuntimePerformanceBudgetPolicy.Medium;
        Assert.Equal(60.0, budget.TargetFramesPerSecond, 3);
        Assert.Equal(16.6, budget.CpuFrameMilliseconds, 3);
        Assert.Equal(16.6, budget.GpuFrameMilliseconds, 3);
        Assert.Equal(1500, budget.MaximumDrawCalls);
        Assert.Equal(2_000_000, budget.MaximumRenderedPrimitives);
        Assert.Equal(500, budget.MaximumActivePhysicsBodies);
        Assert.Equal(20, budget.MaximumFullAi);
        Assert.Equal(80, budget.MaximumSimplifiedAi);
        Assert.Equal(4L * RuntimePerformanceBudgetPolicy.Gibibyte, budget.MaximumVideoMemoryBytes);
        Assert.Equal(6L * RuntimePerformanceBudgetPolicy.Gibibyte, budget.MaximumProcessMemoryBytes);
        Assert.Equal(256L * RuntimePerformanceBudgetPolicy.Kibibyte,
            budget.MaximumManagedAllocationBytesPerFrame);
    }

    [Fact]
    public void LowProfileReducesSceneBudgetsByAtLeastThirtyPercent()
    {
        RuntimePerformanceBudget medium = RuntimePerformanceBudgetPolicy.Medium;
        RuntimePerformanceBudget low = RuntimePerformanceBudgetPolicy.Low;
        Assert.Equal(30.0, low.TargetFramesPerSecond, 3);
        Assert.Equal(33.3, low.CpuFrameMilliseconds, 3);
        Assert.True(low.MaximumDrawCalls <= medium.MaximumDrawCalls * 0.70);
        Assert.True(low.MaximumRenderedPrimitives <= medium.MaximumRenderedPrimitives * 0.70);
        Assert.True(low.MaximumActivePhysicsBodies <= medium.MaximumActivePhysicsBodies * 0.70);
        Assert.True(low.MaximumFullAi <= medium.MaximumFullAi * 0.70);
        Assert.True(low.MaximumSimplifiedAi <= medium.MaximumSimplifiedAi * 0.70);
    }

    [Fact]
    public void EvaluationReportsFrameRenderAndAllocationOverruns()
    {
        RuntimePerformanceSampleCore sample = new(
            55, 20, 3, 1, 1600, 2_100_000, 100, 10, 60,
            RuntimePerformanceBudgetPolicy.Gibibyte,
            2L * RuntimePerformanceBudgetPolicy.Gibibyte,
            300L * RuntimePerformanceBudgetPolicy.Kibibyte,
            true, true);
        RuntimePerformanceEvaluation evaluation =
            RuntimePerformanceBudgetPolicy.Evaluate(sample, RuntimePerformanceProfile.Medium);
        Assert.False(evaluation.WithinBudget);
        Assert.True(evaluation.Overruns.HasFlag(RuntimePerformanceOverrun.FrameRate));
        Assert.True(evaluation.Overruns.HasFlag(RuntimePerformanceOverrun.CpuFrame));
        Assert.True(evaluation.Overruns.HasFlag(RuntimePerformanceOverrun.DrawCalls));
        Assert.True(evaluation.Overruns.HasFlag(RuntimePerformanceOverrun.RenderPrimitives));
        Assert.True(evaluation.Overruns.HasFlag(RuntimePerformanceOverrun.ManagedAllocations));
    }

    [Fact]
    public void AdaptiveGovernorUsesHysteresisAndRecoversGradually()
    {
        RuntimePerformanceSampleCore healthy = new(
            60, 8, 2, .5, 600, 800_000, 100, 10, 50,
            RuntimePerformanceBudgetPolicy.Gibibyte,
            2L * RuntimePerformanceBudgetPolicy.Gibibyte,
            32L * RuntimePerformanceBudgetPolicy.Kibibyte,
            true, true);
        RuntimePerformanceSampleCore overloaded = healthy with { CpuFrameMilliseconds = 24 };
        RuntimePerformanceEvaluation good =
            RuntimePerformanceBudgetPolicy.Evaluate(healthy, RuntimePerformanceProfile.Medium);
        RuntimePerformanceEvaluation bad =
            RuntimePerformanceBudgetPolicy.Evaluate(overloaded, RuntimePerformanceProfile.Medium);
        RuntimePerformanceAdaptiveGovernor governor = new();
        for (int index = 0; index < RuntimePerformanceAdaptiveGovernor.ConstrainedOverrunSamples; index++)
        {
            governor.Observe(bad);
        }
        Assert.Equal(RuntimePerformanceQualityState.Constrained, governor.State);
        governor.Observe(RuntimePerformanceBudgetPolicy.Evaluate(
            overloaded with { CpuFrameMilliseconds = 30 }, RuntimePerformanceProfile.Medium));
        Assert.Equal(RuntimePerformanceQualityState.Critical, governor.State);
        for (int index = 0; index < RuntimePerformanceAdaptiveGovernor.RecoveryCleanSamples; index++)
        {
            governor.Observe(good);
        }
        Assert.Equal(RuntimePerformanceQualityState.Constrained, governor.State);
        for (int index = 0; index < RuntimePerformanceAdaptiveGovernor.RecoveryCleanSamples; index++)
        {
            governor.Observe(good);
        }
        Assert.Equal(RuntimePerformanceQualityState.Nominal, governor.State);
    }

    [Fact]
    public void AdaptiveQualityChangesOnlyPresentationSettings()
    {
        RuntimePerformanceQualitySettings nominal =
            RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                RuntimePerformanceProfile.Medium,
                RuntimePerformanceQualityState.Nominal);
        RuntimePerformanceQualitySettings constrained =
            RuntimePerformanceBudgetPolicy.ResolveQualitySettings(
                RuntimePerformanceProfile.Medium,
                RuntimePerformanceQualityState.Constrained);
        Assert.Equal(1.0, nominal.VegetationDistanceScale, 3);
        Assert.Equal(2, nominal.MaximumCloudLayers);
        Assert.True(constrained.VegetationDistanceScale < nominal.VegetationDistanceScale);
        Assert.Equal(1, constrained.MaximumCloudLayers);
    }
}
