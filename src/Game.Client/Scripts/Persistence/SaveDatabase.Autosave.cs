using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class SaveAutosaveCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly SaveDatabase _database;
    private readonly IDomainEventBus _eventBus;
    private readonly TimeSpan _coalescingWindow;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<AutosaveTrigger> _pendingTriggers = new();
    private readonly HashSet<AutosaveTrigger> _observedTriggers = new();
    private Task? _workerTask;
    private SaveGameSnapshot? _pendingSnapshot;
    private int _requestedSaves;
    private int _completedBatches;
    private int _coalescedRequests;
    private int _failedBatches;
    private int _lastSavedRevision;
    private string _lastCompletedTriggerSummary = "none";
    private string _lastErrorMessage = string.Empty;
    private bool _disposed;

    public SaveAutosaveCoordinator(
        SaveDatabase database,
        IDomainEventBus eventBus,
        TimeSpan? coalescingWindow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _coalescingWindow = coalescingWindow ?? TimeSpan.FromMilliseconds(120.0);
        if (_coalescingWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coalescingWindow),
                "Coalescing window must not be negative.");
        }

        string directory = Path.GetDirectoryName(_database.DatabasePath) ??
            throw new InvalidOperationException(
                "Autosave log directory could not be resolved.");
        string baseName = Path.GetFileNameWithoutExtension(_database.DatabasePath);
        AutosaveLogPath = Path.Combine(
            directory,
            "logs",
            $"{baseName}.autosave.log");
    }

    public string AutosaveLogPath { get; }

    public int RequestedSaves
    {
        get
        {
            lock (_sync)
            {
                return _requestedSaves;
            }
        }
    }

    public int CompletedBatches
    {
        get
        {
            lock (_sync)
            {
                return _completedBatches;
            }
        }
    }

    public int CoalescedRequests
    {
        get
        {
            lock (_sync)
            {
                return _coalescedRequests;
            }
        }
    }

    public int FailedBatches
    {
        get
        {
            lock (_sync)
            {
                return _failedBatches;
            }
        }
    }

    public int LastSavedRevision
    {
        get
        {
            lock (_sync)
            {
                return _lastSavedRevision;
            }
        }
    }

    public string LastCompletedTriggerSummary
    {
        get
        {
            lock (_sync)
            {
                return _lastCompletedTriggerSummary;
            }
        }
    }

    public string LastErrorMessage
    {
        get
        {
            lock (_sync)
            {
                return _lastErrorMessage;
            }
        }
    }

    public int ObservedTriggerTypes
    {
        get
        {
            lock (_sync)
            {
                return _observedTriggers.Count;
            }
        }
    }

    public bool HasObservedTrigger(AutosaveTrigger trigger)
    {
        lock (_sync)
        {
            return _observedTriggers.Contains(trigger);
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _workerTask is not null || _pendingSnapshot is not null;
            }
        }
    }

    public void Request(
        AutosaveTrigger trigger,
        SaveGameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SaveGameSnapshot frozenSnapshot = FreezeSnapshot(snapshot);
        lock (_sync)
        {
            ThrowIfDisposed();
            _requestedSaves++;
            _observedTriggers.Add(trigger);
            _lastErrorMessage = string.Empty;
            if (_pendingSnapshot is not null)
            {
                _coalescedRequests++;
            }

            _pendingSnapshot = frozenSnapshot;
            _pendingTriggers.Add(trigger);
            if (_workerTask is null)
            {
                _workerTask = Task.Run(() => RunWorkerAsync(_lifetimeCancellation.Token));
            }
        }

        _eventBus.Publish(new SaveRequested(
            frozenSnapshot.SlotId,
            frozenSnapshot.Revision,
            trigger.ToString(),
            DateTimeOffset.UtcNow));
    }

    public async Task FlushAsync(
        AutosaveTrigger trigger,
        SaveGameSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        Request(trigger, snapshot);
        await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushPendingAsync(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task? worker;
            lock (_sync)
            {
                ThrowIfDisposed();
                worker = _workerTask;
            }

            if (worker is null)
            {
                break;
            }

            await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        string error;
        lock (_sync)
        {
            error = _lastErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(
                $"Autosave batch failed: {error}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private static SaveGameSnapshot FreezeSnapshot(
        SaveGameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Inventory);
        return snapshot with
        {
            Inventory = snapshot.Inventory.ToArray()
        };
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                if (_coalescingWindow > TimeSpan.Zero)
                {
                    await Task.Delay(
                        _coalescingWindow,
                        cancellationToken).ConfigureAwait(false);
                }

                SaveGameSnapshot? snapshot;
                AutosaveTrigger[] triggers;
                lock (_sync)
                {
                    snapshot = _pendingSnapshot;
                    triggers = _pendingTriggers
                        .OrderBy(trigger => (int)trigger)
                        .ToArray();
                    _pendingSnapshot = null;
                    _pendingTriggers.Clear();
                }

                if (snapshot is null || triggers.Length == 0)
                {
                    return;
                }

                string triggerSummary = string.Join(",", triggers);
                try
                {
                    await _database.SaveAsync(
                        snapshot,
                        cancellationToken).ConfigureAwait(false);
                    if (!TryAppendLog(
                        "AUTOSAVE_COMPLETED",
                        $"revision={snapshot.Revision}; triggers={triggerSummary}; " +
                        $"requested={RequestedSaves}; coalesced={CoalescedRequests}",
                        out string logError))
                    {
                        throw new IOException(logError);
                    }

                    lock (_sync)
                    {
                        _completedBatches++;
                        _lastSavedRevision = snapshot.Revision;
                        _lastCompletedTriggerSummary = triggerSummary;
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    RecordBatchFailure(snapshot, triggerSummary, exception);
                }

                lock (_sync)
                {
                    if (_pendingSnapshot is null)
                    {
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Disposal intentionally cancels a pending coalescing delay or write.
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _failedBatches++;
                _lastErrorMessage =
                    $"{exception.GetType().Name}: {exception.Message}";
            }
        }
        finally
        {
            lock (_sync)
            {
                _workerTask = null;
                if (!_disposed && _pendingSnapshot is not null)
                {
                    _workerTask = Task.Run(() => RunWorkerAsync(cancellationToken));
                }
            }
        }
    }

    private void RecordBatchFailure(
        SaveGameSnapshot snapshot,
        string triggerSummary,
        Exception exception)
    {
        _ = TryAppendLog(
            "AUTOSAVE_FAILED",
            $"revision={snapshot.Revision}; triggers={triggerSummary}; " +
            $"error={exception.GetType().Name}: {exception.Message}",
            out _);
        lock (_sync)
        {
            _failedBatches++;
            _lastErrorMessage =
                $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    private bool TryAppendLog(
        string eventName,
        string details,
        out string error)
    {
        try
        {
            string? directory = Path.GetDirectoryName(AutosaveLogPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "Autosave log directory could not be resolved.");
            }

            Directory.CreateDirectory(directory);
            string line =
                $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} " +
                $"{eventName} {details}{Environment.NewLine}";
            File.AppendAllText(AutosaveLogPath, line);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error =
                $"autosave log failed: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SaveAutosaveCoordinator));
        }
    }
}

public sealed partial class SaveDatabase
{
    public async Task<SaveAutosaveAcceptanceReport> RunAutosaveAcceptanceAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string testPath = BuildSiblingPath(".autosave-test.db");
        using SaveDatabase testDatabase = new(testPath);
        using SaveAutosaveCoordinator coordinator = new(
            testDatabase,
            new DomainEventBus(),
            TimeSpan.FromMilliseconds(80.0));

        try
        {
            await Task.Run(
                () =>
                {
                    testDatabase.DeleteDatabaseFamilyCore();
                    DeleteIfExistsCore(coordinator.AutosaveLogPath);
                },
                cancellationToken).ConfigureAwait(false);

            await testDatabase.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await testDatabase.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);

            SaveGameSnapshot periodicSnapshot = CreateAcceptanceSnapshot(
                slotId,
                revision: 20,
                playerOffset: 20.0,
                oreQuantity: 70,
                visitCount: 4);
            coordinator.Request(AutosaveTrigger.Periodic, periodicSnapshot);
            await coordinator.FlushPendingAsync(cancellationToken)
                .ConfigureAwait(false);

            AutosaveTrigger[] eventTriggers =
            {
                AutosaveTrigger.Landing,
                AutosaveTrigger.Takeoff,
                AutosaveTrigger.Hyperspace,
                AutosaveTrigger.QuestCompleted,
                AutosaveTrigger.ShipPurchased,
                AutosaveTrigger.BaseChanged,
                AutosaveTrigger.DiscoveryChanged,
                AutosaveTrigger.ShipChanged
            };

            for (int index = 0; index < eventTriggers.Length; index++)
            {
                SaveGameSnapshot eventSnapshot = CreateAcceptanceSnapshot(
                    slotId,
                    revision: 21 + index,
                    playerOffset: 21.0 + index,
                    oreQuantity: 71 + index,
                    visitCount: 5 + index);
                coordinator.Request(eventTriggers[index], eventSnapshot);
            }

            SaveGameSnapshot gracefulExitSnapshot = CreateAcceptanceSnapshot(
                slotId,
                revision: 29,
                playerOffset: 29.0,
                oreQuantity: 79,
                visitCount: 13);
            await coordinator.FlushAsync(
                AutosaveTrigger.GracefulExit,
                gracefulExitSnapshot,
                cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot? loaded = await testDatabase.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SnapshotsEqual(
                gracefulExitSnapshot,
                loaded,
                out string mismatch);
            SaveDatabaseDiagnostics diagnostics =
                await testDatabase.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);

            bool periodicTriggered = coordinator.HasObservedTrigger(
                AutosaveTrigger.Periodic);
            bool gracefulExitFlushed =
                loaded?.Revision == gracefulExitSnapshot.Revision &&
                coordinator.LastCompletedTriggerSummary.Contains(
                    nameof(AutosaveTrigger.GracefulExit),
                    StringComparison.Ordinal);
            string autosaveLog = File.Exists(coordinator.AutosaveLogPath)
                ? File.ReadAllText(coordinator.AutosaveLogPath)
                : string.Empty;
            bool logWritten =
                autosaveLog.Contains("AUTOSAVE_COMPLETED", StringComparison.Ordinal) &&
                autosaveLog.Contains(nameof(AutosaveTrigger.Periodic), StringComparison.Ordinal) &&
                autosaveLog.Contains(nameof(AutosaveTrigger.GracefulExit), StringComparison.Ordinal);
            bool triggerCoverage =
                coordinator.ObservedTriggerTypes ==
                Enum.GetValues<AutosaveTrigger>().Length;
            bool integrityPassed = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            bool passed =
                exactRoundTrip &&
                periodicTriggered &&
                gracefulExitFlushed &&
                logWritten &&
                triggerCoverage &&
                coordinator.RequestedSaves == 10 &&
                coordinator.CompletedBatches == 2 &&
                coordinator.CoalescedRequests == 8 &&
                coordinator.FailedBatches == 0 &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                integrityPassed;

            List<string> failedCriteria = new();
            if (!exactRoundTrip)
            {
                failedCriteria.Add($"roundTrip={mismatch}");
            }

            if (!periodicTriggered)
            {
                failedCriteria.Add("periodic=0");
            }

            if (!gracefulExitFlushed)
            {
                failedCriteria.Add("gracefulExit=0");
            }

            if (!logWritten)
            {
                failedCriteria.Add("logWritten=0");
            }

            if (!triggerCoverage)
            {
                failedCriteria.Add(
                    $"triggerTypes={coordinator.ObservedTriggerTypes}");
            }

            if (coordinator.RequestedSaves != 8)
            {
                failedCriteria.Add($"requested={coordinator.RequestedSaves}");
            }

            if (coordinator.CompletedBatches != 2)
            {
                failedCriteria.Add($"batches={coordinator.CompletedBatches}");
            }

            if (coordinator.CoalescedRequests != 6)
            {
                failedCriteria.Add(
                    $"coalesced={coordinator.CoalescedRequests}");
            }

            if (coordinator.FailedBatches != 0)
            {
                failedCriteria.Add($"failed={coordinator.FailedBatches}");
            }

            if (diagnostics.MaximumConcurrentWriters != 1)
            {
                failedCriteria.Add(
                    $"maxWriters={diagnostics.MaximumConcurrentWriters}");
            }

            if (!integrityPassed)
            {
                failedCriteria.Add($"integrity={diagnostics.IntegrityResult}");
            }

            stopwatch.Stop();
            return new SaveAutosaveAcceptanceReport(
                passed,
                passed
                    ? "periodic and gameplay-event autosaves coalesced; graceful exit flushed the latest snapshot"
                    : $"autosave criteria failed: {string.Join(", ", failedCriteria)}",
                loaded,
                diagnostics,
                coordinator.ObservedTriggerTypes,
                coordinator.RequestedSaves,
                coordinator.CompletedBatches,
                coordinator.CoalescedRequests,
                periodicTriggered,
                gracefulExitFlushed,
                exactRoundTrip,
                logWritten,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new SaveAutosaveAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                null,
                testDatabase.EmptyDiagnostics(),
                coordinator.ObservedTriggerTypes,
                coordinator.RequestedSaves,
                coordinator.CompletedBatches,
                coordinator.CoalescedRequests,
                false,
                false,
                false,
                File.Exists(coordinator.AutosaveLogPath),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
