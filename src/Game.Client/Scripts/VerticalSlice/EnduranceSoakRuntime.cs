using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

public enum EnduranceSoakRunState
{
    Idle = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public readonly record struct EnduranceSoakObservation(
    long ManagedMemoryBytes,
    int TerrainFailedJobs,
    int TerrainQueuedWork,
    int TerrainCompletedRevision,
    int WorldQueuedOperations,
    int WorldCompletedRevision,
    int DatabaseQueuedWrites,
    int DatabaseCompletedWrites,
    int DatabaseMaximumConcurrentWriters,
    bool DatabaseIntegrityKnown,
    bool DatabaseIntegrityOk,
    int SaveRevision);

public sealed record EnduranceSoakSnapshot(
    EnduranceSoakRunState State,
    double TargetSeconds,
    double ElapsedSeconds,
    int Samples,
    int Heartbeats,
    int WorkloadPulses,
    int PersistenceCheckpoints,
    int DatabaseIntegrityChecks,
    long BaselineManagedMemoryBytes,
    long PeakManagedMemoryBytes,
    long ManagedMemoryGrowthBytes,
    int TerrainFailureDelta,
    double QueueStallSeconds,
    bool CertificationDuration,
    bool CertificationPassed,
    string LastFailureReason,
    string LastWorkloadSignature)
{
    public string BuildCompactStatus() =>
        $"{State} elapsed={ElapsedSeconds / 3600.0:0.00}/{TargetSeconds / 3600.0:0.00}h " +
        $"samples={Samples} hb={Heartbeats} work={WorkloadPulses} save={PersistenceCheckpoints} " +
        $"db={DatabaseIntegrityChecks} memGrowth={ManagedMemoryGrowthBytes / (1024.0 * 1024.0):0.0}MiB " +
        $"queueStall={QueueStallSeconds:0}s";
}

public sealed class EnduranceSoakRuntime
{
    private double _nextHeartbeatSeconds;
    private double _nextWorkloadSeconds;
    private double _nextCheckpointSeconds;
    private double _nextDatabaseIntegritySeconds;
    private int _baselineTerrainFailedJobs;
    private long _baselineManagedBytes;
    private long _peakManagedBytes;
    private int _lastProgressFingerprint;
    private int _lastQueuedWork;
    private bool _hasProgressFingerprint;
    private double _queueStallSeconds;
    private int _samples;
    private int _heartbeats;
    private int _workloadPulses;
    private int _persistenceCheckpoints;
    private int _databaseIntegrityChecks;
    private string _lastFailureReason = string.Empty;
    private string _lastWorkloadSignature = string.Empty;

    public EnduranceSoakRunState State { get; private set; } = EnduranceSoakRunState.Idle;
    public double TargetSeconds { get; private set; }
    public double ElapsedSeconds { get; private set; }

    public void Start(double requestedHours, EnduranceSoakObservation baseline)
    {
        double hours = EnduranceSoakPolicy.NormalizeRequestedHours(requestedHours);
        TargetSeconds = hours * 3600.0;
        ElapsedSeconds = 0.0;
        _samples = 0;
        _heartbeats = 0;
        _workloadPulses = 0;
        _persistenceCheckpoints = 0;
        _databaseIntegrityChecks = 0;
        _baselineManagedBytes = Math.Max(0L, baseline.ManagedMemoryBytes);
        _peakManagedBytes = _baselineManagedBytes;
        _baselineTerrainFailedJobs = baseline.TerrainFailedJobs;
        _lastProgressFingerprint = BuildProgressFingerprint(baseline);
        _lastQueuedWork = TotalQueuedWork(baseline);
        _hasProgressFingerprint = true;
        _queueStallSeconds = 0.0;
        _lastFailureReason = string.Empty;
        _lastWorkloadSignature = string.Empty;
        CurrentTerrainFailureDelta = 0;
        _nextHeartbeatSeconds = EnduranceSoakPolicy.HeartbeatIntervalSeconds;
        _nextWorkloadSeconds = EnduranceSoakPolicy.SyntheticWorkloadIntervalSeconds;
        _nextCheckpointSeconds = EnduranceSoakPolicy.PersistenceCheckpointIntervalSeconds;
        _nextDatabaseIntegritySeconds = EnduranceSoakPolicy.DatabaseIntegrityIntervalSeconds;
        State = EnduranceSoakRunState.Running;
    }

