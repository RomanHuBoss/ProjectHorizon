using System;
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
    Testing = 5,
    Passed = 6,
    Failed = 7
}

public enum SavePrototypeHudMode
{
    Compact = 0,
    Detailed = 1,
    Hidden = 2
}

public partial class SavePrototype : Node3D
{
    private const string SlotId = "save_1";

    [Export(PropertyHint.Range, "420.0,1000.0,10.0")]
    public float HudCompactWidth { get; set; } = 720.0f;

    [Export(PropertyHint.Range, "180.0,500.0,10.0")]
    public float HudCompactHeight { get; set; } = 280.0f;

    [Export(PropertyHint.Range, "520.0,1200.0,10.0")]
    public float HudDetailedWidth { get; set; } = 820.0f;

    [Export(PropertyHint.Range, "320.0,900.0,10.0")]
    public float HudDetailedHeight { get; set; } = 560.0f;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private SaveDatabase? _database;
    private Task<SaveDatabaseDiagnostics>? _initializeTask;
    private Task<SaveGameSnapshot?>? _loadTask;
    private Task? _writeTask;
    private Task<SavePrototypeAcceptanceReport>? _acceptanceTask;
    private SavePrototypeState _state = SavePrototypeState.Initializing;
    private SavePrototypeHudMode _hudMode = SavePrototypeHudMode.Compact;
    private SaveGameSnapshot? _loadedSnapshot;
    private SaveDatabaseDiagnostics? _diagnostics;
    private SavePrototypeAcceptanceReport? _acceptanceReport;
    private int _manualRevision;
    private string _statusMessage = "инициализация SQLite";
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
        _database = new SaveDatabase(databasePath);
        _initializeTask = _database.InitializeAsync(_lifetimeCancellation.Token);

        GetViewport().SizeChanged += UpdateHudLayout;
        ApplyHudMode();
        UpdateHud();
        GD.Print(
            "Prototype E SQLite foundation initializing. " +
            "Press Z for the acceptance test after READY.");
    }

    public override void _ExitTree()
    {
        if (GetViewport() is Viewport viewport)
        {
            viewport.SizeChanged -= UpdateHudLayout;
        }

        _lifetimeCancellation.Cancel();
        _database?.Dispose();
        _lifetimeCancellation.Dispose();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        PollInitializeTask();
        PollWriteTask();
        PollLoadTask();
        PollAcceptanceTask();
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
            _acceptanceTask is null;
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
            _state = SavePrototypeState.Ready;
            _statusMessage = "SQLite READY";
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
            _state = SavePrototypeState.Ready;
            _statusMessage = "операция записи завершена";
            RefreshDiagnostics();
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"write failed: {exception.Message}";
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
                _state = SavePrototypeState.Ready;
                _statusMessage = "slot save_1 пуст";
            }
            else
            {
                _manualRevision = Math.Max(
                    _manualRevision,
                    _loadedSnapshot.Revision);
                ApplySnapshotToVisualization(_loadedSnapshot);
                _state = SavePrototypeState.Ready;
                _statusMessage =
                    $"loaded revision={_loadedSnapshot.Revision}";
            }

            RefreshDiagnostics();
        }
        catch (Exception exception)
        {
            _state = SavePrototypeState.Failed;
            _statusMessage = $"load failed: {exception.Message}";
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

    private void RefreshDiagnostics()
    {
        if (_database is null || _loadTask is not null ||
            _writeTask is not null || _acceptanceTask is not null)
        {
            return;
        }

        _loadTask = LoadAndRefreshDiagnosticsAsync();
    }

    private async Task<SaveGameSnapshot?> LoadAndRefreshDiagnosticsAsync()
    {
        if (_database is null)
        {
            return null;
        }

        SaveGameSnapshot? snapshot = await _database.LoadAsync(
            SlotId,
            _lifetimeCancellation.Token).ConfigureAwait(false);
        _diagnostics = await _database.ReadDiagnosticsAsync(
            SlotId,
            _lifetimeCancellation.Token).ConfigureAwait(false);
        return snapshot;
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

        _compactLabel.Text =
            "ПРОТОТИП E — SQLITE SAVE FOUNDATION • H — HUD\n" +
            $"DB: {_state} • schema={diagnostics.SchemaVersion} • " +
            $"WAL={diagnostics.JournalMode} • FK={(diagnostics.ForeignKeysEnabled ? "ON" : "OFF")}\n" +
            $"Queue: pending={_database?.QueuedWrites ?? 0} • " +
            $"writes={_database?.CompletedWrites ?? 0} • " +
            $"maxConcurrent={_database?.MaximumConcurrentWriters ?? 0}\n" +
            snapshotLine + "\n" +
            acceptanceLine + "\n" +
            "S — сохранить • L — загрузить • R — очистить slot • Z — тест";

        _detailedLabel.Text =
            "ПРОТОТИП E — SQLITE / МИГРАЦИЯ / TRANSACTION ROUND-TRIP\n" +
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
            $"Inventory rows: {diagnostics.InventoryRows}\n" +
            $"Visited planet rows: {diagnostics.VisitedPlanetRows}\n" +
            $"Queued writes: {_database?.QueuedWrites ?? 0}\n" +
            $"Completed writes: {_database?.CompletedWrites ?? 0}\n" +
            $"Maximum concurrent writers: {_database?.MaximumConcurrentWriters ?? 0}\n\n" +
            snapshotLine + "\n" +
            BuildSnapshotDetails() + "\n\n" +
            acceptanceLine + "\n" +
            "Acceptance: explicit migration, WAL/FK/NORMAL/busy_timeout, " +
            "transactional player/ship/inventory/planet save, exact load comparison, " +
            "8 concurrent submissions through a single writer gate, integrity_check.\n\n" +
            "S — сохранить изменённый snapshot\n" +
            "L — загрузить snapshot\n" +
            "R — очистить игровой slot транзакцией\n" +
            "Z — TASK-054 automatic acceptance\n" +
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

        Vector2 viewportSize = GetViewportRect().Size;
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
}
