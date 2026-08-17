using Xunit;

public sealed class EnduranceSoakTests
{
    [Fact]
    public void CertificationPolicyRequiresEightRealHours()
    {
        Assert.Equal(28_800.0, EnduranceSoakPolicy.RequiredCertificationSeconds);
        Assert.True(EnduranceSoakPolicy.IsCertificationDuration(28_800.0));
        Assert.False(EnduranceSoakPolicy.IsCertificationDuration(7.99 * 3600.0));
    }

    [Fact]
    public void HarnessAcceptanceDetectsCriticalFailureClasses()
    {
        EnduranceSoakAcceptanceReport report = EnduranceSoakAcceptanceRunner.Run();
        Assert.True(report.Passed, report.BuildOutputLine());
        Assert.True(report.StableRunPasses);
        Assert.True(report.MemoryLeakDetected);
        Assert.True(report.QueueStallDetected);
        Assert.True(report.DatabaseCorruptionDetected);
        Assert.True(report.TerrainFailureDetected);
        Assert.True(report.CancellationSafe);
        Assert.True(report.SyntheticWorkload);
    }

    [Fact]
    public void QueueWithoutProgressFailsAfterTwoMinutes()
    {
        EnduranceSoakObservation baseline = new(
            100_000_000,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            true,
            true,
            0);
        EnduranceSoakRuntime runtime = new();
        runtime.Start(8.0, baseline);
        EnduranceSoakObservation stalled = baseline with { TerrainQueuedWork = 3 };
        for (int index = 0; index < 5; index++)
        {
            runtime.Observe(30.0, stalled);
        }
        Assert.Equal(EnduranceSoakRunState.Failed, runtime.State);
        Assert.Contains("stalled", runtime.CreateSnapshot().LastFailureReason);
    }
}
