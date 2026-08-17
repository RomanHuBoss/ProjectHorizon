using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

public partial class SalvageRepairSlice
{
    private readonly EnduranceSoakRuntime _enduranceSoakRuntime = new();
    private string _enduranceSoakAcceptanceHud = "READY";
    private bool? _enduranceSoakAcceptancePassed;
    private bool _enduranceSoakReadyPrinted;
    private double _enduranceSoakSampleAccumulator;
    private double? _enduranceSoakPendingAutoStartHours;
    private SaveDatabase? _enduranceSoakDatabase;
    private Task<SaveDatabaseDiagnostics>? _enduranceSoakDatabaseInitTask;
    private Task? _enduranceSoakPersistenceTask;
    private Task<SaveDatabaseDiagnostics>? _enduranceSoakDiagnosticsTask;
    private Task<EnduranceSyntheticWorkloadResult>? _enduranceSoakWorkloadTask;
    private SaveDatabaseDiagnostics? _enduranceSoakLastDatabaseDiagnostics;
    private int _enduranceSoakPersistenceRevision;
    private int _enduranceSoakWorkloadIndex;
    private string _enduranceSoakRunId = string.Empty;
    private string _enduranceSoakArtifactDirectory = string.Empty;
    private string _enduranceSoakHeartbeatPath = string.Empty;
    private string _enduranceSoakLatestPath = string.Empty;
    private EnduranceSoakRunState _enduranceSoakLastReportedState = EnduranceSoakRunState.Idle;

