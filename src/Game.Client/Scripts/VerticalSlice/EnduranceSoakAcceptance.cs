using System;

public sealed record EnduranceSoakAcceptanceReport(
    bool Passed,
    bool EightHourPolicy,
    bool StableRunPasses,
    bool MemoryLeakDetected,
    bool QueueStallDetected,
    bool DatabaseCorruptionDetected,
    bool TerrainFailureDetected,
    bool CancellationSafe,
    bool SyntheticWorkload,
    int VirtualHeartbeats,
    int VirtualCheckpoints,
    int VirtualDatabaseChecks,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-214 endurance soak harness acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"duration8h={(EightHourPolicy ? 1 : 0)}; stable={(StableRunPasses ? 1 : 0)}; " +
        $"memoryLeak={(MemoryLeakDetected ? 1 : 0)}; queueStall={(QueueStallDetected ? 1 : 0)}; " +
        $"dbCorruption={(DatabaseCorruptionDetected ? 1 : 0)}; terrainFailure={(TerrainFailureDetected ? 1 : 0)}; " +
        $"cancel={(CancellationSafe ? 1 : 0)}; workload={(SyntheticWorkload ? 1 : 0)}; " +
        $"heartbeats={VirtualHeartbeats}; checkpoints={VirtualCheckpoints}; dbChecks={VirtualDatabaseChecks}; " +
        $"ownerCertification=8h-real-time-required; result={Result}.";
}

public static class EnduranceSoakAcceptanceRunner
{
    public static EnduranceSoakAcceptanceReport Run()
    {
        EnduranceSoakObservation baseline = Observation();
        EnduranceSoakRuntime stable = new();
        stable.Start(EnduranceSoakPolicy.RequiredCertificationHours, baseline);
        int minutes = (int)(EnduranceSoakPolicy.RequiredCertificationHours * 60.0);
        for (int minute = 1; minute <= minutes && stable.State == EnduranceSoakRunState.Running; minute++)
        {
            EnduranceSoakObservation sample = Observation(
                memory: baseline.ManagedMemoryBytes + (minute % 48) * 1024L * 1024L,
                terrainRevision: minute,
                worldRevision: minute,
                dbCompleted: minute / 5,
                saveRevision: minute / 5);
            stable.Observe(60.0, sample);
            if (stable.State != EnduranceSoakRunState.Running) break;
            stable.MarkHeartbeat();
            stable.MarkWorkload(true, $"pulse-{minute}-a");
            stable.MarkWorkload(true, $"pulse-{minute}-b");
            if (minute % 5 == 0) stable.MarkPersistenceCheckpoint(true);
            if (minute % 15 == 0) stable.MarkDatabaseIntegrity(true);
            stable.TryComplete();
        }
        EnduranceSoakSnapshot stableSnapshot = stable.CreateSnapshot();

        EnduranceSoakRuntime memoryLeak = new();
        memoryLeak.Start(8.0, baseline);
        memoryLeak.Observe(1.0, Observation(
            memory: baseline.ManagedMemoryBytes + EnduranceSoakPolicy.MaximumManagedMemoryGrowthBytes + 1,
            terrainRevision: 1));

        EnduranceSoakRuntime queueStall = new();
        queueStall.Start(8.0, baseline);
        for (int index = 0; index < 5 && queueStall.State == EnduranceSoakRunState.Running; index++)
        {
            queueStall.Observe(30.0, Observation(queued: 4));
        }

        EnduranceSoakRuntime dbCorruption = new();
        dbCorruption.Start(8.0, baseline);
        dbCorruption.Observe(1.0, Observation(dbKnown: true, dbOk: false, terrainRevision: 1));

        EnduranceSoakRuntime terrainFailure = new();
        terrainFailure.Start(8.0, baseline);
        terrainFailure.Observe(1.0, Observation(terrainFailures: 1, terrainRevision: 1));

        EnduranceSoakRuntime cancelled = new();
        cancelled.Start(8.0, baseline);
        cancelled.Cancel("operator stop");

        EnduranceSyntheticWorkloadResult workload = EnduranceSyntheticWorkloadRuntime.Run(214);

        bool eightHourPolicy = Math.Abs(
            EnduranceSoakPolicy.RequiredCertificationSeconds - 28_800.0) < 0.001 &&
            EnduranceSoakPolicy.IsCertificationDuration(28_800.0) &&
            !EnduranceSoakPolicy.IsCertificationDuration(3_600.0);
        bool stablePass = stableSnapshot.State == EnduranceSoakRunState.Passed &&
            stableSnapshot.CertificationPassed;
        bool memoryDetected = memoryLeak.State == EnduranceSoakRunState.Failed;
        bool stallDetected = queueStall.State == EnduranceSoakRunState.Failed;
        bool corruptionDetected = dbCorruption.State == EnduranceSoakRunState.Failed;
        bool terrainDetected = terrainFailure.State == EnduranceSoakRunState.Failed;
        bool cancellationSafe = cancelled.State == EnduranceSoakRunState.Cancelled;
        bool passed = eightHourPolicy && stablePass && memoryDetected && stallDetected &&
            corruptionDetected && terrainDetected && cancellationSafe && workload.Passed;

        return new EnduranceSoakAcceptanceReport(
            passed,
            eightHourPolicy,
            stablePass,
            memoryDetected,
            stallDetected,
            corruptionDetected,
            terrainDetected,
            cancellationSafe,
            workload.Passed,
            stableSnapshot.Heartbeats,
            stableSnapshot.PersistenceCheckpoints,
            stableSnapshot.DatabaseIntegrityChecks,
            passed
                ? "section-41-eight-hour-harness-detects-critical-failure-and-preserves-owner-certification-boundary"
                : "one or more TASK-214 harness invariants failed");
    }

    private static EnduranceSoakObservation Observation(
        long memory = 256L * 1024L * 1024L,
        int terrainFailures = 0,
        int queued = 0,
        int terrainRevision = 0,
        int worldRevision = 0,
        int dbCompleted = 0,
        bool dbKnown = true,
        bool dbOk = true,
        int saveRevision = 0) => new(
            memory,
            terrainFailures,
            queued,
            terrainRevision,
            0,
            worldRevision,
            0,
            dbCompleted,
            1,
            dbKnown,
            dbOk,
            saveRevision);
}