    public void Observe(double deltaSeconds, EnduranceSoakObservation observation)
    {
        if (State != EnduranceSoakRunState.Running)
        {
            return;
        }

        double delta = Math.Max(0.0, deltaSeconds);
        ElapsedSeconds += delta;
        _samples++;
        _peakManagedBytes = Math.Max(_peakManagedBytes, observation.ManagedMemoryBytes);

        CurrentTerrainFailureDelta = Math.Max(
            0,
            observation.TerrainFailedJobs - _baselineTerrainFailedJobs);
        if (CurrentTerrainFailureDelta > 0)
        {
            Fail($"terrain worker failures increased: {_baselineTerrainFailedJobs}->{observation.TerrainFailedJobs}");
            return;
        }
        if (observation.DatabaseMaximumConcurrentWriters >
            EnduranceSoakPolicy.MaximumConcurrentDatabaseWriters)
        {
            Fail($"database writer concurrency={observation.DatabaseMaximumConcurrentWriters}");
            return;
        }
        if (observation.DatabaseIntegrityKnown && !observation.DatabaseIntegrityOk)
        {
            Fail("diagnostic database integrity_check != ok");
            return;
        }

        long memoryGrowth = Math.Max(0L, _peakManagedBytes - _baselineManagedBytes);
        if (memoryGrowth > EnduranceSoakPolicy.MaximumManagedMemoryGrowthBytes)
        {
            Fail(
                $"managed memory growth={memoryGrowth / (1024.0 * 1024.0):0.0}MiB exceeds " +
                $"{EnduranceSoakPolicy.MaximumManagedMemoryGrowthBytes / (1024.0 * 1024.0):0}MiB");
            return;
        }

        int queued = TotalQueuedWork(observation);
        int progress = BuildProgressFingerprint(observation);
        bool progressed = !_hasProgressFingerprint ||
            progress != _lastProgressFingerprint ||
            queued < _lastQueuedWork;
        if (queued > 0 && !progressed)
        {
            _queueStallSeconds += delta;
            if (_queueStallSeconds > EnduranceSoakPolicy.MaximumQueueStallSeconds)
            {
                Fail($"queued work stalled for {_queueStallSeconds:0.0}s; queued={queued}");
                return;
            }
        }
        else
        {
            _queueStallSeconds = 0.0;
        }
        _lastProgressFingerprint = progress;
        _lastQueuedWork = queued;
        _hasProgressFingerprint = true;

    }

    public void TryComplete()
    {
        if (State == EnduranceSoakRunState.Running &&
            ElapsedSeconds >= TargetSeconds)
        {
            CompleteIfCoverageSufficient();
        }
    }

    public bool HeartbeatDue =>
        State == EnduranceSoakRunState.Running && ElapsedSeconds >= _nextHeartbeatSeconds;
    public bool WorkloadDue =>
        State == EnduranceSoakRunState.Running && ElapsedSeconds >= _nextWorkloadSeconds;
    public bool PersistenceCheckpointDue =>
        State == EnduranceSoakRunState.Running && ElapsedSeconds >= _nextCheckpointSeconds;
    public bool DatabaseIntegrityDue =>
        State == EnduranceSoakRunState.Running && ElapsedSeconds >= _nextDatabaseIntegritySeconds;

    public void MarkHeartbeat()
    {
        if (State != EnduranceSoakRunState.Running) return;
        _heartbeats++;
        _nextHeartbeatSeconds += EnduranceSoakPolicy.HeartbeatIntervalSeconds;
    }

