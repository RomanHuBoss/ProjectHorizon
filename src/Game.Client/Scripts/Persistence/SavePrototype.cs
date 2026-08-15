using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public enum SavePrototypeState
{
    Initializing = 0,
    Ready = 1,
    Saving = 2,
    Loading = 3,
    Resetting = 4,
    BackingUp = 5,
    Recovering = 6,
    Testing = 7,
    Passed = 8,
    Failed = 9
}

public enum SavePrototypeHudMode
{
    Compact = 0,
    Detailed = 1,
    Hidden = 2
}

public partial class SavePrototype : Node3D
{
    private sealed record GracefulExitResult(
        bool SnapshotWritten,
        int Revision);

    private const string SlotId = "save_1";

    [Export(PropertyHint.Range, "420.0,1000.0,10.0")]
    public float HudCompactWidth { get; set; } = 720.0f;

    [Export(PropertyHint.Range, "180.0,560.0,10.0")]
    public float HudCompactHeight { get; set; } = 510.0f;

    [Export(PropertyHint.Range, "520.0,1200.0,10.0")]
    public float HudDetailedWidth { get; set; } = 820.0f;

    [Export(PropertyHint.Range, "320.0,900.0,10.0")]
    public float HudDetailedHeight { get; set; } = 650.0f;

    [Export(PropertyHint.Range, "5.0,600.0,5.0")]
    public double AutosaveIntervalSeconds { get; set; } = 60.0;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private SaveDatabase? _database;
    private Task<SaveDatabaseDiagnostics>? _initializeTask;
    private Task<SaveGameSnapshot?>? _loadTask;
    private Task<SavePrototypeRefresh>? _refreshTask;
    private Task? _writeTask;
    private Task<SavePrototypeAcceptanceReport>? _acceptanceTask;
    private Task<SaveBackupReport>? _backupTask;
    private Task<SaveRecoveryReport>? _recoveryTask;
    private Task<SaveRecoveryAcceptanceReport>? _recoveryAcceptanceTask;
    private Task<SaveMigrationAcceptanceReport>? _migrationAcceptanceTask;
    private Task<SaveAutosaveAcceptanceReport>? _autosaveAcceptanceTask;
    private Task<GracefulExitResult>? _gracefulExitTask;
    private SaveAutosaveCoordinator? _autosaveCoordinator;
    private SavePrototypeState _state = SavePrototypeState.Initializing;
    private SavePrototypeHudMode _hudMode = SavePrototypeHudMode.Compact;
    private SaveGameSnapshot? _loadedSnapshot;
    private SaveDatabaseDiagnostics? _diagnostics;
    private SavePrototypeAcceptanceReport? _acceptanceReport;
    private SaveBackupReport? _backupReport;
    private SaveRecoveryReport? _recoveryReport;
    private SaveRecoveryAcceptanceReport? _recoveryAcceptanceReport;
    private SaveMigrationAcceptanceReport? _migrationAcceptanceReport;
    private SaveAutosaveAcceptanceReport? _autosaveAcceptanceReport;
    private int _manualRevision;
    private int _observedAutosaveBatches;
    private double _autosaveElapsedSeconds;
    private bool _gracefulExitRequested;
    private bool _previousAutoAcceptQuit = true;
    private string _statusMessage = "инициализация SQLite";
    private string _slotOperationHud = "READY";
    private string _backupOperationHud = "READY";
    private string _recoveryOperationHud = "READY";
    private string _autosaveOperationHud = "READY";
    private string _writeCompletionHud = "PASS";
    private string _refreshCompletionMessage = "SQLite READY";
    private SavePrototypeState _refreshCompletionState =
        SavePrototypeState.Ready;
    private string _databaseDisplayPath = string.Empty;

    private MarginContainer? _compactMargin;
    private Label? _compactLabel;
    private MarginContainer? _detailedMargin;
    private Label? _detailedLabel;
    private PanelContainer? _hiddenHint;
    private Node3D? _playerMarker;
    private Node3D? _shipMarker;
    private MeshInstance3D? _visitedPlanetMarker;

