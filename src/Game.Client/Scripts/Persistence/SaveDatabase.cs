using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public sealed partial class SaveDatabase : IDisposable
{
    public const int CurrentSchemaVersion = 2;
    public const int CurrentContentVersion = 2;
    public const int BusyTimeoutMilliseconds = 5000;

    private readonly string _databasePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _queuedWrites;
    private int _completedWrites;
    private int _activeWriters;
    private int _maximumConcurrentWriters;
    private bool _disposed;

    public SaveDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Database path must not be empty.",
                nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public int QueuedWrites => Volatile.Read(ref _queuedWrites);

    public int CompletedWrites => Volatile.Read(ref _completedWrites);

    public int MaximumConcurrentWriters =>
        Volatile.Read(ref _maximumConcurrentWriters);

    public Task<SaveDatabaseDiagnostics> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(slotId: null);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                return ReadDiagnosticsCore(connection, string.Empty);
            },
            cancellationToken);
    }

    public Task SaveAsync(
        SaveGameSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(snapshot.SlotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);

                bool hadPreviousSnapshot = TryLoadSnapshotCore(
                    connection,
                    snapshot.SlotId,
                    out _);
                if (hadPreviousSnapshot)
                {
                    CreateValidatedBackupCore(connection, snapshot.SlotId);
                }

                SaveSnapshotCore(connection, snapshot);
                ValidateExpectedSnapshotCore(connection, snapshot);

                if (!hadPreviousSnapshot)
                {
                    CreateValidatedBackupCore(connection, snapshot.SlotId);
                }

                return true;
            },
            cancellationToken);
    }

    public Task<SaveGameSnapshot?> LoadAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                return LoadSnapshotCore(connection, slotId);
            },
            cancellationToken);
    }

    public Task ResetSlotAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);

                if (TryLoadSnapshotCore(connection, slotId, out _))
                {
                    CreateValidatedBackupCore(connection, slotId);
                }

                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM save_meta WHERE slot_id = $slot_id;";
                command.Parameters.AddWithValue("$slot_id", slotId);
                command.ExecuteNonQuery();
                transaction.Commit();
                return true;
            },
            cancellationToken);
    }

    public async Task<SavePrototypeAcceptanceReport> RunAcceptanceAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        const int concurrentWriteCount = 8;
        int exactComparisons = 0;

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await ResetSlotAsync(slotId, cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot baseline = CreateAcceptanceSnapshot(
                slotId,
                revision: 1,
                playerOffset: 0.0,
                oreQuantity: 12,
                visitCount: 1);
            await SaveAsync(baseline, cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot? firstLoad = await LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            if (!SnapshotsEqual(baseline, firstLoad, out string firstMismatch))
            {
                return BuildFailure(
                    $"baseline mismatch: {firstMismatch}",
                    firstLoad,
                    slotId,
                    concurrentWriteCount,
                    exactComparisons,
                    stopwatch);
            }

            exactComparisons++;

            SaveGameSnapshot finalSnapshot = CreateAcceptanceSnapshot(
                slotId,
                revision: 2,
                playerOffset: 17.5,
                oreQuantity: 57,
                visitCount: 3);

            int completedBefore = CompletedWrites;
            Task[] writes = Enumerable.Range(0, concurrentWriteCount)
                .Select(_ => SaveAsync(finalSnapshot, cancellationToken))
                .ToArray();
            await Task.WhenAll(writes).ConfigureAwait(false);

            int completedDelta = CompletedWrites - completedBefore;
            if (completedDelta != concurrentWriteCount)
            {
                return BuildFailure(
                    $"write queue completed {completedDelta}/" +
                    concurrentWriteCount,
                    null,
                    slotId,
                    concurrentWriteCount,
                    exactComparisons,
                    stopwatch);
            }

            SaveGameSnapshot? finalLoad = await LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            if (!SnapshotsEqual(finalSnapshot, finalLoad, out string finalMismatch))
            {
                return BuildFailure(
                    $"final mismatch: {finalMismatch}",
                    finalLoad,
                    slotId,
                    concurrentWriteCount,
                    exactComparisons,
                    stopwatch);
            }

            exactComparisons++;
            SaveDatabaseDiagnostics diagnostics = await ReadDiagnosticsAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);

            bool passed =
                diagnostics.SchemaVersion == CurrentSchemaVersion &&
                string.Equals(
                    diagnostics.JournalMode,
                    "wal",
                    StringComparison.OrdinalIgnoreCase) &&
                diagnostics.ForeignKeysEnabled &&
                diagnostics.SynchronousMode == 1 &&
                diagnostics.BusyTimeoutMilliseconds == BusyTimeoutMilliseconds &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase) &&
                diagnostics.InventoryRows == finalSnapshot.Inventory.Count &&
                diagnostics.VisitedPlanetRows == 1 &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                exactComparisons == 2;

            stopwatch.Stop();
            string result = passed
                ? "migration, WAL, serialized writes and exact round-trip confirmed"
                : "diagnostic criteria not satisfied";

            return new SavePrototypeAcceptanceReport(
                passed,
                result,
                finalLoad,
                diagnostics,
                concurrentWriteCount,
                exactComparisons,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new SavePrototypeAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                null,
                EmptyDiagnostics(),
                concurrentWriteCount,
                exactComparisons,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public Task<SaveDatabaseDiagnostics> ReadDiagnosticsAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                return ReadDiagnosticsCore(connection, slotId);
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    public static SaveGameSnapshot CreateAcceptanceSnapshot(
        string slotId,
        int revision,
        double playerOffset,
        int oreQuantity,
        int visitCount)
    {
        string updatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return new SaveGameSnapshot(
            slotId,
            revision,
            GeneratorVersion: 1,
            ContentVersion: CurrentContentVersion,
            updatedUtc,
            new PlayerSaveData(
                "player.prototype",
                120.25 + playerOffset,
                42.5,
                -18.75,
                "planet.prototype"),
            new ShipSaveData(
                "ship.prototype",
                "ship.arcade.prototype",
                "Horizon Test Ship",
                87.5,
                63.25,
                135.0 + playerOffset,
                55.0,
                -26.0),
            new List<InventoryItemSaveData>
            {
                new("item.ore", "resource.iron_ore", oreQuantity, 1.0),
                new("item.cell", "component.energy_cell", 4, 0.92),
                new("item.kit", "consumable.repair_kit", 2, 0.78)
            },
            new VisitedPlanetSaveData(
                "planet.prototype",
                "system.prototype",
                updatedUtc,
                visitCount));
    }

    public static bool SnapshotsEqual(
        SaveGameSnapshot expected,
        SaveGameSnapshot? actual,
        out string mismatch)
    {
        if (actual is null)
        {
            mismatch = "loaded snapshot is null";
            return false;
        }

        if (expected.SlotId != actual.SlotId ||
            expected.Revision != actual.Revision ||
            expected.GeneratorVersion != actual.GeneratorVersion ||
            expected.ContentVersion != actual.ContentVersion ||
            expected.UpdatedUtc != actual.UpdatedUtc)
        {
            mismatch = "save_meta differs";
            return false;
        }

        if (expected.Player.PlayerId != actual.Player.PlayerId ||
            expected.Player.CurrentPlanetId != actual.Player.CurrentPlanetId ||
            !NearlyEqual(expected.Player.PositionX, actual.Player.PositionX) ||
            !NearlyEqual(expected.Player.PositionY, actual.Player.PositionY) ||
            !NearlyEqual(expected.Player.PositionZ, actual.Player.PositionZ))
        {
            mismatch = "player_state differs";
            return false;
        }

        if (expected.Ship.ShipId != actual.Ship.ShipId ||
            expected.Ship.TemplateId != actual.Ship.TemplateId ||
            expected.Ship.DisplayName != actual.Ship.DisplayName ||
            expected.Ship.OriginalTemplateId != actual.Ship.OriginalTemplateId ||
            expected.Ship.TemplateResolution != actual.Ship.TemplateResolution ||
            !NearlyEqual(expected.Ship.Health, actual.Ship.Health) ||
            !NearlyEqual(expected.Ship.Fuel, actual.Ship.Fuel) ||
            !NearlyEqual(expected.Ship.PositionX, actual.Ship.PositionX) ||
            !NearlyEqual(expected.Ship.PositionY, actual.Ship.PositionY) ||
            !NearlyEqual(expected.Ship.PositionZ, actual.Ship.PositionZ))
        {
            mismatch = "ships differs";
            return false;
        }

        if (expected.Inventory.Count != actual.Inventory.Count)
        {
            mismatch = "inventory count differs";
            return false;
        }

        Dictionary<string, InventoryItemSaveData> actualItems =
            actual.Inventory.ToDictionary(item => item.ItemId);
        foreach (InventoryItemSaveData expectedItem in expected.Inventory)
        {
            if (!actualItems.TryGetValue(expectedItem.ItemId, out InventoryItemSaveData? actualItem) ||
                expectedItem.DefinitionId != actualItem.DefinitionId ||
                expectedItem.OriginalDefinitionId != actualItem.OriginalDefinitionId ||
                expectedItem.Resolution != actualItem.Resolution ||
                expectedItem.Quantity != actualItem.Quantity ||
                !NearlyEqual(expectedItem.Durability, actualItem.Durability))
            {
                mismatch = $"inventory item {expectedItem.ItemId} differs";
                return false;
            }
        }

        if (expected.VisitedPlanet.PlanetId != actual.VisitedPlanet.PlanetId ||
            expected.VisitedPlanet.SystemId != actual.VisitedPlanet.SystemId ||
            expected.VisitedPlanet.FirstVisitedUtc != actual.VisitedPlanet.FirstVisitedUtc ||
            expected.VisitedPlanet.VisitCount != actual.VisitedPlanet.VisitCount)
        {
            mismatch = "visited_planets differs";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private async Task<T> EnqueueWriteAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _queuedWrites);
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _queuedWrites);
            throw;
        }

        Interlocked.Decrement(ref _queuedWrites);
        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int active = Interlocked.Increment(ref _activeWriters);
                    UpdateMaximumConcurrentWriters(active);
                    try
                    {
                        return operation();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeWriters);
                        Interlocked.Increment(ref _completedWrites);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void EnsureParentDirectory()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Database parent directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
    }

    private SqliteConnection OpenConnection()
    {
        ThrowIfDisposed();
        SqliteConnection connection = new(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;" +
            "Cache=Shared;Pooling=False");
        connection.Open();

        ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
        ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
        ExecutePragma(connection, "PRAGMA synchronous = NORMAL;");
        ExecutePragma(
            connection,
            $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");
        return connection;
    }

    private static MigrationTransformSummary ApplyMigrations(
        SqliteConnection connection)
    {
        using SqliteCommand bootstrap = connection.CreateCommand();
        bootstrap.CommandText =
            "CREATE TABLE IF NOT EXISTS schema_migrations (" +
            "version INTEGER PRIMARY KEY, " +
            "applied_utc TEXT NOT NULL);";
        bootstrap.ExecuteNonQuery();

        int currentVersion;
        using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText =
                "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            currentVersion = Convert.ToInt32(
                versionCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);
        }

        if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Save schema {currentVersion} is newer than supported " +
                $"schema {CurrentSchemaVersion}.");
        }

        if (currentVersion < 1)
        {
            ApplyMigration1(connection);
            currentVersion = 1;
        }

        MigrationTransformSummary summary = MigrationTransformSummary.Empty;
        if (currentVersion < 2)
        {
            summary = ApplyMigration2(connection);
            currentVersion = 2;
        }

        if (currentVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Migration chain stopped at schema {currentVersion}; " +
                $"expected {CurrentSchemaVersion}.");
        }

        return summary;
    }

    private static void ApplyMigration1(SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "CREATE TABLE save_meta (" +
            "slot_id TEXT PRIMARY KEY, " +
            "schema_version INTEGER NOT NULL, " +
            "save_revision INTEGER NOT NULL, " +
            "generator_version INTEGER NOT NULL, " +
            "content_version INTEGER NOT NULL, " +
            "created_utc TEXT NOT NULL, " +
            "updated_utc TEXT NOT NULL);" +
            "CREATE TABLE save_settings (" +
            "slot_id TEXT NOT NULL REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "setting_key TEXT NOT NULL, setting_value TEXT NOT NULL, " +
            "PRIMARY KEY(slot_id, setting_key));" +
            "CREATE TABLE player_state (" +
            "slot_id TEXT PRIMARY KEY REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "player_id TEXT NOT NULL, " +
            "pos_x REAL NOT NULL, pos_y REAL NOT NULL, pos_z REAL NOT NULL, " +
            "current_planet_id TEXT NOT NULL);" +
            "CREATE TABLE ships (" +
            "ship_id TEXT PRIMARY KEY, " +
            "slot_id TEXT NOT NULL UNIQUE REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "template_id TEXT NOT NULL, display_name TEXT NOT NULL, " +
            "health REAL NOT NULL, fuel REAL NOT NULL, " +
            "pos_x REAL NOT NULL, pos_y REAL NOT NULL, pos_z REAL NOT NULL);" +
            "CREATE TABLE containers (" +
            "container_id TEXT PRIMARY KEY, " +
            "slot_id TEXT NOT NULL REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "owner_type TEXT NOT NULL, owner_id TEXT NOT NULL, capacity INTEGER NOT NULL);" +
            "CREATE TABLE inventory_items (" +
            "container_id TEXT NOT NULL REFERENCES containers(container_id) ON DELETE CASCADE, " +
            "item_id TEXT NOT NULL, definition_id TEXT NOT NULL, " +
            "quantity INTEGER NOT NULL CHECK(quantity >= 0), " +
            "durability REAL NOT NULL CHECK(durability >= 0 AND durability <= 1), " +
            "PRIMARY KEY(container_id, item_id));" +
            "CREATE TABLE visited_planets (" +
            "slot_id TEXT NOT NULL REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "planet_id TEXT NOT NULL, system_id TEXT NOT NULL, " +
            "first_visited_utc TEXT NOT NULL, visit_count INTEGER NOT NULL, " +
            "PRIMARY KEY(slot_id, planet_id));" +
            "INSERT INTO schema_migrations(version, applied_utc) " +
            "VALUES (1, $applied_utc);";
        command.Parameters.AddWithValue(
            "$applied_utc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static MigrationTransformSummary ApplyMigration2(
        SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE inventory_items ADD COLUMN " +
            "original_definition_id TEXT NULL;");
        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE ships ADD COLUMN original_template_id TEXT NULL;");

        List<(long RowId, string DefinitionId)> inventoryRows = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "SELECT rowid, definition_id FROM inventory_items ORDER BY rowid;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                inventoryRows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        int aliases = 0;
        int placeholders = 0;
        foreach ((long rowId, string definitionId) in inventoryRows)
        {
            ContentResolution resolution = ResolveInventoryDefinitionCore(
                definitionId,
                originalDefinitionId: null);
            if (resolution.State == ContentResolutionState.Known)
            {
                continue;
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE inventory_items SET definition_id = $definition_id, " +
                "original_definition_id = $original_definition_id " +
                "WHERE rowid = $row_id;",
                ("$definition_id", resolution.EffectiveId),
                ("$original_definition_id", resolution.OriginalId),
                ("$row_id", rowId));

            if (resolution.State == ContentResolutionState.Aliased)
            {
                aliases++;
            }
            else
            {
                placeholders++;
            }
        }

        List<(long RowId, string TemplateId)> shipRows = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "SELECT rowid, template_id FROM ships ORDER BY rowid;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                shipRows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        foreach ((long rowId, string templateId) in shipRows)
        {
            ContentResolution resolution = ResolveShipTemplateCore(
                templateId,
                originalTemplateId: null);
            if (resolution.State == ContentResolutionState.Known)
            {
                continue;
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE ships SET template_id = $template_id, " +
                "original_template_id = $original_template_id " +
                "WHERE rowid = $row_id;",
                ("$template_id", resolution.EffectiveId),
                ("$original_template_id", resolution.OriginalId),
                ("$row_id", rowId));
            placeholders++;
        }

        string appliedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE save_meta SET schema_version = $schema_version, " +
            "content_version = CASE " +
            "WHEN content_version < $content_version THEN $content_version " +
            "ELSE content_version END;",
            ("$schema_version", CurrentSchemaVersion),
            ("$content_version", CurrentContentVersion));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO schema_migrations(version, applied_utc) " +
            "VALUES(2, $applied_utc);",
            ("$applied_utc", appliedUtc));
        transaction.Commit();
        return new MigrationTransformSummary(aliases, placeholders);
    }

    private static void SaveSnapshotCore(
        SqliteConnection connection,
        SaveGameSnapshot snapshot)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        string createdUtc = snapshot.UpdatedUtc;

        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO save_meta(" +
            "slot_id, schema_version, save_revision, generator_version, " +
            "content_version, created_utc, updated_utc) " +
            "VALUES($slot_id, $schema_version, $save_revision, " +
            "$generator_version, $content_version, $created_utc, $updated_utc) " +
            "ON CONFLICT(slot_id) DO UPDATE SET " +
            "schema_version=excluded.schema_version, " +
            "save_revision=excluded.save_revision, " +
            "generator_version=excluded.generator_version, " +
            "content_version=excluded.content_version, " +
            "updated_utc=excluded.updated_utc;",
            ("$slot_id", snapshot.SlotId),
            ("$schema_version", CurrentSchemaVersion),
            ("$save_revision", snapshot.Revision),
            ("$generator_version", snapshot.GeneratorVersion),
            ("$content_version", Math.Max(snapshot.ContentVersion, CurrentContentVersion)),
            ("$created_utc", createdUtc),
            ("$updated_utc", snapshot.UpdatedUtc));

        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO player_state(" +
            "slot_id, player_id, pos_x, pos_y, pos_z, current_planet_id) " +
            "VALUES($slot_id, $player_id, $pos_x, $pos_y, $pos_z, $planet_id) " +
            "ON CONFLICT(slot_id) DO UPDATE SET " +
            "player_id=excluded.player_id, pos_x=excluded.pos_x, " +
            "pos_y=excluded.pos_y, pos_z=excluded.pos_z, " +
            "current_planet_id=excluded.current_planet_id;",
            ("$slot_id", snapshot.SlotId),
            ("$player_id", snapshot.Player.PlayerId),
            ("$pos_x", snapshot.Player.PositionX),
            ("$pos_y", snapshot.Player.PositionY),
            ("$pos_z", snapshot.Player.PositionZ),
            ("$planet_id", snapshot.Player.CurrentPlanetId));

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM ships WHERE slot_id = $slot_id;",
            ("$slot_id", snapshot.SlotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO ships(" +
            "ship_id, slot_id, template_id, original_template_id, " +
            "display_name, health, fuel, pos_x, pos_y, pos_z) VALUES(" +
            "$ship_id, $slot_id, $template_id, $original_template_id, " +
            "$display_name, $health, $fuel, $pos_x, $pos_y, $pos_z);",
            ("$ship_id", snapshot.Ship.ShipId),
            ("$slot_id", snapshot.SlotId),
            ("$template_id", PersistedShipTemplateCore(snapshot.Ship)),
            ("$original_template_id", PersistedShipOriginalCore(snapshot.Ship)),
            ("$display_name", snapshot.Ship.DisplayName),
            ("$health", snapshot.Ship.Health),
            ("$fuel", snapshot.Ship.Fuel),
            ("$pos_x", snapshot.Ship.PositionX),
            ("$pos_y", snapshot.Ship.PositionY),
            ("$pos_z", snapshot.Ship.PositionZ));

        string containerId = $"{snapshot.SlotId}.player_inventory";
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM containers WHERE slot_id = $slot_id;",
            ("$slot_id", snapshot.SlotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO containers(container_id, slot_id, owner_type, owner_id, capacity) " +
            "VALUES($container_id, $slot_id, 'player', $owner_id, 64);",
            ("$container_id", containerId),
            ("$slot_id", snapshot.SlotId),
            ("$owner_id", snapshot.Player.PlayerId));

        foreach (InventoryItemSaveData item in snapshot.Inventory)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO inventory_items(" +
                "container_id, item_id, definition_id, original_definition_id, " +
                "quantity, durability) VALUES(" +
                "$container_id, $item_id, $definition_id, $original_definition_id, " +
                "$quantity, $durability);",
                ("$container_id", containerId),
                ("$item_id", item.ItemId),
                ("$definition_id", PersistedInventoryDefinitionCore(item)),
                ("$original_definition_id", PersistedInventoryOriginalCore(item)),
                ("$quantity", item.Quantity),
                ("$durability", item.Durability));
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM visited_planets WHERE slot_id = $slot_id;",
            ("$slot_id", snapshot.SlotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO visited_planets(" +
            "slot_id, planet_id, system_id, first_visited_utc, visit_count) " +
            "VALUES($slot_id, $planet_id, $system_id, $first_visited_utc, $visit_count);",
            ("$slot_id", snapshot.SlotId),
            ("$planet_id", snapshot.VisitedPlanet.PlanetId),
            ("$system_id", snapshot.VisitedPlanet.SystemId),
            ("$first_visited_utc", snapshot.VisitedPlanet.FirstVisitedUtc),
            ("$visit_count", snapshot.VisitedPlanet.VisitCount));

        transaction.Commit();
    }

    private static SaveGameSnapshot? LoadSnapshotCore(
        SqliteConnection connection,
        string slotId)
    {
        int schemaVersion = ExecuteScalarInt(
            connection,
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
        return LoadSnapshotCore(connection, slotId, schemaVersion);
    }

    private static SaveGameSnapshot? LoadSnapshotCore(
        SqliteConnection connection,
        string slotId,
        int schemaVersion)
    {
        int revision;
        int generatorVersion;
        int contentVersion;
        string updatedUtc;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT save_revision, generator_version, content_version, updated_utc " +
                "FROM save_meta WHERE slot_id = $slot_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            revision = reader.GetInt32(0);
            generatorVersion = reader.GetInt32(1);
            contentVersion = reader.GetInt32(2);
            updatedUtc = reader.GetString(3);
        }

        PlayerSaveData player;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT player_id, pos_x, pos_y, pos_z, current_planet_id " +
                "FROM player_state WHERE slot_id = $slot_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException("player_state row is missing.");
            }

            player = new PlayerSaveData(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetString(4));
        }

        ShipSaveData ship;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = schemaVersion >= 2
                ? "SELECT ship_id, template_id, original_template_id, " +
                  "display_name, health, fuel, pos_x, pos_y, pos_z " +
                  "FROM ships WHERE slot_id = $slot_id;"
                : "SELECT ship_id, template_id, NULL AS original_template_id, " +
                  "display_name, health, fuel, pos_x, pos_y, pos_z " +
                  "FROM ships WHERE slot_id = $slot_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException("ships row is missing.");
            }

            string persistedTemplateId = reader.GetString(1);
            string? originalTemplateId = reader.IsDBNull(2)
                ? null
                : reader.GetString(2);
            ContentResolution templateResolution = ResolveShipTemplateCore(
                persistedTemplateId,
                originalTemplateId);
            ship = new ShipSaveData(
                reader.GetString(0),
                templateResolution.EffectiveId,
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                templateResolution.OriginalId,
                templateResolution.State);
        }

        List<InventoryItemSaveData> inventory = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = schemaVersion >= 2
                ? "SELECT i.item_id, i.definition_id, i.original_definition_id, " +
                  "i.quantity, i.durability FROM inventory_items i " +
                  "JOIN containers c ON c.container_id = i.container_id " +
                  "WHERE c.slot_id = $slot_id ORDER BY i.item_id;"
                : "SELECT i.item_id, i.definition_id, " +
                  "NULL AS original_definition_id, i.quantity, i.durability " +
                  "FROM inventory_items i " +
                  "JOIN containers c ON c.container_id = i.container_id " +
                  "WHERE c.slot_id = $slot_id ORDER BY i.item_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string persistedDefinitionId = reader.GetString(1);
                string? originalDefinitionId = reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2);
                ContentResolution resolution = ResolveInventoryDefinitionCore(
                    persistedDefinitionId,
                    originalDefinitionId);
                inventory.Add(new InventoryItemSaveData(
                    reader.GetString(0),
                    resolution.EffectiveId,
                    reader.GetInt32(3),
                    reader.GetDouble(4),
                    resolution.OriginalId,
                    resolution.State));
            }
        }

        VisitedPlanetSaveData visitedPlanet;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT planet_id, system_id, first_visited_utc, visit_count " +
                "FROM visited_planets WHERE slot_id = $slot_id " +
                "ORDER BY planet_id LIMIT 1;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException("visited_planets row is missing.");
            }

            visitedPlanet = new VisitedPlanetSaveData(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3));
        }

        return new SaveGameSnapshot(
            slotId,
            revision,
            generatorVersion,
            contentVersion,
            updatedUtc,
            player,
            ship,
            inventory,
            visitedPlanet);
    }

    private SaveDatabaseDiagnostics ReadDiagnosticsCore(
        SqliteConnection connection,
        string slotId)
    {
        int schemaVersion = ExecuteScalarInt(
            connection,
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
        string journalMode = ExecuteScalarString(
            connection,
            "PRAGMA journal_mode;");
        bool foreignKeys = ExecuteScalarInt(
            connection,
            "PRAGMA foreign_keys;") == 1;
        int synchronousMode = ExecuteScalarInt(
            connection,
            "PRAGMA synchronous;");
        int busyTimeout = ExecuteScalarInt(
            connection,
            "PRAGMA busy_timeout;");
        string integrityResult = ExecuteScalarString(
            connection,
            "PRAGMA integrity_check;");

        int inventoryRows = string.IsNullOrWhiteSpace(slotId)
            ? 0
            : ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM inventory_items i " +
                "JOIN containers c ON c.container_id = i.container_id " +
                "WHERE c.slot_id = $slot_id;",
                ("$slot_id", slotId));
        int visitedRows = string.IsNullOrWhiteSpace(slotId)
            ? 0
            : ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM visited_planets WHERE slot_id = $slot_id;",
                ("$slot_id", slotId));

        long databaseBytes = File.Exists(_databasePath)
            ? new FileInfo(_databasePath).Length
            : 0L;

        SaveFileInspection backupInspection = InspectDatabaseFileCore(
            BackupPath,
            slotId,
            requireSnapshot: false);

        return new SaveDatabaseDiagnostics(
            schemaVersion,
            journalMode,
            foreignKeys,
            synchronousMode,
            busyTimeout,
            integrityResult,
            databaseBytes,
            inventoryRows,
            visitedRows,
            QueuedWrites,
            CompletedWrites,
            MaximumConcurrentWriters,
            backupInspection.Exists,
            backupInspection.Bytes,
            backupInspection.IntegrityResult);
    }

    private SavePrototypeAcceptanceReport BuildFailure(
        string result,
        SaveGameSnapshot? snapshot,
        string slotId,
        int concurrentWriteCount,
        int exactComparisons,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        SaveDatabaseDiagnostics diagnostics;
        try
        {
            using SqliteConnection connection = OpenConnection();
            diagnostics = ReadDiagnosticsCore(connection, slotId);
        }
        catch
        {
            diagnostics = EmptyDiagnostics();
        }

        return new SavePrototypeAcceptanceReport(
            false,
            result,
            snapshot,
            diagnostics,
            concurrentWriteCount,
            exactComparisons,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private SaveDatabaseDiagnostics EmptyDiagnostics()
    {
        return new SaveDatabaseDiagnostics(
            0,
            string.Empty,
            false,
            0,
            0,
            "unavailable",
            0,
            0,
            0,
            QueuedWrites,
            CompletedWrites,
            MaximumConcurrentWriters);
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static int ExecuteScalarInt(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        object? value = ExecuteScalar(connection, commandText, parameters);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string ExecuteScalarString(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        object? value = ExecuteScalar(connection, commandText, parameters);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void ExecutePragma(
        SqliteConnection connection,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteScalar();
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar();
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }

    private void UpdateMaximumConcurrentWriters(int active)
    {
        while (true)
        {
            int observed = Volatile.Read(ref _maximumConcurrentWriters);
            if (active <= observed)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _maximumConcurrentWriters,
                    active,
                    observed) == observed)
            {
                return;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SaveDatabase));
        }
    }
}