    public void MarkWorkload(bool passed, string signature)
    {
        if (State != EnduranceSoakRunState.Running) return;
        if (!passed)
        {
            Fail("synthetic domain workload invariant failed");
            return;
        }
        _workloadPulses++;
        _lastWorkloadSignature = signature ?? string.Empty;
        _nextWorkloadSeconds += EnduranceSoakPolicy.SyntheticWorkloadIntervalSeconds;
    }

    public void MarkPersistenceCheckpoint(bool passed)
    {
        if (State != EnduranceSoakRunState.Running) return;
        if (!passed)
        {
            Fail("diagnostic transactional save failed");
            return;
        }
        _persistenceCheckpoints++;
        _nextCheckpointSeconds += EnduranceSoakPolicy.PersistenceCheckpointIntervalSeconds;
    }

    public void MarkDatabaseIntegrity(bool passed)
    {
        if (State != EnduranceSoakRunState.Running) return;
        _databaseIntegrityChecks++;
        _nextDatabaseIntegritySeconds += EnduranceSoakPolicy.DatabaseIntegrityIntervalSeconds;
        if (!passed)
        {
            Fail("diagnostic database integrity_check != ok");
        }
    }

    public void Fail(string reason)
    {
        if (State != EnduranceSoakRunState.Running) return;
        _lastFailureReason = string.IsNullOrWhiteSpace(reason) ? "unspecified critical failure" : reason;
        State = EnduranceSoakRunState.Failed;
    }

    public void Cancel(string reason)
    {
        if (State != EnduranceSoakRunState.Running) return;
        _lastFailureReason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason;
        State = EnduranceSoakRunState.Cancelled;
    }

    public EnduranceSoakSnapshot CreateSnapshot()
    {
        long growth = Math.Max(0L, _peakManagedBytes - _baselineManagedBytes);
        bool certification = EnduranceSoakPolicy.IsCertificationDuration(TargetSeconds);
        return new EnduranceSoakSnapshot(
            State,
            TargetSeconds,
            ElapsedSeconds,
            _samples,
            _heartbeats,
            _workloadPulses,
            _persistenceCheckpoints,
            _databaseIntegrityChecks,
            _baselineManagedBytes,
            _peakManagedBytes,
            growth,
            Math.Max(0, CurrentTerrainFailureDelta),
            _queueStallSeconds,
            certification,
            certification && State == EnduranceSoakRunState.Passed,
            _lastFailureReason,
            _lastWorkloadSignature);
    }

    private int CurrentTerrainFailureDelta { get; set; }

    private void CompleteIfCoverageSufficient()
    {
        int requiredHeartbeats = EnduranceSoakPolicy.RequiredCoverageCount(
            TargetSeconds,
            EnduranceSoakPolicy.HeartbeatIntervalSeconds);
        int requiredWorkloads = EnduranceSoakPolicy.RequiredCoverageCount(
            TargetSeconds,
            EnduranceSoakPolicy.SyntheticWorkloadIntervalSeconds);
        int requiredCheckpoints = EnduranceSoakPolicy.RequiredCoverageCount(
            TargetSeconds,
            EnduranceSoakPolicy.PersistenceCheckpointIntervalSeconds);
        int requiredDatabaseChecks = EnduranceSoakPolicy.RequiredCoverageCount(
            TargetSeconds,
            EnduranceSoakPolicy.DatabaseIntegrityIntervalSeconds);

        if (_heartbeats < requiredHeartbeats ||
            _workloadPulses < requiredWorkloads ||
            _persistenceCheckpoints < requiredCheckpoints ||
            _databaseIntegrityChecks < requiredDatabaseChecks)
        {
            Fail(
                $"insufficient endurance coverage: heartbeat={_heartbeats}/{requiredHeartbeats}; " +
                $"workload={_workloadPulses}/{requiredWorkloads}; save={_persistenceCheckpoints}/{requiredCheckpoints}; " +
                $"db={_databaseIntegrityChecks}/{requiredDatabaseChecks}");
            return;
        }
        State = EnduranceSoakRunState.Passed;
    }

    private static int TotalQueuedWork(EnduranceSoakObservation observation) =>
        Math.Max(0, observation.TerrainQueuedWork) +
        Math.Max(0, observation.WorldQueuedOperations) +
        Math.Max(0, observation.DatabaseQueuedWrites);