    public override void _Ready()
    {
        _compactMargin = GetNodeOrNull<MarginContainer>("Hud/CompactMargin");
        _compactLabel = GetNodeOrNull<Label>(
            "Hud/CompactMargin/PanelContainer/Label");
        _detailedMargin = GetNodeOrNull<MarginContainer>("Hud/DetailedMargin");
        _detailedLabel = GetNodeOrNull<Label>(
            "Hud/DetailedMargin/PanelContainer/ScrollContainer/Label");
        _hiddenHint = GetNodeOrNull<PanelContainer>("Hud/HiddenHint");
        _playerMarker = GetNodeOrNull<Node3D>("SaveVisualization/PlayerMarker");
        _shipMarker = GetNodeOrNull<Node3D>("SaveVisualization/ShipMarker");
        _visitedPlanetMarker = GetNodeOrNull<MeshInstance3D>(
            "SaveVisualization/VisitedPlanetMarker");

        if (_compactMargin is null || _compactLabel is null ||
            _detailedMargin is null || _detailedLabel is null ||
            _hiddenHint is null || _playerMarker is null ||
            _shipMarker is null || _visitedPlanetMarker is null)
        {
            throw new InvalidOperationException(
                "SavePrototype scene is missing HUD or visualization nodes.");
        }

        string userDirectory = ProjectSettings.GlobalizePath("user://");
        string databasePath = System.IO.Path.Combine(
            userDirectory,
            "profiles",
            "profile_prototype",
            "save_1.db");
        _databaseDisplayPath = databasePath;
        SaveDatabase database = new(databasePath);
        _database = database;
        _autosaveCoordinator = new SaveAutosaveCoordinator(
            database,
            new DomainEventBus());
        _initializeTask = database.InitializeAsync(_lifetimeCancellation.Token);

        SceneTree tree = GetTree();
        _previousAutoAcceptQuit = tree.AutoAcceptQuit;
        tree.AutoAcceptQuit = false;
        GetViewport().SizeChanged += UpdateHudLayout;
        ApplyHudMode();
        UpdateHud();
        GD.Print(
            "Prototype E SQLite autosave/migration/recovery initializing. " +
            "Press F6 for autosave/graceful-exit acceptance after READY.");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            BeginGracefulExit();
        }
    }

    public override void _ExitTree()
    {
        if (GetViewport() is Viewport viewport)
        {
            viewport.SizeChanged -= UpdateHudLayout;
        }

        GetTree().AutoAcceptQuit = _previousAutoAcceptQuit;
        _lifetimeCancellation.Cancel();
        _autosaveCoordinator?.Dispose();
        _database?.Dispose();
        _lifetimeCancellation.Dispose();
    }

    public override void _Process(double delta)
    {
        PollInitializeTask();
        PollWriteTask();
        PollLoadTask();
        PollAcceptanceTask();
        PollBackupTask();
        PollRecoveryTask();
        PollRecoveryAcceptanceTask();
        PollMigrationAcceptanceTask();
        PollAutosaveAcceptanceTask();
        PollAutosaveCoordinator();
        PollGracefulExitTask();
        PollRefreshTask();
        UpdateAutosaveTimer(delta);
        UpdateHud();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo)
        {
            return;
        }

        Key physical = keyEvent.PhysicalKeycode;
        Key logical = keyEvent.Keycode;

        if (Matches(physical, logical, Key.H))
        {
            _hudMode = (SavePrototypeHudMode)(((int)_hudMode + 1) % 3);
            ApplyHudMode();
            GD.Print($"Save prototype HUD mode: {_hudMode}");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!CanStartOperation())
        {
            return;
        }

        if (Matches(physical, logical, Key.S))
        {
            BeginManualSave();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.L))
        {
            BeginManualLoad();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.R))
        {
            BeginReset();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.B))
        {
            BeginBackup();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.Y))
        {
            BeginRestoreBackup();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.F6))
        {
            BeginAutosaveAcceptanceTest();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.C))
        {
            BeginMigrationAcceptanceTest();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.X))
        {
            BeginRecoveryAcceptanceTest();
            GetViewport().SetInputAsHandled();
        }
        else if (Matches(physical, logical, Key.Z))
        {
            BeginAcceptanceTest();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool CanStartOperation()
    {
        return _database is not null &&
            _initializeTask is null &&
            _writeTask is null &&
            _loadTask is null &&
            _refreshTask is null &&
            _acceptanceTask is null &&
            _backupTask is null &&
            _recoveryTask is null &&
            _recoveryAcceptanceTask is null &&
            _migrationAcceptanceTask is null &&
            _autosaveAcceptanceTask is null &&
            _gracefulExitTask is null &&
            !(_autosaveCoordinator?.IsBusy ?? false);
    }

    private void BeginManualSave()
    {
        if (_database is null)
        {
            return;
        }

        _manualRevision++;
        SaveGameSnapshot snapshot = SaveDatabase.CreateAcceptanceSnapshot(
            SlotId,
            _manualRevision,
            playerOffset: _manualRevision * 2.5,
            oreQuantity: 10 + _manualRevision,
            visitCount: Math.Max(1, _manualRevision));
        _loadedSnapshot = snapshot;
        ApplySnapshotToVisualization(snapshot);
        _state = SavePrototypeState.Saving;
        _statusMessage = $"транзакционная запись revision={snapshot.Revision}";
        _slotOperationHud = $"RUNNING save rev={snapshot.Revision}";
        _writeCompletionHud = $"PASS save rev={snapshot.Revision}";
        _writeTask = _database.SaveAsync(
            snapshot,
            _lifetimeCancellation.Token);
    }

    private void BeginManualLoad()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Loading;
        _statusMessage = "чтение snapshot из SQLite";
        _slotOperationHud = "RUNNING load";
        _loadTask = _database.LoadAsync(
            SlotId,
            _lifetimeCancellation.Token);
    }

    private void BeginReset()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Resetting;
        _statusMessage = "очистка slot save_1 транзакцией";
        _slotOperationHud = "RUNNING reset";
        _writeCompletionHud = "PASS reset; slot пуст";
        _writeTask = _database.ResetSlotAsync(
            SlotId,
            _lifetimeCancellation.Token);
        _loadedSnapshot = null;
        _manualRevision = 0;
        ResetVisualization();
    }

    private void BeginAcceptanceTest()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Testing;
        _statusMessage = "migration → save → load → queued writes → integrity";
        _acceptanceReport = null;
        _acceptanceTask = _database.RunAcceptanceAsync(
            SlotId,
            _lifetimeCancellation.Token);
        GD.Print("TASK-054 SQLite save foundation acceptance started.");
    }

    private void BeginBackup()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.BackingUp;
        _statusMessage = "создание и валидация предыдущей копии";
        _backupOperationHud = "RUNNING validated backup";
        _backupReport = null;
        _backupTask = _database.CreateBackupAsync(
            SlotId,
            _lifetimeCancellation.Token);
        GD.Print("Prototype E validated backup creation started.");
    }

    private void BeginRestoreBackup()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Recovering;
        _statusMessage = "атомарное восстановление предыдущей копии";
        _recoveryOperationHud = "RUNNING previous-copy restore";
        _recoveryReport = null;
        _recoveryTask = _database.RestoreBackupAsync(
            SlotId,
            _lifetimeCancellation.Token);
        GD.Print("Prototype E previous-copy restore started.");
    }

    private void BeginRecoveryAcceptanceTest()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Testing;
        _statusMessage =
            "isolated backup → reject invalid candidate → corrupt → atomic recovery";
        _recoveryAcceptanceReport = null;
        _recoveryAcceptanceTask = _database.RunRecoveryAcceptanceAsync(
            SlotId,
            _lifetimeCancellation.Token);
        GD.Print("TASK-056 SQLite backup/recovery acceptance started.");
    }

    private void BeginMigrationAcceptanceTest()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Testing;
        _statusMessage =
            "isolated schema-1 copy → schema-2 migration → aliases/placeholders → round-trip";
        _migrationAcceptanceReport = null;
        _migrationAcceptanceTask = _database.RunMigrationAcceptanceAsync(
            SlotId,
            _lifetimeCancellation.Token);
        GD.Print("TASK-058 SQLite migration/content compatibility acceptance started.");
    }

    private void BeginAutosaveAcceptanceTest()
    {
        if (_database is null)
        {
            return;
        }

        _state = SavePrototypeState.Testing;
        _statusMessage =
            "isolated periodic/events → coalescing → graceful-exit flush → exact load";
        _autosaveAcceptanceReport = null;
        _autosaveAcceptanceTask = _database.RunAutosaveAcceptanceAsync(
            SlotId,
            _lifetimeCancellation.Token);
        GD.Print("TASK-060 SQLite autosave/graceful-exit acceptance started.");
    }

    private void BeginGracefulExit()
    {
        if (_gracefulExitRequested)
        {
            return;
        }

        _gracefulExitRequested = true;
        if (_database is null || _autosaveCoordinator is null)
        {
            GetTree().Quit();
            return;
        }

        SaveGameSnapshot? inMemorySnapshot = _loadedSnapshot;
        Task[] activeTasks = CaptureActivePersistenceTasks();
        _state = SavePrototypeState.Saving;
        _statusMessage =
            "graceful-exit: ожидание активных операций и полного autosave flush";
        _autosaveOperationHud = "RUNNING GracefulExit flush";
        _gracefulExitTask = FlushGracefulExitAsync(activeTasks, _lifetimeCancellation.Token);
        GD.Print(
            "Prototype E graceful-exit flush started: " +
            $"activeTasks={activeTasks.Length}; " +
            $"inMemoryRevision={inMemorySnapshot?.Revision ?? 0}.");
    }

    private Task[] CaptureActivePersistenceTasks()
    {
        List<Task> tasks = new();

        AddIfActive(_initializeTask);
        AddIfActive(_writeTask);
        AddIfActive(_loadTask);
        AddIfActive(_refreshTask);
        AddIfActive(_acceptanceTask);
        AddIfActive(_backupTask);
        AddIfActive(_recoveryTask);
        AddIfActive(_recoveryAcceptanceTask);
        AddIfActive(_migrationAcceptanceTask);
        AddIfActive(_autosaveAcceptanceTask);
        return tasks.ToArray();

        void AddIfActive(Task? task)
        {
            if (task is not null && !task.IsCompleted)
            {
                tasks.Add(task);
            }
        }
    }

    private async Task<GracefulExitResult> FlushGracefulExitAsync(
        Task[] activeTasks,
        CancellationToken cancellationToken)
    {
        SaveDatabase database = _database ??
            throw new InvalidOperationException(
                "Save database is unavailable during graceful exit.");
        SaveAutosaveCoordinator coordinator = _autosaveCoordinator ??
            throw new InvalidOperationException(
                "Autosave coordinator is unavailable during graceful exit.");

        if (activeTasks.Length > 0)
        {
            await Task.WhenAll(activeTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await coordinator.FlushPendingAsync(
            cancellationToken).ConfigureAwait(false);

        SaveGameSnapshot? sourceSnapshot = await database.LoadAsync(
            SlotId,
            cancellationToken).ConfigureAwait(false);
        if (sourceSnapshot is null)
        {
            return new GracefulExitResult(
                SnapshotWritten: false,
                Revision: 0);
        }

        int revision = sourceSnapshot.Revision + 1;
        SaveGameSnapshot exitSnapshot = sourceSnapshot with
        {
            Revision = revision,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture)
        };
        await coordinator.FlushAsync(
            AutosaveTrigger.GracefulExit,
            exitSnapshot,
            cancellationToken).ConfigureAwait(false);
        return new GracefulExitResult(
            SnapshotWritten: true,
            Revision: revision);
    }

    private SaveGameSnapshot BuildNextAutosaveSnapshot(AutosaveTrigger trigger)
    {
        _manualRevision = Math.Max(
            _manualRevision + 1,
            (_loadedSnapshot?.Revision ?? 0) + 1);
        string updatedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);

        if (_loadedSnapshot is not null)
        {
            return _loadedSnapshot with
            {
                Revision = _manualRevision,
                UpdatedUtc = updatedUtc
            };
        }

        int triggerOffset = (int)trigger;
        return SaveDatabase.CreateAcceptanceSnapshot(
            SlotId,
            _manualRevision,
            playerOffset: _manualRevision * 2.5 + triggerOffset,
            oreQuantity: 10 + _manualRevision,
            visitCount: Math.Max(1, _manualRevision));
    }

    private void RequestMainAutosave(AutosaveTrigger trigger)
    {
        if (_autosaveCoordinator is null || _gracefulExitRequested)
        {
            return;
        }

        SaveGameSnapshot snapshot = BuildNextAutosaveSnapshot(trigger);
        _loadedSnapshot = snapshot;
        ApplySnapshotToVisualization(snapshot);
        _state = SavePrototypeState.Saving;
        _statusMessage =
            $"autosave {trigger} revision={snapshot.Revision}";
        _autosaveOperationHud =
            $"RUNNING {trigger} rev={snapshot.Revision}";
        _autosaveCoordinator.Request(trigger, snapshot);
    }

    private void UpdateAutosaveTimer(double delta)
    {
        if (_gracefulExitRequested || _initializeTask is not null ||
            _database is null || _autosaveCoordinator is null ||
            _loadedSnapshot is null || _autosaveAcceptanceTask is not null)
        {
            return;
        }

        _autosaveElapsedSeconds += Math.Max(0.0, delta);
        if (_autosaveElapsedSeconds < AutosaveIntervalSeconds ||
            !CanStartOperation())
        {
            return;
        }

        _autosaveElapsedSeconds = 0.0;
        RequestMainAutosave(AutosaveTrigger.Periodic);
    }

    private void PollAutosaveCoordinator()
    {
        if (_autosaveCoordinator is null || _gracefulExitTask is not null)
        {
            return;
        }

        string error = _autosaveCoordinator.LastErrorMessage;
        if (!string.IsNullOrWhiteSpace(error))
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"autosave failed: {error}";
            _autosaveOperationHud = $"FAIL {error}";
            return;
        }

        int completedBatches = _autosaveCoordinator.CompletedBatches;
        if (_autosaveCoordinator.IsBusy ||
            completedBatches == _observedAutosaveBatches)
        {
            return;
        }

        _observedAutosaveBatches = completedBatches;
        _autosaveElapsedSeconds = 0.0;
        _autosaveOperationHud =
            $"PASS rev={_autosaveCoordinator.LastSavedRevision}, " +
            $"triggers={_autosaveCoordinator.LastCompletedTriggerSummary}, " +
            $"batches={completedBatches}, " +
            $"coalesced={_autosaveCoordinator.CoalescedRequests}";
        GD.Print(
            $"Prototype E autosave PASS: revision=" +
            $"{_autosaveCoordinator.LastSavedRevision}; " +
            $"triggers={_autosaveCoordinator.LastCompletedTriggerSummary}; " +
            $"requests={_autosaveCoordinator.RequestedSaves}; " +
            $"batches={completedBatches}; " +
            $"coalesced={_autosaveCoordinator.CoalescedRequests}");
        BeginRefresh(
            completionMessage: _autosaveOperationHud,
            completionState: SavePrototypeState.Ready);
    }

    private void PollGracefulExitTask()
    {
        if (_gracefulExitTask is null || !_gracefulExitTask.IsCompleted)
        {
            return;
        }

        Task<GracefulExitResult> task = _gracefulExitTask;
        _gracefulExitTask = null;
        try
        {
            GracefulExitResult result = task.GetAwaiter().GetResult();
            _autosaveOperationHud = result.SnapshotWritten
                ? $"PASS GracefulExit rev={result.Revision}"
                : "PASS GracefulExit; slot empty";
            GD.Print(
                "Prototype E graceful-exit autosave PASS: " +
                $"saved={(result.SnapshotWritten ? 1 : 0)}; " +
                $"revision={result.Revision}; " +
                $"pending={((_autosaveCoordinator?.IsBusy ?? false) ? 1 : 0)}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage =
                $"graceful-exit autosave failed: {exception.Message}";
            _autosaveOperationHud = $"FAIL GracefulExit {exception.Message}";
            _gracefulExitRequested = false;
            GD.PushError(
                $"Prototype E graceful-exit autosave failed: {exception}");
        }
    }

    private void PollInitializeTask()
    {
        if (_initializeTask is null || !_initializeTask.IsCompleted)
        {
            return;
        }

        Task<SaveDatabaseDiagnostics> task = _initializeTask;
        _initializeTask = null;
        try
        {
            _diagnostics = task.GetAwaiter().GetResult();
            BeginRefresh(
                completionMessage: "SQLite READY",
                completionState: SavePrototypeState.Ready);
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"init failed: {exception.Message}";
            GD.PushError($"Prototype E initialization failed: {exception}");
        }
    }

    private void PollWriteTask()
    {
        if (_writeTask is null || !_writeTask.IsCompleted)
        {
            return;
        }

        Task task = _writeTask;
        _writeTask = null;
        try
        {
            task.GetAwaiter().GetResult();
            _autosaveElapsedSeconds = 0.0;
            _slotOperationHud = _writeCompletionHud;
            GD.Print(
                $"Prototype E slot operation {_slotOperationHud}; " +
                $"completedWrites={_database?.CompletedWrites ?? 0}");
            BeginRefresh(
                completionMessage: _writeCompletionHud,
                completionState: SavePrototypeState.Ready);
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"write failed: {exception.Message}";
            _slotOperationHud = $"FAIL {exception.Message}";
            GD.PushError($"Prototype E write failed: {exception}");
        }
    }

    private void PollLoadTask()
    {
        if (_loadTask is null || !_loadTask.IsCompleted)
        {
            return;
        }

        Task<SaveGameSnapshot?> task = _loadTask;
        _loadTask = null;
        try
        {
            _loadedSnapshot = task.GetAwaiter().GetResult();
            if (_loadedSnapshot is null)
            {
                _manualRevision = 0;
                _slotOperationHud = "PASS load; slot пуст";
            }
            else
            {
                _manualRevision = Math.Max(
                    _manualRevision,
                    _loadedSnapshot.Revision);
                ApplySnapshotToVisualization(_loadedSnapshot);
                _slotOperationHud =
                    $"PASS load rev={_loadedSnapshot.Revision}";
            }

            GD.Print($"Prototype E slot operation {_slotOperationHud}");
            BeginRefresh(
                completionMessage: _slotOperationHud,
                completionState: SavePrototypeState.Ready);
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"load failed: {exception.Message}";
            _slotOperationHud = $"FAIL {exception.Message}";
            GD.PushError($"Prototype E load failed: {exception}");
        }
    }

    private void PollAcceptanceTask()
    {
        if (_acceptanceTask is null || !_acceptanceTask.IsCompleted)
        {
            return;
        }

        Task<SavePrototypeAcceptanceReport> task = _acceptanceTask;
        _acceptanceTask = null;
        try
        {
            _acceptanceReport = task.GetAwaiter().GetResult();
            _diagnostics = _acceptanceReport.Diagnostics;
            _loadedSnapshot = _acceptanceReport.LoadedSnapshot;
            if (_loadedSnapshot is not null)
            {
                _manualRevision = _loadedSnapshot.Revision;
                ApplySnapshotToVisualization(_loadedSnapshot);
            }

            _state = _acceptanceReport.Passed
                ? SavePrototypeState.Passed
                : SavePrototypeState.Failed;
            _statusMessage = _acceptanceReport.Result;

            string resultLine = BuildAcceptanceOutput(_acceptanceReport);
            if (_acceptanceReport.Passed)
            {
                GD.Print(resultLine);
            }
            else
            {
                GD.PushError(resultLine);
            }
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"test failed: {exception.Message}";
            GD.PushError($"TASK-054 SQLite acceptance failed: {exception}");
        }
    }

    private void PollBackupTask()
    {
        if (_backupTask is null || !_backupTask.IsCompleted)
        {
            return;
        }

        Task<SaveBackupReport> task = _backupTask;
        _backupTask = null;
        try
        {
            _backupReport = task.GetAwaiter().GetResult();
            _state = _backupReport.Succeeded
                ? SavePrototypeState.Ready
                : SavePrototypeState.Failed;
            _statusMessage = _backupReport.Result;
            _backupOperationHud = _backupReport.Succeeded
                ? $"PASS rev={_backupReport.Snapshot?.Revision ?? 0}, " +
                  $"integrity={_backupReport.IntegrityResult}, " +
                  $"atomic={(_backupReport.AtomicReplacementUsed ? 1 : 0)}"
                : $"FAIL {_backupReport.Result}";
            GD.Print(BuildBackupOutput(_backupReport));
            BeginRefresh(
                completionMessage: _backupOperationHud,
                completionState: _backupReport.Succeeded
                    ? SavePrototypeState.Ready
                    : SavePrototypeState.Failed);
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"backup failed: {exception.Message}";
            _backupOperationHud = $"FAIL {exception.Message}";
            GD.PushError($"Prototype E backup failed: {exception}");
        }
    }

    private void PollRecoveryTask()
    {
        if (_recoveryTask is null || !_recoveryTask.IsCompleted)
        {
            return;
        }

        Task<SaveRecoveryReport> task = _recoveryTask;
        _recoveryTask = null;
        try
        {
            _recoveryReport = task.GetAwaiter().GetResult();
            _loadedSnapshot = _recoveryReport.Snapshot;
            if (_loadedSnapshot is not null)
            {
                _manualRevision = _loadedSnapshot.Revision;
                ApplySnapshotToVisualization(_loadedSnapshot);
            }

            _state = _recoveryReport.Recovered
                ? SavePrototypeState.Ready
                : SavePrototypeState.Failed;
            _statusMessage = _recoveryReport.Result;
            _recoveryOperationHud = _recoveryReport.Recovered
                ? $"PASS rev={_recoveryReport.Snapshot?.Revision ?? 0}, " +
                  $"atomic={(_recoveryReport.AtomicReplacementUsed ? 1 : 0)}, " +
                  $"quarantine={(!string.IsNullOrWhiteSpace(_recoveryReport.QuarantinePath) ? 1 : 0)}"
                : $"FAIL {_recoveryReport.Result}";
            string output = BuildRecoveryOutput(_recoveryReport);
            if (_recoveryReport.Recovered)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }

            BeginRefresh(
                completionMessage: _recoveryOperationHud,
                completionState: _recoveryReport.Recovered
                    ? SavePrototypeState.Ready
                    : SavePrototypeState.Failed);
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"recovery failed: {exception.Message}";
            _recoveryOperationHud = $"FAIL {exception.Message}";
            GD.PushError($"Prototype E recovery failed: {exception}");
        }
    }

    private void PollRecoveryAcceptanceTask()
    {
        if (_recoveryAcceptanceTask is null ||
            !_recoveryAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<SaveRecoveryAcceptanceReport> task = _recoveryAcceptanceTask;
        _recoveryAcceptanceTask = null;
        try
        {
            _recoveryAcceptanceReport = task.GetAwaiter().GetResult();
            _state = _recoveryAcceptanceReport.Passed
                ? SavePrototypeState.Passed
                : SavePrototypeState.Failed;
            _statusMessage = _recoveryAcceptanceReport.Result;
            string output = BuildRecoveryAcceptanceOutput(
                _recoveryAcceptanceReport);
            if (_recoveryAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"recovery test failed: {exception.Message}";
            GD.PushError($"TASK-056 SQLite recovery acceptance failed: {exception}");
        }
    }

    private void PollMigrationAcceptanceTask()
    {
        if (_migrationAcceptanceTask is null ||
            !_migrationAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<SaveMigrationAcceptanceReport> task = _migrationAcceptanceTask;
        _migrationAcceptanceTask = null;
        try
        {
            _migrationAcceptanceReport = task.GetAwaiter().GetResult();
            _state = _migrationAcceptanceReport.Passed
                ? SavePrototypeState.Passed
                : SavePrototypeState.Failed;
            _statusMessage = _migrationAcceptanceReport.Result;
            string output = BuildMigrationAcceptanceOutput(
                _migrationAcceptanceReport);
            if (_migrationAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"migration test failed: {exception.Message}";
            GD.PushError(
                $"TASK-058 SQLite migration/content acceptance failed: {exception}");
        }
    }

    private void PollAutosaveAcceptanceTask()
    {
        if (_autosaveAcceptanceTask is null ||
            !_autosaveAcceptanceTask.IsCompleted)
        {
            return;
        }

        Task<SaveAutosaveAcceptanceReport> task = _autosaveAcceptanceTask;
        _autosaveAcceptanceTask = null;
        try
        {
            _autosaveAcceptanceReport = task.GetAwaiter().GetResult();
            _state = _autosaveAcceptanceReport.Passed
                ? SavePrototypeState.Passed
                : SavePrototypeState.Failed;
            _statusMessage = _autosaveAcceptanceReport.Result;
            string output = BuildAutosaveAcceptanceOutput(
                _autosaveAcceptanceReport);
            if (_autosaveAcceptanceReport.Passed)
            {
                GD.Print(output);
            }
            else
            {
                GD.PushError(output);
            }
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"autosave test failed: {exception.Message}";
            GD.PushError(
                $"TASK-060 SQLite autosave/graceful-exit acceptance failed: {exception}");
        }
    }

    private void BeginRefresh(
        string completionMessage,
        SavePrototypeState completionState)
    {
        if (_database is null || _refreshTask is not null ||
            _loadTask is not null ||
            _writeTask is not null || _acceptanceTask is not null ||
            _backupTask is not null || _recoveryTask is not null ||
            _recoveryAcceptanceTask is not null ||
            _migrationAcceptanceTask is not null ||
            _autosaveAcceptanceTask is not null ||
            _gracefulExitTask is not null ||
            (_autosaveCoordinator?.IsBusy ?? false))
        {
            return;
        }

        _refreshCompletionMessage = completionMessage;
        _refreshCompletionState = completionState;
        _state = SavePrototypeState.Loading;
        _statusMessage = "обновление snapshot и диагностики";
        _refreshTask = LoadAndRefreshDiagnosticsAsync(_lifetimeCancellation.Token);
    }

    private async Task<SavePrototypeRefresh> LoadAndRefreshDiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        if (_database is null)
        {
            throw new InvalidOperationException("Save database is unavailable.");
        }

        SaveGameSnapshot? snapshot = await _database.LoadAsync(
            SlotId,
            cancellationToken).ConfigureAwait(false);
        SaveDatabaseDiagnostics diagnostics =
            await _database.ReadDiagnosticsAsync(
                SlotId,
                cancellationToken).ConfigureAwait(false);
        return new SavePrototypeRefresh(snapshot, diagnostics);
    }

    private void PollRefreshTask()
    {
        if (_refreshTask is null || !_refreshTask.IsCompleted)
        {
            return;
        }

        Task<SavePrototypeRefresh> task = _refreshTask;
        _refreshTask = null;
        try
        {
            SavePrototypeRefresh refresh = task.GetAwaiter().GetResult();
            _loadedSnapshot = refresh.Snapshot;
            _diagnostics = refresh.Diagnostics;
            if (_loadedSnapshot is null)
            {
                _manualRevision = 0;
                ResetVisualization();
            }
            else
            {
                _manualRevision = Math.Max(
                    _manualRevision,
                    _loadedSnapshot.Revision);
                ApplySnapshotToVisualization(_loadedSnapshot);
            }

            _state = _refreshCompletionState;
            _statusMessage = _refreshCompletionMessage;
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"refresh failed: {exception.Message}";
            GD.PushError($"Prototype E diagnostics refresh failed: {exception}");
        }
    }

    private void ApplySnapshotToVisualization(SaveGameSnapshot snapshot)
    {
        if (_playerMarker is null || _shipMarker is null ||
            _visitedPlanetMarker is null)
        {
            return;
        }

        _playerMarker.Position = new Vector3(
            (float)((snapshot.Player.PositionX - 120.0) * 0.08),
            0.0f,
            (float)(snapshot.Player.PositionZ * 0.04));
        _shipMarker.Position = new Vector3(
            (float)((snapshot.Ship.PositionX - 135.0) * 0.08),
            1.5f,
            (float)(snapshot.Ship.PositionZ * 0.04));
        _visitedPlanetMarker.Visible = snapshot.VisitedPlanet.VisitCount > 0;
    }

    private void ResetVisualization()
    {
        if (_playerMarker is not null)
        {
            _playerMarker.Position = Vector3.Zero;
        }

        if (_shipMarker is not null)
        {
            _shipMarker.Position = new Vector3(0.0f, 1.5f, -1.0f);
        }

        if (_visitedPlanetMarker is not null)
        {
            _visitedPlanetMarker.Visible = false;
        }
    }

    private void UpdateHud()
    {
        if (_compactLabel is null || _detailedLabel is null)
        {
            return;
        }

        SaveDatabaseDiagnostics diagnostics = _diagnostics ??
            new SaveDatabaseDiagnostics(
                0, "—", false, 0, 0, "—", 0, 0, 0, 0, 0, 0);
        string snapshotLine = _loadedSnapshot is null
            ? "Snapshot: slot пуст"
            : $"Snapshot: rev={_loadedSnapshot.Revision} • " +
              $"inventory={_loadedSnapshot.Inventory.Count} • " +
              $"ore={FindOreQuantity(_loadedSnapshot)} • " +
              $"planet visits={_loadedSnapshot.VisitedPlanet.VisitCount}";
        string acceptanceLine = BuildAcceptanceHudLine();
        string recoveryAcceptanceLine = BuildRecoveryAcceptanceHudLine();
        string migrationAcceptanceLine = BuildMigrationAcceptanceHudLine();
        string autosaveAcceptanceLine = BuildAutosaveAcceptanceHudLine();
        double autosaveRemaining = Math.Max(
            0.0,
            AutosaveIntervalSeconds - _autosaveElapsedSeconds);

        _compactLabel.Text =
            "SQLITE AUTOSAVE / MIGRATION / RECOVERY • H — HUD\n" +
            $"DB: {_state} • schema={diagnostics.SchemaVersion} • " +
            $"WAL={diagnostics.JournalMode} • FK={(diagnostics.ForeignKeysEnabled ? "ON" : "OFF")}\n" +
            $"Queue: pending={_database?.QueuedWrites ?? 0} • " +
            $"writes={_database?.CompletedWrites ?? 0} • " +
            $"maxConcurrent={_database?.MaximumConcurrentWriters ?? 0}\n" +
            snapshotLine + "\n" +
            acceptanceLine + "\n" +
            recoveryAcceptanceLine + "\n" +
            migrationAcceptanceLine + "\n" +
            autosaveAcceptanceLine + "\n" +
            $"Autosave: {_autosaveOperationHud} • next={autosaveRemaining:F1}s\n" +
            $"Slot S/L/R: {_slotOperationHud}\n" +
            $"Backup B: {_backupOperationHud}\n" +
            $"Restore Y: {_recoveryOperationHud}\n" +
            $"Backup: {(diagnostics.BackupExists ? "есть" : "нет")} • " +
            $"integrity={diagnostics.BackupIntegrityResult} • " +
            $"bytes={diagnostics.BackupBytes}\n" +
            "S/L/R — slot • B/Y — backup/restore • F6/C/X/Z — acceptance tests";

        _detailedLabel.Text =
            "SQLITE / AUTOSAVE / COPY MIGRATION / ATOMIC RECOVERY\n" +
            "HUD: подробный • H — compact/hidden • колесо — прокрутка\n\n" +
            $"State: {_state}\n" +
            $"Message: {_statusMessage}\n" +
            $"Database: {_databaseDisplayPath}\n" +
            $"Database bytes: {diagnostics.DatabaseBytes}\n" +
            $"Schema version: {diagnostics.SchemaVersion}\n" +
            $"PRAGMA journal_mode: {diagnostics.JournalMode}\n" +
            $"PRAGMA foreign_keys: {(diagnostics.ForeignKeysEnabled ? 1 : 0)}\n" +
            $"PRAGMA synchronous: {diagnostics.SynchronousMode}\n" +
            $"PRAGMA busy_timeout: {diagnostics.BusyTimeoutMilliseconds}\n" +
            $"PRAGMA integrity_check: {diagnostics.IntegrityResult}\n" +
            $"Backup path: {_database?.BackupPath ?? "—"}\n" +
            $"Backup exists: {(diagnostics.BackupExists ? 1 : 0)}\n" +
            $"Backup bytes: {diagnostics.BackupBytes}\n" +
            $"Backup integrity: {diagnostics.BackupIntegrityResult}\n" +
            $"Recovery log: {_database?.RecoveryLogPath ?? "—"}\n" +
            $"Migration log: {_database?.MigrationLogPath ?? "—"}\n" +
            $"Autosave log: {_autosaveCoordinator?.AutosaveLogPath ?? "—"}\n" +
            $"Autosave interval: {AutosaveIntervalSeconds:F1} s\n" +
            $"Autosave next: {autosaveRemaining:F1} s\n" +
            $"Autosave requests/batches/coalesced: " +
            $"{_autosaveCoordinator?.RequestedSaves ?? 0}/" +
            $"{_autosaveCoordinator?.CompletedBatches ?? 0}/" +
            $"{_autosaveCoordinator?.CoalescedRequests ?? 0}\n" +
            $"Inventory rows: {diagnostics.InventoryRows}\n" +
            $"Visited planet rows: {diagnostics.VisitedPlanetRows}\n" +
            $"Queued writes: {_database?.QueuedWrites ?? 0}\n" +
            $"Completed writes: {_database?.CompletedWrites ?? 0}\n" +
            $"Maximum concurrent writers: {_database?.MaximumConcurrentWriters ?? 0}\n\n" +
            snapshotLine + "\n" +
            BuildSnapshotDetails() + "\n\n" +
            acceptanceLine + "\n" +
            recoveryAcceptanceLine + "\n" +
            migrationAcceptanceLine + "\n" +
            autosaveAcceptanceLine + "\n" +
            $"Autosave: {_autosaveOperationHud}\n" +
            $"Slot S/L/R: {_slotOperationHud}\n" +
            $"Backup B: {_backupOperationHud}\n" +
            $"Restore Y: {_recoveryOperationHud}\n" +
            "Foundation acceptance: explicit migration, WAL/FK/NORMAL/busy_timeout, " +
            "transactional player/ship/inventory/planet save, exact load comparison, " +
            "8 concurrent submissions through a single writer gate, integrity_check.\n" +
            "Recovery acceptance uses an isolated database: protected revision 10, " +
            "newer primary revision 11, rejected invalid backup candidate, intentional " +
            "primary corruption, atomic replacement, quarantine and exact rollback.\n" +
            "Migration acceptance creates an isolated schema-1 save, migrates only a " +
            "validated copy to schema 2, preserves the byte-identical source, resolves a " +
            "legacy alias and substitutes placeholders for removed item/ship IDs while " +
            "retaining their original IDs and gameplay values through a second save/load.\n" +
            "Autosave acceptance covers the 60-second periodic trigger, six gameplay " +
            "event reasons, deterministic burst coalescing, one-writer serialization, " +
            "autosave logging and a graceful-exit flush of the latest immutable snapshot.\n\n" +
            "S — сохранить snapshot; предыдущая копия защищается автоматически\n" +
            "L — загрузить snapshot\n" +
            "R — очистить slot, сохранив предыдущую копию\n" +
            "B — создать/обновить валидированный backup\n" +
            "Y — восстановить предыдущую копию с quarantine текущей БД\n" +
            "Z — TASK-054 foundation acceptance\n" +
            "X — TASK-056 backup/recovery acceptance\n" +
            "C — TASK-058 migration/unknown-content acceptance\n" +
            "F6 — TASK-060 autosave/graceful-exit acceptance\n" +
            "H — compact / detailed / hidden";
    }

    private string BuildAcceptanceHudLine()
    {
        if (_acceptanceTask is not null)
        {
            return "TASK-054 save (Z): RUNNING migration/save/load/queue/integrity";
        }

        if (_acceptanceReport is null)
        {
            return "TASK-054 save (Z): READY";
        }

        SaveDatabaseDiagnostics diagnostics = _acceptanceReport.Diagnostics;
        return _acceptanceReport.Passed
            ? $"TASK-054 save (Z): PASS rev={_acceptanceReport.LoadedSnapshot?.Revision ?? 0}, " +
              $"items={diagnostics.InventoryRows}, writes={_acceptanceReport.ConcurrentWritesSubmitted}, " +
              $"maxWriters={diagnostics.MaximumConcurrentWriters}, integrity={diagnostics.IntegrityResult}"
            : $"TASK-054 save (Z): FAIL — {_acceptanceReport.Result}";
    }

    private string BuildRecoveryAcceptanceHudLine()
    {
        if (_recoveryAcceptanceTask is not null)
        {
            return "TASK-056 recovery (X): RUNNING isolated corruption/recovery";
        }

        if (_recoveryAcceptanceReport is null)
        {
            return "TASK-056 recovery (X): READY";
        }

        return _recoveryAcceptanceReport.Passed
            ? $"TASK-056 recovery (X): PASS rev={_recoveryAcceptanceReport.RecoveredSnapshot?.Revision ?? 0}, " +
              $"candidateRejected={(_recoveryAcceptanceReport.CandidateRejected ? 1 : 0)}, " +
              $"backupPreserved={(_recoveryAcceptanceReport.BackupPreserved ? 1 : 0)}, " +
              $"atomic={(_recoveryAcceptanceReport.AtomicReplacementUsed ? 1 : 0)}, " +
              $"quarantine={(_recoveryAcceptanceReport.QuarantinePreserved ? 1 : 0)}"
            : $"TASK-056 recovery (X): FAIL — {_recoveryAcceptanceReport.Result}";
    }

    private string BuildMigrationAcceptanceHudLine()
    {
        if (_migrationAcceptanceTask is not null)
        {
            return "TASK-058 migration (C): RUNNING schema-1 copy/content compatibility";
        }

        if (_migrationAcceptanceReport is null)
        {
            return "TASK-058 migration (C): READY";
        }

        SaveMigrationReport migration = _migrationAcceptanceReport.Migration;
        return _migrationAcceptanceReport.Passed
            ? $"TASK-058 migration (C): PASS {migration.FromSchemaVersion}→" +
              $"{migration.ToSchemaVersion}, source=1, aliases={migration.AliasedReferences}, " +
              $"unknown={migration.PlaceholderReferences}, roundTrip=1"
            : $"TASK-058 migration (C): FAIL — {_migrationAcceptanceReport.Result}";
    }

    private string BuildAutosaveAcceptanceHudLine()
    {
        if (_autosaveAcceptanceTask is not null)
        {
            return "TASK-060 autosave (F6): RUNNING periodic/events/graceful-exit";
        }

        if (_autosaveAcceptanceReport is null)
        {
            return "TASK-060 autosave (F6): READY";
        }

        return _autosaveAcceptanceReport.Passed
            ? $"TASK-060 autosave (F6): PASS triggers=" +
              $"{_autosaveAcceptanceReport.TriggerTypesCovered}, " +
              $"requests={_autosaveAcceptanceReport.RequestedSaves}, " +
              $"batches={_autosaveAcceptanceReport.CompletedBatches}, " +
              $"coalesced={_autosaveAcceptanceReport.CoalescedRequests}, exit=1"
            : $"TASK-060 autosave (F6): FAIL — {_autosaveAcceptanceReport.Result}";
    }

    private static string BuildAutosaveAcceptanceOutput(
        SaveAutosaveAcceptanceReport report)
    {
        string prefix = report.Passed
            ? "TASK-060 SQLite autosave/graceful-exit acceptance PASS"
            : "TASK-060 SQLite autosave/graceful-exit acceptance FAIL";
        return prefix +
            $": triggerTypes={report.TriggerTypesCovered}; " +
            $"requested={report.RequestedSaves}; " +
            $"batches={report.CompletedBatches}; " +
            $"coalesced={report.CoalescedRequests}; " +
            $"periodic={(report.PeriodicTriggered ? 1 : 0)}; " +
            $"gracefulExit={(report.GracefulExitFlushed ? 1 : 0)}; " +
            $"roundTrip={(report.ExactRoundTrip ? 1 : 0)}; " +
            $"logWritten={(report.LogWritten ? 1 : 0)}; " +
            $"revision={report.LoadedSnapshot?.Revision ?? 0}; " +
            $"maxWriters={report.Diagnostics.MaximumConcurrentWriters}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; elapsedMs=" +
            report.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
            $"; result={report.Result}";
    }

    private static string BuildMigrationAcceptanceOutput(
        SaveMigrationAcceptanceReport report)
    {
        SaveMigrationReport migration = report.Migration;
        string prefix = report.Passed
            ? "TASK-058 SQLite migration/content acceptance PASS"
            : "TASK-058 SQLite migration/content acceptance FAIL";
        return prefix +
            $": fromSchema={migration.FromSchemaVersion}; " +
            $"toSchema={migration.ToSchemaVersion}; " +
            $"fromContent={migration.FromContentVersion}; " +
            $"toContent={migration.ToContentVersion}; " +
            $"sourcePreserved={(migration.SourcePreserved ? 1 : 0)}; " +
            $"sourceHashUnchanged={(report.LegacySourceUnchanged ? 1 : 0)}; " +
            $"atomicReplace={(migration.AtomicReplacementUsed ? 1 : 0)}; " +
            $"aliases={migration.AliasedReferences}; " +
            $"placeholders={migration.PlaceholderReferences}; " +
            $"aliasResolved={(report.AliasResolved ? 1 : 0)}; " +
            $"unknownItemPreserved={(report.UnknownItemPreserved ? 1 : 0)}; " +
            $"unknownShipPreserved={(report.UnknownShipPreserved ? 1 : 0)}; " +
            $"roundTripPreserved={(report.RoundTripPreserved ? 1 : 0)}; " +
            $"exactContentChecks={report.ExactContentChecks}; " +
            $"integrity={report.Diagnostics.IntegrityResult}; elapsedMs=" +
            report.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
            $"; result={report.Result}";
    }

    private static string BuildBackupOutput(SaveBackupReport report)
    {
        return "Prototype E validated backup " +
            (report.Succeeded ? "PASS" : "FAIL") +
            $": revision={report.Snapshot?.Revision ?? 0}; " +
            $"integrity={report.IntegrityResult}; bytes={report.BackupBytes}; " +
            $"atomicReplace={(report.AtomicReplacementUsed ? 1 : 0)}; " +
            $"sha256={report.Sha256}; elapsedMs=" +
            report.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
            $"; result={report.Result}";
    }

    private static string BuildRecoveryOutput(SaveRecoveryReport report)
    {
        return "Prototype E previous-copy recovery " +
            (report.Recovered ? "PASS" : "FAIL") +
            $": revision={report.Snapshot?.Revision ?? 0}; " +
            $"primaryIntegrity={report.PrimaryIntegrityResult}; " +
            $"backupIntegrity={report.BackupIntegrityResult}; " +
            $"atomicReplace={(report.AtomicReplacementUsed ? 1 : 0)}; " +
            $"quarantine={report.QuarantinePath}; elapsedMs=" +
            report.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
            $"; result={report.Result}";
    }

    private static string BuildRecoveryAcceptanceOutput(
        SaveRecoveryAcceptanceReport report)
    {
        string prefix = report.Passed
            ? "TASK-056 SQLite backup/recovery acceptance PASS"
            : "TASK-056 SQLite backup/recovery acceptance FAIL";
        return prefix +
            $": protectedRevision={report.ProtectedRevision}; " +
            $"newerRevision={report.NewerRevision}; " +
            $"recoveredRevision={report.RecoveredSnapshot?.Revision ?? 0}; " +
            $"primaryIntegrity={report.Diagnostics.IntegrityResult}; " +
            $"backupIntegrity={report.Diagnostics.BackupIntegrityResult}; " +
            $"candidateRejected={(report.CandidateRejected ? 1 : 0)}; " +
            $"backupPreserved={(report.BackupPreserved ? 1 : 0)}; " +
            $"corruptionDetected={(report.CorruptionDetected ? 1 : 0)}; " +
            $"atomicReplace={(report.AtomicReplacementUsed ? 1 : 0)}; " +
            $"quarantinePreserved={(report.QuarantinePreserved ? 1 : 0)}; " +
            $"logWritten={(report.RecoveryLogWritten ? 1 : 0)}; " +
            $"exactComparisons={report.ExactComparisons}; elapsedMs=" +
            report.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
            $"; result={report.Result}";
    }

    private string BuildSnapshotDetails()
    {
        if (_loadedSnapshot is null)
        {
            return "Loaded snapshot: none";
        }

        SaveGameSnapshot snapshot = _loadedSnapshot;
        return
            $"Player: id={snapshot.Player.PlayerId}, " +
            $"pos=({snapshot.Player.PositionX:F2}, {snapshot.Player.PositionY:F2}, " +
            $"{snapshot.Player.PositionZ:F2}), planet={snapshot.Player.CurrentPlanetId}\n" +
            $"Ship: id={snapshot.Ship.ShipId}, template={snapshot.Ship.TemplateId}, " +
            $"health={snapshot.Ship.Health:F2}, fuel={snapshot.Ship.Fuel:F2}\n" +
            $"Inventory: {string.Join(", ", snapshot.Inventory)}\n" +
            $"Visited: {snapshot.VisitedPlanet.SystemId}/" +
            $"{snapshot.VisitedPlanet.PlanetId}, visits={snapshot.VisitedPlanet.VisitCount}";
    }

    private static string BuildAcceptanceOutput(
        SavePrototypeAcceptanceReport report)
    {
        SaveDatabaseDiagnostics diagnostics = report.Diagnostics;
        string prefix = report.Passed
            ? "TASK-054 SQLite save foundation acceptance PASS"
            : "TASK-054 SQLite save foundation acceptance FAIL";
        return prefix +
            $": schema={diagnostics.SchemaVersion}; " +
            $"journal={diagnostics.JournalMode}; " +
            $"foreignKeys={(diagnostics.ForeignKeysEnabled ? 1 : 0)}; " +
            $"synchronous={diagnostics.SynchronousMode}; " +
            $"busyTimeout={diagnostics.BusyTimeoutMilliseconds}; " +
            $"integrity={diagnostics.IntegrityResult}; " +
            $"revision={report.LoadedSnapshot?.Revision ?? 0}; " +
            $"inventoryRows={diagnostics.InventoryRows}; " +
            $"visitedRows={diagnostics.VisitedPlanetRows}; " +
            $"queuedWrites={report.ConcurrentWritesSubmitted}; " +
            $"completedWrites={diagnostics.CompletedWrites}; " +
            $"maxConcurrentWriters={diagnostics.MaximumConcurrentWriters}; " +
            $"exactComparisons={report.ExactComparisons}; " +
            $"elapsedMs={report.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}; " +
            $"result={report.Result}";
    }

    private static int FindOreQuantity(SaveGameSnapshot snapshot)
    {
        foreach (InventoryItemSaveData item in snapshot.Inventory)
        {
            if (item.DefinitionId == "resource.iron_ore")
            {
                return item.Quantity;
            }
        }

        return 0;
    }

    private void ApplyHudMode()
    {
        if (_compactMargin is null || _detailedMargin is null ||
            _hiddenHint is null)
        {
            return;
        }

        _compactMargin.Visible = _hudMode == SavePrototypeHudMode.Compact;
        _detailedMargin.Visible = _hudMode == SavePrototypeHudMode.Detailed;
        _hiddenHint.Visible = _hudMode == SavePrototypeHudMode.Hidden;
        UpdateHudLayout();
    }

    private void UpdateHudLayout()
    {
        if (_compactMargin is null || _detailedMargin is null)
        {
            return;
        }

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float maximumWidth = Math.Max(320.0f, viewportSize.X - 32.0f);
        float maximumHeight = Math.Max(160.0f, viewportSize.Y - 32.0f);
        _compactMargin.CustomMinimumSize = new Vector2(
            Math.Min(HudCompactWidth, maximumWidth),
            Math.Min(HudCompactHeight, maximumHeight));
        _detailedMargin.CustomMinimumSize = new Vector2(
            Math.Min(HudDetailedWidth, maximumWidth),
            Math.Min(HudDetailedHeight, maximumHeight));
    }

    private static bool Matches(Key physical, Key logical, Key expected)
    {
        return physical == expected || logical == expected;
    }

    private sealed record SavePrototypeRefresh(
        SaveGameSnapshot? Snapshot,
        SaveDatabaseDiagnostics Diagnostics);
}