    private void InitializeEnduranceSoakRuntime()
    {
        PrintEnduranceSoakReady();
        string diagnosticsDirectory = ProjectSettings.GlobalizePath("user://diagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);
        _enduranceSoakLatestPath = Path.Combine(
            diagnosticsDirectory,
            "task214-endurance-latest.json");
        DetectInterruptedEnduranceRun();

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (string.Equals(argument, "--endurance-soak", StringComparison.OrdinalIgnoreCase))
            {
                _enduranceSoakPendingAutoStartHours = EnduranceSoakPolicy.RequiredCertificationHours;
                break;
            }
            const string prefix = "--endurance-soak=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(
                    argument[prefix.Length..],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double hours))
            {
                _enduranceSoakPendingAutoStartHours = EnduranceSoakPolicy.NormalizeRequestedHours(hours);
                break;
            }
        }
    }

    private void PrintEnduranceSoakReady()
    {
        if (_enduranceSoakReadyPrinted)
        {
            return;
        }
        _enduranceSoakReadyPrinted = true;
        GD.Print(
            "TASK-214 eight-hour endurance READY: " +
            "criterion=spec-41.16; certification=8h-real-time; sample=1s; heartbeat=60s; " +
            "workload=30s-galaxy+streaming+terrain; isolatedSave=5m; dbIntegrity=15m-integrity_check; " +
            "memoryGrowth<=768MiB; queueStall<=120s; terrainWorkerFailure=hard-fail; " +
            "dbWriter<=1; artifacts=user://diagnostics; command=endurance_soak; " +
            "auto=--endurance-soak=8; F5=harness-acceptance-only.");
    }

    private void UpdateEnduranceSoakRuntime(double delta)
    {
        PollEnduranceSoakAsyncWork();
        if (_enduranceSoakPendingAutoStartHours is double hours &&
            _state == SalvageRepairSliceState.Ready &&
            _enduranceSoakRuntime.State != EnduranceSoakRunState.Running)
        {
            _enduranceSoakPendingAutoStartHours = null;
            StartEnduranceSoak(hours);
        }

        if (_enduranceSoakRuntime.State != EnduranceSoakRunState.Running)
        {
            ReportEnduranceTerminalStateIfNeeded();
            return;
        }
        if (_state == SalvageRepairSliceState.Failed)
        {
            _enduranceSoakRuntime.Fail("gameplay state entered Failed during endurance run");
            ReportEnduranceTerminalStateIfNeeded();
            return;
        }

        _enduranceSoakSampleAccumulator += Math.Max(0.0, delta);
        if (_enduranceSoakSampleAccumulator >= EnduranceSoakPolicy.SampleIntervalSeconds)
        {
            double observedDelta = _enduranceSoakSampleAccumulator;
            _enduranceSoakSampleAccumulator = 0.0;
            _enduranceSoakRuntime.Observe(observedDelta, CaptureEnduranceObservation());
        }

        while (_enduranceSoakRuntime.HeartbeatDue &&
            _enduranceSoakRuntime.State == EnduranceSoakRunState.Running)
        {
            _enduranceSoakRuntime.MarkHeartbeat();
            WriteEnduranceArtifacts(checkpoint: false);
        }

        if (_enduranceSoakRuntime.WorkloadDue && _enduranceSoakWorkloadTask is null)
        {
            int pulse = ++_enduranceSoakWorkloadIndex;
            _enduranceSoakWorkloadTask = Task.Run(
                () => EnduranceSyntheticWorkloadRuntime.Run(pulse),
                _lifetimeCancellation.Token);
        }

        if (_enduranceSoakRuntime.PersistenceCheckpointDue &&
            _enduranceSoakPersistenceTask is null &&
            _enduranceSoakDatabaseInitTask is null &&
            _enduranceSoakDatabase is not null)
        {
            int revision = ++_enduranceSoakPersistenceRevision;
            SaveGameSnapshot snapshot = SaveDatabase.CreateAcceptanceSnapshot(
                "task214.soak",
                revision,
                playerOffset: revision * 0.125,
                oreQuantity: 20 + revision % 71,
                visitCount: 1 + revision % 11);
            _enduranceSoakPersistenceTask = _enduranceSoakDatabase.SaveAsync(
                snapshot,
                _lifetimeCancellation.Token);
        }

        if (_enduranceSoakRuntime.DatabaseIntegrityDue &&
            _enduranceSoakDiagnosticsTask is null &&
            _enduranceSoakPersistenceTask is null &&
            _enduranceSoakDatabaseInitTask is null &&
            _enduranceSoakDatabase is not null)
        {
            _enduranceSoakDiagnosticsTask = _enduranceSoakDatabase.ReadDiagnosticsAsync(
                "task214.soak",
                _lifetimeCancellation.Token);
        }

        _enduranceSoakRuntime.TryComplete();
        ReportEnduranceTerminalStateIfNeeded();
    }

    private EnduranceSoakObservation CaptureEnduranceObservation()
    {
        TerrainChunkProfilerSnapshot? terrain =
            _planetSurfaceStreamer is not null && GodotObject.IsInstanceValid(_planetSurfaceStreamer)
                ? _planetSurfaceStreamer.CaptureProfilerSnapshot()
                : null;
        WorldStreamingDiagnostics? world =
            _worldStreamingCoordinator is not null && GodotObject.IsInstanceValid(_worldStreamingCoordinator)
                ? _worldStreamingCoordinator.CreateDiagnostics()
                : null;
        SaveDatabaseDiagnostics? database = _enduranceSoakLastDatabaseDiagnostics;
        return new EnduranceSoakObservation(
            GC.GetTotalMemory(false),
            terrain?.FailedJobs ?? 0,
            terrain?.QueuedWork ?? 0,
            terrain?.CompletedRevision ?? 0,
            world?.QueuedOperations ?? 0,
            world?.CompletedRevision ?? 0,
            _enduranceSoakDatabase?.QueuedWrites ?? 0,
            _enduranceSoakDatabase?.CompletedWrites ?? 0,
            Math.Max(
                _enduranceSoakDatabase?.MaximumConcurrentWriters ?? 0,
                database?.MaximumConcurrentWriters ?? 0),
            database is not null,
            database is null || string.Equals(
                database.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase),
            _enduranceSoakPersistenceRevision);
    }

    private void PollEnduranceSoakAsyncWork()
    {
        if (_enduranceSoakDatabaseInitTask is { IsCompleted: true } initTask)
        {
            _enduranceSoakDatabaseInitTask = null;
            try
            {
                _enduranceSoakLastDatabaseDiagnostics = initTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _enduranceSoakRuntime.Fail($"diagnostic database initialize failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (_enduranceSoakWorkloadTask is { IsCompleted: true } workloadTask)
        {
            _enduranceSoakWorkloadTask = null;
            try
            {
                EnduranceSyntheticWorkloadResult result = workloadTask.GetAwaiter().GetResult();
                _enduranceSoakRuntime.MarkWorkload(result.Passed, result.Signature);
            }
            catch (Exception exception)
            {
                _enduranceSoakRuntime.Fail($"synthetic workload failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (_enduranceSoakPersistenceTask is { IsCompleted: true } persistenceTask)
        {
            _enduranceSoakPersistenceTask = null;
            try
            {
                persistenceTask.GetAwaiter().GetResult();
                _enduranceSoakRuntime.MarkPersistenceCheckpoint(true);
                WriteEnduranceArtifacts(checkpoint: true);
            }
            catch (Exception exception)
            {
                _enduranceSoakRuntime.MarkPersistenceCheckpoint(false);
                _enduranceSoakRuntime.Fail($"diagnostic save failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (_enduranceSoakDiagnosticsTask is { IsCompleted: true } diagnosticsTask)
        {
            _enduranceSoakDiagnosticsTask = null;
            try
            {
                SaveDatabaseDiagnostics diagnostics = diagnosticsTask.GetAwaiter().GetResult();
                _enduranceSoakLastDatabaseDiagnostics = diagnostics;
                bool ok = string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase) &&
                    diagnostics.MaximumConcurrentWriters <= EnduranceSoakPolicy.MaximumConcurrentDatabaseWriters;
                _enduranceSoakRuntime.MarkDatabaseIntegrity(ok);
            }
            catch (Exception exception)
            {
                _enduranceSoakRuntime.MarkDatabaseIntegrity(false);
                _enduranceSoakRuntime.Fail($"diagnostic integrity read failed: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private DeveloperCommandResult DeveloperEnduranceSoak(string[] parts)
    {
        if (parts.Length < 2 || string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase))
        {
            return new DeveloperCommandResult(
                true,
                _enduranceSoakRuntime.CreateSnapshot().BuildCompactStatus());
        }

        if (string.Equals(parts[1], "stop", StringComparison.OrdinalIgnoreCase))
        {
            if (_enduranceSoakRuntime.State != EnduranceSoakRunState.Running)
            {
                return new DeveloperCommandResult(false, "endurance soak is not running");
            }
            _enduranceSoakRuntime.Cancel("operator stop");
            WriteEnduranceArtifacts(checkpoint: true);
            return new DeveloperCommandResult(true, "endurance soak cancelled safely");
        }

        if (!string.Equals(parts[1], "start", StringComparison.OrdinalIgnoreCase))
        {
            return new DeveloperCommandResult(false, "usage: endurance_soak <start [hours]|status|stop>");
        }
        double hours = EnduranceSoakPolicy.RequiredCertificationHours;
        if (parts.Length >= 3 && !double.TryParse(
            parts[2],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out hours))
        {
            return new DeveloperCommandResult(false, "hours must be numeric, for example: endurance_soak start 8");
        }
        if (_state is not (SalvageRepairSliceState.Ready or SalvageRepairSliceState.Passed))
        {
            return new DeveloperCommandResult(false, "endurance soak requires a loaded Ready or Passed gameplay state");
        }
        StartEnduranceSoak(hours);
        return new DeveloperCommandResult(
            true,
            $"endurance soak started for {EnduranceSoakPolicy.NormalizeRequestedHours(hours):0.00}h; " +
            $"certification={(EnduranceSoakPolicy.NormalizeRequestedHours(hours) >= 8.0 ? 1 : 0)}; " +
            $"heartbeat={_enduranceSoakHeartbeatPath}");
    }

    private void StartEnduranceSoak(double requestedHours)
    {
        if (_enduranceSoakRuntime.State == EnduranceSoakRunState.Running)
        {
            return;
        }
        if ((_enduranceSoakDatabaseInitTask is not null && !_enduranceSoakDatabaseInitTask.IsCompleted) ||
            (_enduranceSoakPersistenceTask is not null && !_enduranceSoakPersistenceTask.IsCompleted) ||
            (_enduranceSoakDiagnosticsTask is not null && !_enduranceSoakDiagnosticsTask.IsCompleted))
        {
            GD.PushWarning("TASK-214 cannot restart while previous diagnostic DB work is still completing.");
            return;
        }

        _enduranceSoakDatabase?.Dispose();
        _enduranceSoakDatabase = null;
        _enduranceSoakLastDatabaseDiagnostics = null;
        _enduranceSoakPersistenceRevision = 0;
        _enduranceSoakWorkloadIndex = 0;
        _enduranceSoakSampleAccumulator = 0.0;
        _enduranceSoakRunId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        _enduranceSoakArtifactDirectory = ProjectSettings.GlobalizePath("user://diagnostics");
        Directory.CreateDirectory(_enduranceSoakArtifactDirectory);
        _enduranceSoakHeartbeatPath = Path.Combine(
            _enduranceSoakArtifactDirectory,
            $"task214-endurance-{_enduranceSoakRunId}.jsonl");
        _enduranceSoakLatestPath = Path.Combine(
            _enduranceSoakArtifactDirectory,
            "task214-endurance-latest.json");
        string databasePath = Path.Combine(
            _enduranceSoakArtifactDirectory,
            $"task214-endurance-{_enduranceSoakRunId}.db");
        _enduranceSoakDatabase = new SaveDatabase(databasePath);
        _enduranceSoakDatabaseInitTask = _enduranceSoakDatabase.InitializeAsync(
            _lifetimeCancellation.Token);
        _enduranceSoakRuntime.Start(
            requestedHours,
            CaptureEnduranceObservation());
        _enduranceSoakLastReportedState = EnduranceSoakRunState.Running;
        WriteEnduranceArtifacts(checkpoint: true);
        GD.Print(
            "TASK-214 endurance soak START: " +
            $"run={_enduranceSoakRunId}; target={_enduranceSoakRuntime.TargetSeconds / 3600.0:0.00}h; " +
            $"certification={(EnduranceSoakPolicy.IsCertificationDuration(_enduranceSoakRuntime.TargetSeconds) ? 1 : 0)}; " +
            "primarySaveMutation=0; diagnosticDatabase=isolated; " +
            $"heartbeat={_enduranceSoakHeartbeatPath}.");
    }

    private void DetectInterruptedEnduranceRun()
    {
        try
        {
            if (!File.Exists(_enduranceSoakLatestPath))
            {
                return;
            }
            string previous = File.ReadAllText(_enduranceSoakLatestPath);
            if (previous.Contains("\"state\":\"Running\"", StringComparison.Ordinal))
            {
                GD.PushWarning(
                    "TASK-214 previous endurance interruption DETECTED: latest checkpoint was Running; " +
                    "previous run is not certifiable and a new uninterrupted 8h run is required.");
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"TASK-214 recovery marker read skipped: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void WriteEnduranceArtifacts(bool checkpoint)
    {
        try
        {
            EnduranceSoakSnapshot snapshot = _enduranceSoakRuntime.CreateSnapshot();
            var payload = new
            {
                task = "TASK-214",
                runId = _enduranceSoakRunId,
                timestampUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                state = snapshot.State.ToString(),
                certification = snapshot.CertificationDuration,
                certificationPassed = snapshot.CertificationPassed,
                elapsedSeconds = Math.Round(snapshot.ElapsedSeconds, 3),
                targetSeconds = Math.Round(snapshot.TargetSeconds, 3),
                samples = snapshot.Samples,
                heartbeats = snapshot.Heartbeats,
                workloads = snapshot.WorkloadPulses,
                checkpoints = snapshot.PersistenceCheckpoints,
                databaseChecks = snapshot.DatabaseIntegrityChecks,
                managedBaselineBytes = snapshot.BaselineManagedMemoryBytes,
                managedPeakBytes = snapshot.PeakManagedMemoryBytes,
                managedGrowthBytes = snapshot.ManagedMemoryGrowthBytes,
                terrainFailureDelta = snapshot.TerrainFailureDelta,
                queueStallSeconds = Math.Round(snapshot.QueueStallSeconds, 3),
                failure = snapshot.LastFailureReason,
                workloadSignature = snapshot.LastWorkloadSignature,
                checkpoint
            };
            string json = JsonSerializer.Serialize(payload);
            File.AppendAllText(_enduranceSoakHeartbeatPath, json + Environment.NewLine);
            string temporary = _enduranceSoakLatestPath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _enduranceSoakLatestPath, overwrite: true);
        }
        catch (Exception exception)
        {
            _enduranceSoakRuntime.Fail(
                $"heartbeat/checkpoint artifact write failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ReportEnduranceTerminalStateIfNeeded()
    {
        EnduranceSoakSnapshot snapshot = _enduranceSoakRuntime.CreateSnapshot();
        if (snapshot.State is EnduranceSoakRunState.Idle or EnduranceSoakRunState.Running ||
            snapshot.State == _enduranceSoakLastReportedState)
        {
            return;
        }
        _enduranceSoakLastReportedState = snapshot.State;
        WriteEnduranceArtifacts(checkpoint: true);
        if (snapshot.State == EnduranceSoakRunState.Passed)
        {
            string prefix = snapshot.CertificationPassed
                ? "TASK-214 eight-hour endurance CERTIFICATION PASS: "
                : "TASK-214 endurance smoke PASS: ";
            GD.Print(
                prefix +
                $"run={_enduranceSoakRunId}; duration={snapshot.ElapsedSeconds / 3600.0:0.00}h; " +
                $"samples={snapshot.Samples}; heartbeats={snapshot.Heartbeats}; workloads={snapshot.WorkloadPulses}; " +
                $"checkpoints={snapshot.PersistenceCheckpoints}; dbChecks={snapshot.DatabaseIntegrityChecks}; " +
                $"managedGrowth={snapshot.ManagedMemoryGrowthBytes / (1024.0 * 1024.0):0.0}MiB; " +
                $"terrainFailures={snapshot.TerrainFailureDelta}; ownerCertification={(snapshot.CertificationPassed ? 1 : 0)}.");
        }
        else if (snapshot.State == EnduranceSoakRunState.Failed)
        {
            GD.PushError(
                "TASK-214 endurance soak FAIL: " +
                $"run={_enduranceSoakRunId}; elapsed={snapshot.ElapsedSeconds / 3600.0:0.00}h; " +
                $"reason={snapshot.LastFailureReason}; heartbeat={_enduranceSoakHeartbeatPath}.");
        }
        else if (snapshot.State == EnduranceSoakRunState.Cancelled)
        {
            GD.Print(
                "TASK-214 endurance soak CANCELLED: " +
                $"run={_enduranceSoakRunId}; elapsed={snapshot.ElapsedSeconds / 3600.0:0.00}h; " +
                $"reason={snapshot.LastFailureReason}; certification=0.");
        }
    }

    private void DisposeEnduranceSoakRuntime()
    {
        if (_enduranceSoakRuntime.State == EnduranceSoakRunState.Running)
        {
            _enduranceSoakRuntime.Cancel("gameplay tree exit before certification completed");
            WriteEnduranceArtifacts(checkpoint: true);
        }
        _enduranceSoakDatabase?.Dispose();
        _enduranceSoakDatabase = null;
    }

    private void RunEnduranceSoakAcceptance()
    {
        EnduranceSoakAcceptanceReport report = EnduranceSoakAcceptanceRunner.Run();
        _enduranceSoakAcceptancePassed = report.Passed;
        _enduranceSoakAcceptanceHud = report.Passed
            ? $"PASS harness=1 duration=8h owner=RUN-REQUIRED hb={report.VirtualHeartbeats}"
            : "FAIL endurance harness";
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