    private static int BuildProgressFingerprint(EnduranceSoakObservation observation)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + observation.TerrainCompletedRevision;
            hash = hash * 31 + observation.WorldCompletedRevision;
            hash = hash * 31 + observation.DatabaseCompletedWrites;
            hash = hash * 31 + observation.SaveRevision;
            return hash;
        }
    }
}

public sealed record EnduranceSyntheticWorkloadResult(
    bool Passed,
    int PulseIndex,
    int Regions,
    int FullRegions,
    int SimplifiedRegions,
    int PreloadRegions,
    int PlanetCount,
    string Signature);

public static class EnduranceSyntheticWorkloadRuntime
{
    public static EnduranceSyntheticWorkloadResult Run(int pulseIndex)
    {
        int index = Math.Max(1, pulseIndex);
        GalaxyNavigationRuntime navigation = new(
            GalaxyNavigationRuntime.DefaultUniverseSeed + (index % 17));
        int sectorX = (index % 97) - 48;
        int sectorY = ((index / 7) % 9) - 4;
        int sectorZ = ((index * 11) % 83) - 41;
        GalaxySystemDefinition first = navigation.GenerateSystem(sectorX, sectorY, sectorZ);
        GalaxySystemDefinition replay = navigation.GenerateSystem(sectorX, sectorY, sectorZ);
        bool deterministic = string.Equals(first.SystemId, replay.SystemId, StringComparison.Ordinal) &&
            first.StarType == replay.StarType &&
            first.Planets.Count == replay.Planets.Count &&
            first.Planets.Select(planet => planet.PlanetId)
                .SequenceEqual(replay.Planets.Select(planet => planet.PlanetId), StringComparer.Ordinal);

        WorldStreamingTravelMode mode = (WorldStreamingTravelMode)(index % 3);
        WorldStreamingObserverSample observer = new(
            EastMeters: sectorX * 173.0,
            NorthMeters: sectorZ * 149.0,
            VelocityEastMetersPerSecond: 18.0 + (index % 31),
            VelocityNorthMetersPerSecond: -12.0 + (index % 23),
            TravelMode: mode);
        WorldStreamingPlan plan = WorldStreamingRuntime.BuildPlan(
            observer,
            presentationDistanceScale: 0.70,
            CancellationToken.None);

        PlanetSurfaceTerrainProfile terrain = new(
            "task214.synthetic",
            "temperate",
            GalaxyNavigationRuntime.DefaultUniverseSeed + index,
            PlanetSurfaceTerrainRuntime.DefaultHalfExtent,
            PlanetSurfaceTerrainRuntime.DefaultResolution,
            7.0,
            0.024,
            16.0,
            23.0,
            34.0,
            true);
        double terrainAccumulator = 0.0;
        for (int sample = 0; sample < 24; sample++)
        {
            double x = ((index * 13 + sample * 17) % 401) - 200.0;
            double z = ((index * 19 + sample * 23) % 401) - 200.0;
            PlanetSurfaceTerrainSample terrainSample =
                PlanetSurfaceTerrainRuntime.Sample(terrain, x, z);
            terrainAccumulator += terrainSample.Height + terrainSample.SlopeDegrees * 0.01;
        }

        string signature =
            $"{index}:{first.SystemId}:{first.StarType}:{first.Planets.Count}:" +
            $"{plan.FullCount}/{plan.SimplifiedCount}/{plan.PreloadCount}:" +
            terrainAccumulator.ToString("0.000", CultureInfo.InvariantCulture);
        bool passed = deterministic && first.Planets.Count is >= 1 and <= 8 &&
            plan.Regions.Count > 0 && plan.FullCount > 0 &&
            double.IsFinite(terrainAccumulator);
        return new EnduranceSyntheticWorkloadResult(
            passed,
            index,
            plan.Regions.Count,
            plan.FullCount,
            plan.SimplifiedCount,
            plan.PreloadCount,
            first.Planets.Count,
            signature);
    }
}
